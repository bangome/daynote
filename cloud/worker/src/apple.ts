import { fromBase64Url, sha256Hex, toBase64Url } from './bytes';
import { ApiError } from './http';
import type { Env } from './env';

/**
 * Sign in with Apple, for the iPhone app (App Store guideline 4.8: an app that offers Google
 * sign-in has to offer this too).
 *
 * The shape mirrors google.ts on purpose. The app runs Apple's native sheet, gets an authorization
 * code, and posts it here; the Worker redeems it at Apple's token endpoint, so the only id_token
 * this code ever reads is one that came straight from Apple over TLS. The app also gets an
 * id_token from the sheet, and it is deliberately not what we trust: a token the client hands us
 * would need its signature checked against Apple's JWKS, while a token fetched from Apple ourselves
 * does not — the same reasoning, and the same trade, as the Google flow.
 *
 * What differs from Google is the client secret. Apple issues no static one: the secret is a JWT
 * the Worker signs itself, ES256, with a key downloaded once from the developer console. It lives
 * for a few minutes and is minted per call, so there is no long-lived secret to rotate beyond the
 * key itself.
 *
 * The nonce is the replay guard for a code that could otherwise be lifted from one attempt and
 * redeemed in another. The app hashes a random value (lowercase hex SHA-256) into the request it
 * shows Apple, and posts the raw value here; Apple copies the hash into the id_token, and the two
 * must agree.
 */

const TOKEN_ENDPOINT = 'https://appleid.apple.com/auth/token';
const REVOKE_ENDPOINT = 'https://appleid.apple.com/auth/revoke';
const ISSUER = 'https://appleid.apple.com';

/** Apple allows up to 180 days; a secret minted per call needs minutes, and leaks less if logged. */
const CLIENT_SECRET_TTL_SECONDS = 5 * 60;

interface AppleConfig {
  readonly bundleId: string;
  readonly teamId: string;
  readonly keyId: string;
  readonly privateKey: string;
}

function configured(value: string | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/** The four values, or null when any is missing — a deployment state, not a bug. */
function readConfig(env: Env): AppleConfig | null {
  const { APPLE_BUNDLE_ID, APPLE_TEAM_ID, APPLE_KEY_ID, APPLE_PRIVATE_KEY } = env;
  if (!configured(APPLE_BUNDLE_ID) || !configured(APPLE_TEAM_ID) || !configured(APPLE_KEY_ID) ||
    !configured(APPLE_PRIVATE_KEY)) {
    return null;
  }
  return {
    bundleId: APPLE_BUNDLE_ID.trim(),
    teamId: APPLE_TEAM_ID.trim(),
    keyId: APPLE_KEY_ID.trim(),
    privateKey: APPLE_PRIVATE_KEY,
  };
}

/**
 * The same refusal an unconfigured Google phone client gets (google.ts): a 400 the app can show,
 * not a 500, because nothing is broken — the server has simply not been given the key yet.
 */
function requireConfig(env: Env): AppleConfig {
  const config = readConfig(env);
  if (config === null) {
    throw new ApiError('bad_request', 'Signing in with Apple is not configured on this server yet.');
  }
  return config;
}

/**
 * Imports the `.p8` key. Accepts the PEM as downloaded, and also with its line breaks flattened to
 * literal `\n` — which is what a PEM pasted into a one-line secret prompt often turns into.
 */
export async function importPrivateKey(pem: string, name = 'APPLE_PRIVATE_KEY'): Promise<CryptoKey> {
  const body = pem
    .replaceAll('\\n', '\n')
    .replace(/-----(BEGIN|END) PRIVATE KEY-----/g, '')
    .replace(/\s+/g, '');
  const der = fromBase64Url(body.replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, ''));
  if (der === null) {
    throw new Error(`${name} is not a PKCS#8 PEM. Run: wrangler secret put ${name}`);
  }
  return crypto.subtle.importKey(
    'pkcs8',
    der as BufferSource,
    { name: 'ECDSA', namedCurve: 'P-256' },
    false,
    ['sign'],
  );
}

export function encodeJson(value: unknown): string {
  return toBase64Url(new TextEncoder().encode(JSON.stringify(value)));
}

/**
 * The client secret: an ES256 JWT naming our team, the app, and Apple as audience.
 *
 * WebCrypto's ECDSA signature is already the raw `r || s` that JWS wants, so there is no DER to
 * unpack — the one place hand-rolled ES256 usually goes wrong.
 */
async function clientSecret(config: AppleConfig, now: Date): Promise<string> {
  const iat = Math.floor(now.getTime() / 1000);
  const signingInput = `${encodeJson({ alg: 'ES256', kid: config.keyId })}.${encodeJson({
    iss: config.teamId,
    iat,
    exp: iat + CLIENT_SECRET_TTL_SECONDS,
    aud: ISSUER,
    sub: config.bundleId,
  })}`;

  const signature = await crypto.subtle.sign(
    { name: 'ECDSA', hash: 'SHA-256' },
    await importPrivateKey(config.privateKey),
    new TextEncoder().encode(signingInput),
  );
  return `${signingInput}.${toBase64Url(new Uint8Array(signature))}`;
}

export interface AppleIdentity {
  /** The `sub` claim: stable for this developer team, never reused, and the only identity. */
  readonly subject: string;
  /**
   * Present on the first sign-in and usually after, absent sometimes; may be a private-relay
   * address. Display and support only — never matched on.
   */
  readonly email: string | null;
  /** Apple's refresh token, kept sealed for the revoke deletion requires. */
  readonly refreshToken: string | null;
}

interface IdTokenClaims {
  iss?: string;
  aud?: string;
  sub?: string;
  exp?: number;
  nonce?: string;
  email?: string;
  email_verified?: boolean | string;
}

/**
 * Reads the claims of an id_token fetched from Apple's own token endpoint. Not signature-checked,
 * for the reason in the header; the claims are, because they say which account, which app, and
 * which attempt.
 */
async function readClaims(
  idToken: string,
  bundleId: string,
  rawNonce: string,
  now: Date,
): Promise<{ subject: string; email: string | null }> {
  const parts = idToken.split('.');
  const decoded = parts.length === 3 ? fromBase64Url(parts[1]!) : null;
  if (decoded === null) {
    throw new ApiError('server_error', 'Apple returned a malformed ID token.');
  }

  let claims: IdTokenClaims;
  try {
    claims = JSON.parse(new TextDecoder().decode(decoded)) as IdTokenClaims;
  } catch {
    throw new ApiError('server_error', 'Apple returned an unreadable ID token.');
  }

  if (claims.iss !== ISSUER) {
    throw new ApiError('server_error', 'The ID token was not issued by Apple.');
  }
  if (claims.aud !== bundleId) {
    // A deployment mistake — APPLE_BUNDLE_ID and the app's bundle disagree — not the user's.
    throw new ApiError('server_error', 'The ID token was issued for a different app.');
  }
  if (typeof claims.exp !== 'number' || claims.exp * 1000 <= now.getTime()) {
    throw new ApiError('invalid_credentials', 'The Apple sign-in expired. Try again.');
  }
  if (typeof claims.sub !== 'string' || claims.sub.length === 0) {
    throw new ApiError('server_error', 'The ID token carried no subject.');
  }
  if (typeof claims.nonce !== 'string' || claims.nonce !== (await sha256Hex(rawNonce))) {
    // The code was issued for a different attempt than the one presenting it.
    throw new ApiError('invalid_credentials', 'That Apple sign-in does not match this attempt. Try again.');
  }

  // Apple only ever returns addresses it has verified, relay or real; an explicit "false" is the
  // one case to refuse, and even then only the address is dropped, not the sign-in.
  const unverified = claims.email_verified === false || claims.email_verified === 'false';
  const email = typeof claims.email === 'string' && claims.email.trim().length > 0 && !unverified
    ? claims.email.trim().toLowerCase()
    : null;

  return { subject: claims.sub, email };
}

/** Redeems the authorization code from the app and returns who signed in. */
export async function identify(
  env: Env,
  code: string,
  rawNonce: string,
  now: Date,
): Promise<AppleIdentity> {
  const config = requireConfig(env);

  const response = await fetch(TOKEN_ENDPOINT, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      code,
      client_id: config.bundleId,
      client_secret: await clientSecret(config, now),
    }),
  });

  const body = (await response.json().catch(() => ({}))) as Record<string, unknown>;
  if (!response.ok) {
    // As with Google: a used or expired code is the caller's problem, not a server fault.
    if (body['error'] === 'invalid_grant') {
      throw new ApiError('invalid_credentials', 'That Apple sign-in is no longer valid. Try again.');
    }
    console.error('apple token exchange failed', response.status, JSON.stringify(body).slice(0, 300));
    throw new ApiError('server_error', 'Apple would not complete the sign-in.');
  }

  const idToken = body['id_token'];
  if (typeof idToken !== 'string') {
    throw new ApiError('server_error', 'Apple returned no ID token.');
  }

  const identity = await readClaims(idToken, config.bundleId, rawNonce, now);
  const refreshToken = body['refresh_token'];
  return {
    ...identity,
    refreshToken: typeof refreshToken === 'string' && refreshToken.length > 0 ? refreshToken : null,
  };
}

/**
 * Revokes the Apple refresh token, which Apple requires when an account is deleted.
 *
 * Best effort and never throws: the person asked for their data to be gone, and a hiccup at Apple
 * is not a reason to keep it. A failure is logged for us to follow up; the user can also remove the
 * app from Settings → Apple ID → Sign in with Apple themselves.
 */
export async function revoke(env: Env, refreshToken: string, now: Date): Promise<boolean> {
  const config = readConfig(env);
  if (config === null) {
    console.error('apple revoke skipped: Sign in with Apple is not configured');
    return false;
  }

  try {
    const response = await fetch(REVOKE_ENDPOINT, {
      method: 'POST',
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        client_id: config.bundleId,
        client_secret: await clientSecret(config, now),
        token: refreshToken,
        token_type_hint: 'refresh_token',
      }),
    });
    if (!response.ok) {
      console.error('apple revoke failed', response.status, (await response.text()).slice(0, 300));
      return false;
    }
    return true;
  } catch (error) {
    console.error('apple revoke failed', error instanceof Error ? error.message : String(error));
    return false;
  }
}
