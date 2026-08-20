import { uuid } from './bytes';
import { accessTtlSeconds, refreshTtlDays, requireJwtSecret, type Env } from './env';
import { ApiError, bearerToken, clientIp, json, noContent, readJsonObject } from './http';
import { issueAccessToken, verifyAccessToken } from './jwt';
import { LOGIN_LIMITS, REGISTER_LIMITS, enforce } from './ratelimit';
import {
  issueSession,
  listDevices,
  revokeAllForUser,
  revokeToken,
  rotateSession,
} from './sessions';
import { canonicalUtc } from './time';
import {
  deviceName,
  normalizeEmail,
  optionalWrappedDek,
  requireAuthKey,
  requireKdfParams,
  requireString,
  requireWrappedDek,
} from './validate';
import { createVerifier, verifyAuthKey, verifyDecoy } from './verifier';

interface UserRow {
  id: string;
  email: string;
  verifier: string;
  kdf_params: string;
  wrapped_dek_pw: string;
  wrapped_dek_rk: string | null;
  dek_generation: number;
  rewrap_pending: number;
}

async function findUser(env: Env, email: string): Promise<UserRow | null> {
  return env.DB.prepare(
    `SELECT id, email, verifier, kdf_params, wrapped_dek_pw, wrapped_dek_rk,
            dek_generation, rewrap_pending
       FROM users WHERE email = ?1`,
  )
    .bind(email)
    .first<UserRow>();
}

async function findUserById(env: Env, id: string): Promise<UserRow | null> {
  return env.DB.prepare(
    `SELECT id, email, verifier, kdf_params, wrapped_dek_pw, wrapped_dek_rk,
            dek_generation, rewrap_pending
       FROM users WHERE id = ?1`,
  )
    .bind(id)
    .first<UserRow>();
}

/** Resolves the caller from the Bearer access token, or throws 401. */
export async function authenticate(request: Request, env: Env, now: Date): Promise<UserRow> {
  const token = bearerToken(request);
  if (token === null) {
    throw new ApiError('unauthorized', 'An access token is required.');
  }

  const claims = await verifyAccessToken(requireJwtSecret(env), token, now);
  if (claims === null) {
    throw new ApiError('unauthorized', 'The access token is not valid.');
  }

  const user = await findUserById(env, claims.sub);
  if (user === null) {
    // The account was deleted while a token was still live.
    throw new ApiError('unauthorized', 'The access token is not valid.');
  }
  return user;
}

async function sessionPayload(
  env: Env,
  user: UserRow,
  device: string,
  now: Date,
  familyId?: string,
): Promise<Record<string, unknown>> {
  const access = await issueAccessToken(
    requireJwtSecret(env),
    user.id,
    now,
    accessTtlSeconds(env),
  );
  const session = await issueSession(env, user.id, device, now, refreshTtlDays(env), familyId);

  return {
    user_id: user.id,
    access_token: access.token,
    access_expires_epoch: access.expiresAtEpoch,
    refresh_token: session.token,
    refresh_expires_utc: session.expiresUtc,
    // Everything below is what the client needs to unlock its own data. The server cannot read any
    // of it; it is storage-and-forward only. See docs/CLOUD_SYNC.md §4.4.
    kdf_params: JSON.parse(user.kdf_params),
    wrapped_dek_pw: user.wrapped_dek_pw,
    // Also sealed against us. The client needs it to unlock after a password reset, and withholding
    // it would make the recovery key useless on a fresh device.
    wrapped_dek_rk: user.wrapped_dek_rk,
    dek_generation: user.dek_generation,
    rewrap_pending: user.rewrap_pending === 1,
    server_utc: canonicalUtc(now),
  };
}

export async function register(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const email = normalizeEmail(body);
  const authKey = requireAuthKey(body);
  const wrappedPw = requireWrappedDek(body, 'wrapped_dek_pw');
  const wrappedRk = optionalWrappedDek(body, 'wrapped_dek_rk');
  const kdfParams = requireKdfParams(body);

  await enforce(env, REGISTER_LIMITS(clientIp(request)), now);

  const verifier = await createVerifier(authKey);
  const id = uuid();

  try {
    await env.DB.prepare(
      `INSERT INTO users
         (id, email, verifier, kdf_params, wrapped_dek_pw, wrapped_dek_rk,
          dek_generation, rewrap_pending, created_utc)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, 1, 0, ?7)`,
    )
      .bind(id, email, verifier, kdfParams, wrappedPw, wrappedRk, canonicalUtc(now))
      .run();
  } catch (error) {
    if (String(error).includes('UNIQUE')) {
      throw new ApiError('email_taken', 'That email address is already registered.');
    }
    throw error;
  }

  return json({ user_id: id, recovery_key_set: wrappedRk !== null }, 201);
}

export async function login(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const email = normalizeEmail(body);
  const authKey = requireAuthKey(body);
  const device = deviceName(body);

  // Count the attempt before checking the credential, so a wrong guess still costs a slot.
  await enforce(env, LOGIN_LIMITS(email, clientIp(request)), now);

  const user = await findUser(env, email);
  if (user === null) {
    // Spend the same CPU as a real verification so an unknown email is not detectable by timing,
    // and return the identical error the wrong-password path returns.
    await verifyDecoy(authKey);
    throw new ApiError('invalid_credentials', 'The email or password is incorrect.');
  }

  if (!(await verifyAuthKey(user.verifier, authKey))) {
    throw new ApiError('invalid_credentials', 'The email or password is incorrect.');
  }

  return json(await sessionPayload(env, user, device, now));
}

export async function refresh(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const presented = requireString(body, 'refresh_token');

  const rotated = await rotateSession(env, presented, now, refreshTtlDays(env));
  const user = await findUserById(env, rotated.userId);
  if (user === null) {
    throw new ApiError('unauthorized', 'The refresh token is not valid.');
  }

  const access = await issueAccessToken(
    requireJwtSecret(env),
    user.id,
    now,
    accessTtlSeconds(env),
  );

  return json({
    user_id: user.id,
    access_token: access.token,
    access_expires_epoch: access.expiresAtEpoch,
    refresh_token: rotated.session.token,
    refresh_expires_utc: rotated.session.expiresUtc,
    kdf_params: JSON.parse(user.kdf_params),
    wrapped_dek_pw: user.wrapped_dek_pw,
    wrapped_dek_rk: user.wrapped_dek_rk,
    dek_generation: user.dek_generation,
    rewrap_pending: user.rewrap_pending === 1,
    server_utc: canonicalUtc(now),
  });
}

export async function logout(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  await revokeToken(env, requireString(body, 'refresh_token'), now);
  // Always 204: whether that token existed is not the caller's business.
  return noContent();
}

export async function me(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  return json({
    user_id: user.id,
    email: user.email,
    devices: await listDevices(env, user.id, now),
    recovery_key_set: user.wrapped_dek_rk !== null,
    // Returned here too, so unlocking after a reset does not have to spend a refresh-token rotation
    // just to read an envelope the server cannot open anyway.
    wrapped_dek_rk: user.wrapped_dek_rk,
    rewrap_pending: user.rewrap_pending === 1,
    dek_generation: user.dek_generation,
    server_utc: canonicalUtc(now),
  });
}

/**
 * Password change for a user who knows their current password.
 *
 * `current_auth_key` is required in addition to the access token. Without it, a stolen 15-minute
 * access token would be enough to change the password and lock the owner out. The client re-wraps
 * the DEK locally under the new KEK and sends the new envelope; the server never sees either key.
 */
export async function changePassword(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const body = await readJsonObject(request);

  const currentAuthKey = requireAuthKey(body, 'current_auth_key');
  const newAuthKey = requireAuthKey(body, 'new_auth_key');
  const newWrapped = requireWrappedDek(body, 'new_wrapped_dek_pw');
  const kdfParams = requireKdfParams(body);

  if (!(await verifyAuthKey(user.verifier, currentAuthKey))) {
    throw new ApiError('invalid_credentials', 'The current password is incorrect.');
  }

  await env.DB.prepare(
    `UPDATE users
        SET verifier = ?2, kdf_params = ?3, wrapped_dek_pw = ?4,
            dek_generation = dek_generation + 1, rewrap_pending = 0
      WHERE id = ?1`,
  )
    .bind(user.id, await createVerifier(newAuthKey), kdfParams, newWrapped)
    .run();

  // Every other device must sign in again with the new password; this device gets a fresh session
  // in the same response so the user is not bounced out of the app they just used.
  await revokeAllForUser(env, user.id, now);
  const refreshed = await findUserById(env, user.id);
  if (refreshed === null) {
    throw new ApiError('server_error', 'The account disappeared mid-update.');
  }

  return json(await sessionPayload(env, refreshed, deviceName(body), now));
}
