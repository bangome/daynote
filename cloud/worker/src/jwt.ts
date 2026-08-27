import { fromBase64Url, timingSafeEqual, toBase64Url, uuid } from './bytes';

/**
 * Minimal HS256 JWT, hand-rolled to avoid a dependency for two functions.
 *
 * Access tokens are stateless for their 15-minute lifetime: revoking a refresh token does not
 * invalidate an already-issued access token. That window is the deliberate trade for not hitting D1
 * on every request; anything that must revoke immediately has to go through the refresh path.
 */

export interface AccessClaims {
  sub: string;
  jti: string;
  iat: number;
  exp: number;
}

const HEADER = toBase64Url(new TextEncoder().encode(JSON.stringify({ alg: 'HS256', typ: 'JWT' })));

async function hmacKey(secret: string): Promise<CryptoKey> {
  return crypto.subtle.importKey(
    'raw',
    new TextEncoder().encode(secret) as BufferSource,
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign'],
  );
}

async function sign(secret: string, payload: string): Promise<string> {
  const signature = await crypto.subtle.sign(
    'HMAC',
    await hmacKey(secret),
    new TextEncoder().encode(payload) as BufferSource,
  );
  return toBase64Url(new Uint8Array(signature));
}

export async function issueAccessToken(
  secret: string,
  userId: string,
  issuedAt: Date,
  ttlSeconds: number,
): Promise<{ token: string; expiresAtEpoch: number }> {
  const iat = Math.floor(issuedAt.getTime() / 1000);
  const exp = iat + ttlSeconds;
  const claims: AccessClaims = { sub: userId, jti: uuid(), iat, exp };
  const body = toBase64Url(new TextEncoder().encode(JSON.stringify(claims)));
  const payload = `${HEADER}.${body}`;
  return { token: `${payload}.${await sign(secret, payload)}`, expiresAtEpoch: exp };
}

/** Returns null for anything not currently valid; callers turn that into a 401. */
export async function verifyAccessToken(
  secret: string,
  token: string,
  now: Date,
): Promise<AccessClaims | null> {
  const segments = token.split('.');
  if (segments.length !== 3) {
    return null;
  }

  const [header, body, signature] = segments as [string, string, string];
  // Pin the algorithm to the header this Worker issues: never read `alg` from the token itself,
  // which is how "alg: none" and HS/RS confusion attacks get in.
  if (header !== HEADER) {
    return null;
  }

  const presented = fromBase64Url(signature);
  const expected = fromBase64Url(await sign(secret, `${header}.${body}`));
  if (presented === null || expected === null || !timingSafeEqual(presented, expected)) {
    return null;
  }

  const decoded = fromBase64Url(body);
  if (decoded === null) {
    return null;
  }

  let claims: AccessClaims;
  try {
    claims = JSON.parse(new TextDecoder().decode(decoded)) as AccessClaims;
  } catch {
    return null;
  }

  if (typeof claims.sub !== 'string' || claims.sub.length === 0) {
    return null;
  }
  if (typeof claims.exp !== 'number' || typeof claims.iat !== 'number') {
    return null;
  }

  const nowEpoch = Math.floor(now.getTime() / 1000);
  if (claims.exp <= nowEpoch) {
    return null;
  }
  // Reject a token minted in the future beyond a small skew allowance.
  if (claims.iat > nowEpoch + 60) {
    return null;
  }

  return claims;
}
