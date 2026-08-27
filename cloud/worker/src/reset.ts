import { authenticate } from './auth';
import { randomBytes, sha256Hex, timingSafeEqual } from './bytes';
import { emailSenderFor, resetEmail } from './email';
import { ApiError, clientIp, json, noContent, readJsonObject } from './http';
import { enforce } from './ratelimit';
import { revokeAllForUser } from './sessions';
import { addSeconds, canonicalUtc } from './time';
import { normalizeEmail, requireAuthKey, requireKdfParams, requireString, requireWrappedDek } from './validate';
import { createVerifier } from './verifier';
import type { Env } from './env';

/**
 * Password reset, and the re-wrap that follows it.
 *
 * Reset restores *account* access. It cannot restore *data* access: the server has no way to re-wrap
 * a key it cannot read. So `/reset/confirm` rotates the verifier and flags the account, and the
 * client supplies a freshly wrapped data key through `/rewrap` — from the recovery key, or from a
 * device that still has the key cached. See docs/CLOUD_SYNC.md §4.8.
 */

const CODE_TTL_MINUTES = 30;

/**
 * Crockford base32, excluding the letters people mistranscribe. Eight characters is 40 bits — far
 * beyond guessing at five attempts per token, and still short enough to read off a phone.
 */
const ALPHABET = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
const CODE_LENGTH = 8;
const MAX_ATTEMPTS = 5;

interface TokenRow {
  token_hash: string;
  user_id: string;
  attempts: number;
  expires_utc: string;
  used_utc: string | null;
}

function generateCode(): string {
  // Rejection-free: 32 is a divisor of 256, so masking to 5 bits is uniform.
  const bytes = randomBytes(CODE_LENGTH);
  let code = '';
  for (const byte of bytes) {
    code += ALPHABET[byte & 0x1f];
  }
  return `${code.slice(0, 4)}-${code.slice(4)}`;
}

/** Accepts the code in any case, with or without the separator, mapping the confusable letters. */
function normalizeCode(value: string): string | null {
  let normalized = '';
  for (const character of value) {
    if (character === '-' || character === ' ') {
      continue;
    }

    let upper = character.toUpperCase();
    if (upper === 'I' || upper === 'L') {
      upper = '1';
    } else if (upper === 'O') {
      upper = '0';
    } else if (upper === 'U') {
      upper = 'V';
    }

    if (!ALPHABET.includes(upper)) {
      return null;
    }
    normalized += upper;
  }

  return normalized.length === CODE_LENGTH ? normalized : null;
}

export async function requestReset(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const email = normalizeEmail(body);

  // Per-IP and per-email, because this endpoint sends mail: without a limit it is both an
  // enumeration probe and a way to have us spam someone else's inbox.
  await enforce(
    env,
    [
      { action: 'reset', scope: 'email', value: email, max: 3 },
      { action: 'reset', scope: 'ip', value: clientIp(request), max: 10 },
    ],
    now,
  );

  const user = await env.DB.prepare('SELECT id FROM users WHERE email = ?1')
    .bind(email)
    .first<{ id: string }>();

  if (user !== null) {
    const code = generateCode();
    const normalized = normalizeCode(code)!;

    await env.DB.batch([
      // One live code per account: requesting a new one invalidates the previous, so an old email
      // cannot be replayed after the user asks again.
      env.DB.prepare('DELETE FROM reset_tokens WHERE user_id = ?1').bind(user.id),
      env.DB.prepare(
        `INSERT INTO reset_tokens(token_hash, user_id, attempts, expires_utc, used_utc)
         VALUES (?1, ?2, 0, ?3, NULL)`,
      ).bind(
        await sha256Hex(normalized),
        user.id,
        canonicalUtc(addSeconds(now, CODE_TTL_MINUTES * 60)),
      ),
    ]);

    // Awaited, not fire-and-forget: a provider outage must surface as a 500 the operator can see,
    // rather than as a user waiting forever for an email that was never sent.
    await emailSenderFor(env).send(resetEmail(email, code, CODE_TTL_MINUTES));
  }

  // Always 204, whether or not the account exists. Anything else turns this into an
  // account-enumeration endpoint that also happens to send mail.
  return noContent();
}

export async function confirmReset(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const email = normalizeEmail(body);
  const newAuthKey = requireAuthKey(body, 'new_auth_key');
  const kdfParams = requireKdfParams(body);
  const presented = normalizeCode(requireString(body, 'reset_token'));

  await enforce(
    env,
    [{ action: 'reset-confirm', scope: 'ip', value: clientIp(request), max: 20 }],
    now,
  );

  if (presented === null) {
    throw new ApiError('bad_request', 'That reset code is not in the expected format.');
  }

  const user = await env.DB.prepare('SELECT id FROM users WHERE email = ?1')
    .bind(email)
    .first<{ id: string }>();
  if (user === null) {
    throw new ApiError('invalid_credentials', 'That reset code is not valid.');
  }

  const row = await env.DB.prepare(
    `SELECT token_hash, user_id, attempts, expires_utc, used_utc
       FROM reset_tokens WHERE user_id = ?1`,
  )
    .bind(user.id)
    .first<TokenRow>();

  const nowUtc = canonicalUtc(now);
  if (
    row === null ||
    row.used_utc !== null ||
    row.expires_utc <= nowUtc ||
    row.attempts >= MAX_ATTEMPTS
  ) {
    throw new ApiError('invalid_credentials', 'That reset code is not valid.');
  }

  const presentedHash = await sha256Hex(presented);
  const encoder = new TextEncoder();
  if (!timingSafeEqual(encoder.encode(presentedHash), encoder.encode(row.token_hash))) {
    // Count the miss before answering, so a wrong guess costs an attempt even if the client hangs up.
    await env.DB.prepare('UPDATE reset_tokens SET attempts = attempts + 1 WHERE user_id = ?1')
      .bind(user.id)
      .run();
    throw new ApiError('invalid_credentials', 'That reset code is not valid.');
  }

  await env.DB.batch([
    // The verifier rotates and every session dies, but `wrapped_dek_pw` is left exactly as it was:
    // the server cannot re-wrap a key it cannot read, and overwriting it with anything would destroy
    // the only copy the recovery key can still open.
    env.DB.prepare(
      `UPDATE users
          SET verifier = ?2, kdf_params = ?3, rewrap_pending = 1
        WHERE id = ?1`,
    ).bind(user.id, await createVerifier(newAuthKey), kdfParams),
    env.DB.prepare('UPDATE reset_tokens SET used_utc = ?2 WHERE user_id = ?1').bind(user.id, nowUtc),
  ]);

  await revokeAllForUser(env, user.id, now);

  return json({
    // Said plainly in the response, because this is the moment the client has to explain it.
    rewrap_pending: true,
    message: 'The password was changed. The cloud copy stays locked until the data key is re-wrapped.',
  });
}

/**
 * Supplies a data key wrapped under the new password's key-encryption key, clearing the locked state.
 * Authenticated, so only someone who can sign in with the new password can do it.
 */
export async function rewrap(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const body = await readJsonObject(request);
  const wrapped = requireWrappedDek(body, 'new_wrapped_dek_pw');

  const generation = body['dek_generation'];
  if (typeof generation !== 'number' || !Number.isSafeInteger(generation) || generation < 1) {
    throw new ApiError('bad_request', "Field 'dek_generation' must be a positive integer.");
  }

  // Optimistic check: two devices unlocking at once would otherwise race, and the loser would
  // overwrite a perfectly good envelope with one wrapped under a key it no longer holds.
  const updated = await env.DB.prepare(
    `UPDATE users
        SET wrapped_dek_pw = ?2, dek_generation = dek_generation + 1, rewrap_pending = 0
      WHERE id = ?1 AND dek_generation = ?3`,
  )
    .bind(user.id, wrapped, generation)
    .run();

  if ((updated.meta.changes ?? 0) === 0) {
    throw new ApiError(
      'bad_request',
      'The account was updated elsewhere. Sign in again and retry.',
    );
  }

  return json({ dek_generation: generation + 1, rewrap_pending: false });
}
