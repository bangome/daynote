import { identify as identifyWithApple } from './apple';
import { uuid } from './bytes';
import { createDek, open, seal, sealText, toWire } from './dek';
import { accessTtlSeconds, refreshTtlDays, requireJwtSecret, type Env } from './env';
import { resolve as resolveEntitlement, toWire as entitlementToWire, trialEnd } from './entitlement';
import { identify } from './google';
import { ApiError, bearerToken, clientIp, json, noContent, readJsonObject } from './http';
import { issueAccessToken, verifyAccessToken } from './jwt';
import { SIGNIN_LIMITS, enforce } from './ratelimit';
import { issueSession, listDevices, revokeToken, rotateSession } from './sessions';
import { canonicalUtc } from './time';
import {
  deviceName,
  requireKdfParams,
  requireRawDek,
  requireClient,
  requireRedirectUri,
  requireString,
  requireWrappedDek,
} from './validate';

/**
 * Accounts and sessions, on Google sign-in and Sign in with Apple.
 *
 * There is no register endpoint and no password. The first successful sign-in for a subject
 * creates the account, so "sign up" and "sign in" are the same request — with an identity provider
 * there is nothing for a separate registration step to collect.
 *
 * An account belongs to exactly one provider. A Google account and an Apple account with the same
 * address are two accounts: linking them by address would hand one to whoever controls the other's
 * mailbox, and an Apple address is often a relay that matches nothing anyway.
 */

/** Which provider subject an account is found by. Column names, so never from input. */
type ProviderColumn = 'google_sub' | 'apple_sub';

/** Purpose label for the sealed Apple refresh token (dek.ts `sealText`). */
export const APPLE_REFRESH_PURPOSE = 'apple-refresh-token';

interface UserRow {
  id: string;
  /** Null for an account created through Apple. */
  google_sub: string | null;
  email: string;
  /** Null exactly when `protection` is 'passphrase': the server destroyed its copy. */
  wrapped_dek: string | null;
  protection: 'server' | 'passphrase';
  wrapped_dek_pw: string | null;
  wrapped_dek_rk: string | null;
  kdf_params: string | null;
}

const SELECT =
  `SELECT id, google_sub, email, wrapped_dek, protection, wrapped_dek_pw, wrapped_dek_rk,
          kdf_params
     FROM users`;

/**
 * What the client needs to get at its data key, in whichever custody the account chose.
 *
 * A locked account gets envelopes and nothing else. There is deliberately no branch that returns a
 * raw key for it: the server has none, and a response shape that could carry one would be the first
 * step back to holding a spare.
 */
async function keyMaterial(env: Env, user: UserRow): Promise<Record<string, unknown>> {
  if (user.protection === 'passphrase') {
    return {
      protection: 'passphrase',
      wrapped_dek_pw: user.wrapped_dek_pw,
      wrapped_dek_rk: user.wrapped_dek_rk,
      kdf_params: user.kdf_params === null ? null : JSON.parse(user.kdf_params),
    };
  }

  return {
    protection: 'server',
    // The key that decrypts this account's notes, over TLS. The client caches it locally so that
    // reading a note does not depend on the network.
    data_key: toWire(await open(env, user.wrapped_dek!)),
  };
}

async function findUserById(env: Env, id: string): Promise<UserRow | null> {
  return env.DB.prepare(`${SELECT} WHERE id = ?1`).bind(id).first<UserRow>();
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

/**
 * Finds the account for a provider subject, creating it on first sign-in.
 *
 * The lookup is by the subject, never by address: Google lets people change the address on an
 * account, Apple may hand out a relay, and matching on the address would either lose the account
 * or, worse, hand it to whoever holds the address now. The stored address is refreshed whenever the
 * provider sends one, so the settings panel shows the current one — and kept when it does not,
 * which Apple does on most sign-ins after the first. An account that has never had one stores ''
 * (the column is NOT NULL).
 *
 * `appleRefreshToken` is already sealed. It replaces the stored one when Apple sends a new one, so
 * the revoke at deletion uses the newest.
 */
async function upsertUser(
  env: Env,
  column: ProviderColumn,
  subject: string,
  email: string | null,
  now: Date,
  appleRefreshToken: string | null = null,
): Promise<UserRow> {
  const stamp = canonicalUtc(now);
  const existing = await env.DB.prepare(`${SELECT} WHERE ${column} = ?1`)
    .bind(subject)
    .first<UserRow>();

  if (existing !== null) {
    await env.DB.prepare(
      `UPDATE users
          SET email = COALESCE(?2, email), last_seen_utc = ?3,
              apple_refresh_token = COALESCE(?4, apple_refresh_token)
        WHERE id = ?1`,
    )
      .bind(existing.id, email, stamp, appleRefreshToken)
      .run();
    return { ...existing, email: email ?? existing.email };
  }

  const id = uuid();
  const dek = await createDek(env);
  try {
    await env.DB.prepare(
      `INSERT INTO users
         (id, ${column}, email, wrapped_dek, protection, apple_refresh_token, trial_ends_utc,
          created_utc, last_seen_utc)
       VALUES (?1, ?2, ?3, ?4, 'server', ?5, ?6, ?7, ?7)`,
    )
      .bind(id, subject, email ?? '', dek.wrapped, appleRefreshToken, trialEnd(now), stamp)
      .run();
  } catch (error) {
    if (String(error).includes('UNIQUE')) {
      // Two sign-ins for a brand-new account raced. The other one won; use its row.
      const raced = await env.DB.prepare(`${SELECT} WHERE ${column} = ?1`)
        .bind(subject)
        .first<UserRow>();
      if (raced !== null) {
        return raced;
      }
    }
    throw error;
  }

  return {
    id,
    google_sub: column === 'google_sub' ? subject : null,
    email: email ?? '',
    wrapped_dek: dek.wrapped,
    protection: 'server',
    wrapped_dek_pw: null,
    wrapped_dek_rk: null,
    kdf_params: null,
  };
}

async function sessionPayload(
  env: Env,
  user: UserRow,
  device: string,
  now: Date,
  includeDek: boolean,
): Promise<Record<string, unknown>> {
  const access = await issueAccessToken(requireJwtSecret(env), user.id, now, accessTtlSeconds(env));
  const session = await issueSession(env, user.id, device, now, refreshTtlDays(env));

  return {
    user_id: user.id,
    email: user.email,
    access_token: access.token,
    access_expires_epoch: access.expiresAtEpoch,
    refresh_token: session.token,
    refresh_expires_utc: session.expiresUtc,
    ...(includeDek ? await keyMaterial(env, user) : { protection: user.protection }),
    // Sent on every session response, so the app can show a trial countdown or a renewal prompt
    // without a second request. Store policy 10.8.4 requires telling people before a trial takes
    // functionality away, and the app cannot do that without knowing the date.
    entitlement: entitlementToWire(await resolveEntitlement(env, user.id, now)),
    server_utc: canonicalUtc(now),
  };
}

/**
 * Google sign-in. The app sends the authorization code from its loopback redirect together with the
 * PKCE verifier; the exchange with Google happens in `google.ts`.
 */
export async function google(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const code = requireString(body, 'code');
  const codeVerifier = requireString(body, 'code_verifier');
  const client = requireClient(body);
  const redirectUri = requireRedirectUri(body, client);
  const device = deviceName(body);

  // Counted before the exchange, so a flood of junk codes costs the sender its slots rather than
  // costing us a request to Google each time.
  await enforce(env, SIGNIN_LIMITS(clientIp(request)), now);

  const identity = await identify(env, code, codeVerifier, redirectUri, client);
  const user = await upsertUser(env, 'google_sub', identity.subject, identity.email, now);

  return json(await sessionPayload(env, user, device, now, true));
}

/**
 * Sign in with Apple, from the iPhone app. The app sends the authorization code from Apple's native
 * sheet and the raw nonce whose hash it gave Apple; the exchange happens in `apple.ts`.
 *
 * The response is exactly the Google one, so the app parses both with the same code.
 */
export async function apple(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const code = requireString(body, 'authorization_code');
  const nonce = requireString(body, 'nonce');
  const device = deviceName(body);

  // Counted before the exchange, for the same reason as Google's.
  await enforce(env, SIGNIN_LIMITS(clientIp(request)), now);

  const identity = await identifyWithApple(env, code, nonce, now);
  const sealedRefresh = identity.refreshToken === null
    ? null
    : await sealText(env, APPLE_REFRESH_PURPOSE, identity.refreshToken);
  const user = await upsertUser(env, 'apple_sub', identity.subject, identity.email, now, sealedRefresh);

  return json(await sessionPayload(env, user, device, now, true));
}

export async function refresh(request: Request, env: Env, now: Date): Promise<Response> {
  const body = await readJsonObject(request);
  const presented = requireString(body, 'refresh_token');

  const rotated = await rotateSession(env, presented, now, refreshTtlDays(env));
  const user = await findUserById(env, rotated.userId);
  if (user === null) {
    throw new ApiError('unauthorized', 'The refresh token is not valid.');
  }

  const access = await issueAccessToken(requireJwtSecret(env), user.id, now, accessTtlSeconds(env));

  return json({
    user_id: user.id,
    email: user.email,
    access_token: access.token,
    access_expires_epoch: access.expiresAtEpoch,
    refresh_token: rotated.session.token,
    refresh_expires_utc: rotated.session.expiresUtc,
    // No data key here: a refresh renews a session for a client that already has one. Handing the
    // key out on every rotation would widen its exposure for nothing.
    entitlement: entitlementToWire(await resolveEntitlement(env, user.id, now)),
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
    entitlement: entitlementToWire(await resolveEntitlement(env, user.id, now)),
    server_utc: canonicalUtc(now),
  });
}

/**
 * Re-issues the key material to a client that signed in earlier and lost its local copy — a restored
 * profile, a cleared credential store — without making it sign in with Google again. For a locked
 * account that means the envelopes, which are useless without the passphrase or recovery key.
 */
export async function dataKey(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  return json({ ...(await keyMaterial(env, user)), server_utc: canonicalUtc(now) });
}

/**
 * Turns the lock on: the client sends the data key wrapped under its passphrase and under a
 * one-time recovery key, and the server destroys its own copy.
 *
 * The destruction happens in the same UPDATE as the write, so there is no window where both exist.
 * A server that kept its copy "just in case" would be offering a lock with a spare key on the hook.
 */
export async function protect(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const body = await readJsonObject(request);

  const wrappedPw = requireWrappedDek(body, 'wrapped_dek_pw');
  const wrappedRk = requireWrappedDek(body, 'wrapped_dek_rk');
  const kdfParams = requireKdfParams(body);

  if (user.protection === 'passphrase') {
    // Re-wrapping under a new passphrase is a different operation; refusing here keeps this one
    // honest about what it does.
    throw new ApiError('bad_request', 'This account is already locked.');
  }

  await env.DB.prepare(
    `UPDATE users
        SET protection = 'passphrase', wrapped_dek_pw = ?2, wrapped_dek_rk = ?3, kdf_params = ?4,
            wrapped_dek = NULL
      WHERE id = ?1`,
  )
    .bind(user.id, wrappedPw, wrappedRk, kdfParams)
    .run();

  return json({ protection: 'passphrase', server_utc: canonicalUtc(now) });
}

/**
 * Turns the lock off, handing key custody back to the server. The client has to supply the raw data
 * key: the server cannot recover it on its own, which is the point of the lock.
 */
export async function unprotect(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const body = await readJsonObject(request);
  const raw = requireRawDek(body);

  if (user.protection !== 'passphrase') {
    throw new ApiError('bad_request', 'This account is not locked.');
  }

  await env.DB.prepare(
    `UPDATE users
        SET protection = 'server', wrapped_dek = ?2, wrapped_dek_pw = NULL, wrapped_dek_rk = NULL,
            kdf_params = NULL
      WHERE id = ?1`,
  )
    .bind(user.id, await seal(env, raw))
    .run();

  return json({ protection: 'server', server_utc: canonicalUtc(now) });
}
