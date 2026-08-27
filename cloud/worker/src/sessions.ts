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
 */

const TOKEN_BYTES = 32;

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
  const row = await env.DB.prepare(
    `SELECT token_hash, user_id, family_id, device_name, expires_utc, revoked_utc
       FROM refresh_tokens WHERE token_hash = ?1`,
  )
    .bind(presentedHash)
    .first<TokenRow>();

  if (row === null) {
    throw new ApiError('unauthorized', 'The refresh token is not valid.');
  }

  if (row.revoked_utc !== null) {
    // Reuse of a rotated token: assume theft and burn the whole chain, including the copy the
    // legitimate client is holding. Forcing a fresh sign-in is the correct outcome here.
    await revokeFamily(env, row.family_id, now);
    throw new ApiError('unauthorized', 'The refresh token is not valid.');
  }

  if (row.expires_utc <= canonicalUtc(now)) {
    throw new ApiError('unauthorized', 'The refresh token is not valid.');
  }

  const { token, hashPromise } = newToken();
  const nowUtc = canonicalUtc(now);
  const expiresUtc = canonicalUtc(addSeconds(now, ttlDays * DAY_SECONDS));

  // One batch so a crash cannot leave the old token revoked without a replacement, which would log
  // the user out silently.
  await env.DB.batch([
    env.DB.prepare('UPDATE refresh_tokens SET revoked_utc = ?2 WHERE token_hash = ?1')
      .bind(presentedHash, nowUtc),
    env.DB.prepare(
      `INSERT INTO refresh_tokens
         (token_hash, user_id, family_id, device_name, issued_utc, expires_utc, revoked_utc)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, NULL)`,
    ).bind(await hashPromise, row.user_id, row.family_id, row.device_name, nowUtc, expiresUtc),
  ]);

  return {
    userId: row.user_id,
    session: { token, familyId: row.family_id, expiresUtc },
  };
}

export async function revokeToken(env: Env, presentedToken: string, now: Date): Promise<void> {
  await env.DB.prepare(
    'UPDATE refresh_tokens SET revoked_utc = ?2 WHERE token_hash = ?1 AND revoked_utc IS NULL',
  )
    .bind(await sha256Hex(presentedToken), canonicalUtc(now))
    .run();
}

export async function revokeFamily(env: Env, familyId: string, now: Date): Promise<void> {
  await env.DB.prepare(
    'UPDATE refresh_tokens SET revoked_utc = ?2 WHERE family_id = ?1 AND revoked_utc IS NULL',
  )
    .bind(familyId, canonicalUtc(now))
    .run();
}

/** Used by password change and (later) password reset: every device must sign in again. */
export async function revokeAllForUser(env: Env, userId: string, now: Date): Promise<void> {
  await env.DB.prepare(
    'UPDATE refresh_tokens SET revoked_utc = ?2 WHERE user_id = ?1 AND revoked_utc IS NULL',
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
