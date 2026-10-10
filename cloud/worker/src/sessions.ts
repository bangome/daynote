import { randomBytes, sha256Hex, toBase64Url, uuid } from './bytes';
import { ApiError } from './http';
import { DAY_SECONDS, addSeconds, canonicalUtc } from './time';
import type { Env } from './env';

/**
 * Refresh-token sessions.
 *
 * Only the SHA-256 of a token is stored, so a leaked `refresh_tokens` table yields nothing usable.
 * Tokens rotate on every refresh, and presenting a token that has already been rotated revokes the
 * whole family: that is the theft signal. A stolen token either gets used before the real client
 * refreshes (and the real client's next refresh kills the family) or after (and the thief's attempt
 * kills it). Either way the window closes instead of staying open for the full 60 days.
 *
 * Tokens are high-entropy random values, so a plain SHA-256 is the right hash here — a slow KDF
 * would buy nothing against a 256-bit random preimage.
 *
 * One exception to the theft rule: a client that never received the rotated token. A phone loses
 * the response when the OS suspends the app mid-request or the request times out after the server
 * has already rotated, and then presents the old token again. Within {@link REUSE_GRACE_SECONDS} of
 * the rotation, and only while its successor has never been used, that is answered with a fresh pair
 * in the same family (the unused successor is retired) rather than by revoking the family.
 *
 * Every retirement is a conditional `UPDATE … WHERE revoked_utc IS NULL` batched with an `INSERT`
 * that only happens if that update set `replaced_by` to the new token's hash. A D1 batch is one
 * transaction, so of two concurrent requests retiring the same token exactly one issues a successor:
 * there is never a fork into two live chains. Only a token retired by an ordinary rotation
 * (`revoke_reason = 'rotated'`) is grace-eligible, never one the grace itself retired, so the grace
 * cannot be chained.
 */

const TOKEN_BYTES = 32;

/**
 * How long after a rotation the old token may be presented again (see above). Short on purpose:
 * it only has to cover a lost response and the client's immediate retry.
 */
export const REUSE_GRACE_SECONDS = 60;

type RevokeReason = 'rotated' | 'grace_retired' | 'logout' | 'family';

export interface IssuedSession {
  readonly token: string;
  readonly familyId: string;
  readonly expiresUtc: string;
}

interface TokenRow {
  token_hash: string;
  user_id: string;
  family_id: string;
  device_name: string;
  expires_utc: string;
  revoked_utc: string | null;
  revoke_reason: string | null;
  replaced_by: string | null;
}

function newToken(): { token: string; hashPromise: Promise<string> } {
  const token = toBase64Url(randomBytes(TOKEN_BYTES));
  return { token, hashPromise: sha256Hex(token) };
}

export async function issueSession(
  env: Env,
  userId: string,
  device: string,
  now: Date,
  ttlDays: number,
  familyId: string = uuid(),
): Promise<IssuedSession> {
  const { token, hashPromise } = newToken();
  const expiresUtc = canonicalUtc(addSeconds(now, ttlDays * DAY_SECONDS));

  await env.DB.prepare(
    `INSERT INTO refresh_tokens
       (token_hash, user_id, family_id, device_name, issued_utc, expires_utc, revoked_utc)
     VALUES (?1, ?2, ?3, ?4, ?5, ?6, NULL)`,
  )
    .bind(await hashPromise, userId, familyId, device, canonicalUtc(now), expiresUtc)
    .run();

  return { token, familyId, expiresUtc };
}

/**
 * Validates and rotates a refresh token in one step. Throws `unauthorized` for every failure mode
 * without saying which, so a caller cannot probe for valid-but-expired versus unknown tokens.
 */
export async function rotateSession(
  env: Env,
  presentedToken: string,
  now: Date,
  ttlDays: number,
): Promise<{ userId: string; session: IssuedSession }> {
  const presentedHash = await sha256Hex(presentedToken);

  // A second pass only follows a rotation lost to a concurrent request for the same token; by then
  // the token is revoked, so the second pass never rotates and the loop cannot go round again.
  for (let pass = 0; pass < 2; pass += 1) {
    const row = await env.DB.prepare(
      `SELECT token_hash, user_id, family_id, device_name, expires_utc, revoked_utc, revoke_reason,
              replaced_by
         FROM refresh_tokens WHERE token_hash = ?1`,
    )
      .bind(presentedHash)
      .first<TokenRow>();

    if (row === null) {
      throw new ApiError('unauthorized', 'The refresh token is not valid.');
    }

    if (row.revoked_utc === null) {
      if (row.expires_utc <= canonicalUtc(now)) {
        throw new ApiError('unauthorized', 'The refresh token is not valid.');
      }
      const session = await retireAndIssue(env, row, presentedHash, 'rotated', now, ttlDays);
      if (session !== null) {
        return { userId: row.user_id, session };
      }
      // Another request rotated this token first. Read it again: its rotation is now what decides
      // between the grace and a revocation.
      continue;
    }

    // A retry inside the grace window retires the presented token's unused successor, which the
    // client never received. The retirement only succeeds while that successor is still live, so
    // a successor that has been used, or a second retry racing this one, falls through to theft.
    if (graceEligible(row, now) && row.expires_utc > canonicalUtc(now)) {
      const session = await retireAndIssue(env, row, row.replaced_by!, 'grace_retired', now, ttlDays);
      if (session !== null) {
        console.log(
          `refresh grace applied: user ${row.user_id.slice(0, 8)} family ${row.family_id.slice(0, 8)}`,
        );
        return { userId: row.user_id, session };
      }
    }

    // Reuse of a rotated token: assume theft and burn the whole chain, including the copy the
    // legitimate client is holding. Forcing a fresh sign-in is the correct outcome here.
    console.warn(
      `refresh token reuse, family revoked: user ${row.user_id.slice(0, 8)} family ${row.family_id.slice(0, 8)}`,
    );
    await revokeFamily(env, row.family_id, now);
    throw new ApiError('unauthorized', 'The refresh token is not valid.');
  }

  throw new ApiError('unauthorized', 'The refresh token is not valid.');
}

/**
 * A token retired by an ordinary rotation less than {@link REUSE_GRACE_SECONDS} ago. Logged-out,
 * family-revoked and grace-retired tokens never qualify, nor does a row revoked before
 * `revoke_reason` existed.
 */
function graceEligible(row: TokenRow, now: Date): boolean {
  return row.revoke_reason === 'rotated'
    && row.replaced_by !== null
    && row.revoked_utc! >= canonicalUtc(addSeconds(now, -REUSE_GRACE_SECONDS));
}

/**
 * Revokes `retiring` and issues its replacement in `row`'s family, or does nothing and returns null
 * when `retiring` was already revoked. The insert reads the new hash back from `replaced_by`, which
 * only this call's update can have written, so it happens exactly when the update did. One batch, so
 * a crash cannot leave the old token revoked without a replacement either.
 */
async function retireAndIssue(
  env: Env,
  row: TokenRow,
  retiring: string,
  reason: RevokeReason,
  now: Date,
  ttlDays: number,
): Promise<IssuedSession | null> {
  const { token, hashPromise } = newToken();
  const hash = await hashPromise;
  const nowUtc = canonicalUtc(now);
  const expiresUtc = canonicalUtc(addSeconds(now, ttlDays * DAY_SECONDS));

  const [, inserted] = await env.DB.batch([
    env.DB.prepare(
      `UPDATE refresh_tokens SET revoked_utc = ?2, revoke_reason = ?3, replaced_by = ?4
        WHERE token_hash = ?1 AND revoked_utc IS NULL`,
    ).bind(retiring, nowUtc, reason, hash),
    env.DB.prepare(
      `INSERT INTO refresh_tokens
         (token_hash, user_id, family_id, device_name, issued_utc, expires_utc, revoked_utc)
       SELECT ?1, ?2, ?3, ?4, ?5, ?6, NULL
         FROM refresh_tokens WHERE token_hash = ?7 AND replaced_by = ?1`,
    ).bind(hash, row.user_id, row.family_id, row.device_name, nowUtc, expiresUtc, retiring),
  ]);

  return inserted?.meta.changes === 1 ? { token, familyId: row.family_id, expiresUtc } : null;
}

/**
 * Logout revokes the presented token's whole family, not just the token: a token that has already
 * been rotated would otherwise leave its successor alive for the rest of its 60 days.
 */
export async function revokeToken(env: Env, presentedToken: string, now: Date): Promise<void> {
  await env.DB.prepare(
    `UPDATE refresh_tokens SET revoked_utc = ?2, revoke_reason = 'logout'
      WHERE family_id = (SELECT family_id FROM refresh_tokens WHERE token_hash = ?1)
        AND revoked_utc IS NULL`,
  )
    .bind(await sha256Hex(presentedToken), canonicalUtc(now))
    .run();
}

export async function revokeFamily(env: Env, familyId: string, now: Date): Promise<void> {
  await env.DB.prepare(
    `UPDATE refresh_tokens SET revoked_utc = ?2, revoke_reason = 'family'
      WHERE family_id = ?1 AND revoked_utc IS NULL`,
  )
    .bind(familyId, canonicalUtc(now))
    .run();
}

/** Used by password change and (later) password reset: every device must sign in again. */
export async function revokeAllForUser(env: Env, userId: string, now: Date): Promise<void> {
  await env.DB.prepare(
    `UPDATE refresh_tokens SET revoked_utc = ?2, revoke_reason = 'family'
      WHERE user_id = ?1 AND revoked_utc IS NULL`,
  )
    .bind(userId, canonicalUtc(now))
    .run();
}

export interface DeviceSummary {
  device_name: string;
  issued_utc: string;
  expires_utc: string;
}

export async function listDevices(env: Env, userId: string, now: Date): Promise<DeviceSummary[]> {
  const { results } = await env.DB.prepare(
    `SELECT device_name, MAX(issued_utc) AS issued_utc, MAX(expires_utc) AS expires_utc
       FROM refresh_tokens
      WHERE user_id = ?1 AND revoked_utc IS NULL AND expires_utc > ?2
      GROUP BY family_id, device_name
      ORDER BY issued_utc DESC`,
  )
    .bind(userId, canonicalUtc(now))
    .all<DeviceSummary>();

  return results;
}
