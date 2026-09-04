import { ApiError } from './http';
import type { Env } from './env';

/**
 * Google sign-in: the authorization-code half of the desktop OAuth flow.
 *
 * The app opens the system browser, receives the code on a loopback redirect, and posts it here with
 * its PKCE verifier. The code-for-token exchange happens in the Worker, not in the app, because it
 * needs the OAuth client secret: Google documents the secret of an "installed app" client as not
 * confidential, but a value shipped inside a WPF binary can be lifted out of it with a hex editor,
 * and there is no reason to publish one when the Worker can hold it.
 *
 * PKCE is still used even though the exchange is server-side. It binds the code to the browser
 * session that started the flow, which is what stops a code intercepted on the loopback redirect
 * from being redeemed by anything else on the machine.
 */

const TOKEN_ENDPOINT = 'https://oauth2.googleapis.com/token';
const ISSUERS = new Set(['accounts.google.com', 'https://accounts.google.com']);

/** What the Worker needs about the person who just signed in. */
export interface GoogleIdentity {
  /** The `sub` claim: Google's stable, never-reused account id. */
  readonly subject: string;
  readonly email: string;
}

interface IdTokenClaims {
  iss?: string;
  aud?: string;
  sub?: string;
  exp?: number;
  email?: string;
  email_verified?: boolean | string;
}

function decodeSegment(segment: string): unknown {
  const padded = segment.replaceAll('-', '+').replaceAll('_', '/');
  const binary = atob(padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), '='));
  const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes));
}

/**
 * Reads the claims out of an ID token that came straight from Google's token endpoint.
 *
 * The signature is deliberately not checked. This token did not arrive from the client: it was
 * fetched by this Worker over TLS from `oauth2.googleapis.com`, and Google's own documentation says
 * a token obtained that way can be trusted without verification. Fetching JWKS to re-verify what we
 * just received on an authenticated channel would add a cache, a network dependency on the login
 * path, and a second way to fail, for no property we do not already have. The claims below are
 * still checked, because they say *which* account and *which* client, and a mismatch there is a
 * configuration error worth failing loudly on.
 */
function readClaims(idToken: string, clientId: string): GoogleIdentity {
  const parts = idToken.split('.');
  if (parts.length !== 3) {
    throw new ApiError('server_error', 'Google returned a malformed ID token.');
  }

  let claims: IdTokenClaims;
  try {
    claims = decodeSegment(parts[1]!) as IdTokenClaims;
  } catch {
    throw new ApiError('server_error', 'Google returned an unreadable ID token.');
  }

  if (typeof claims.iss !== 'string' || !ISSUERS.has(claims.iss)) {
    throw new ApiError('server_error', 'The ID token was not issued by Google.');
  }
  if (claims.aud !== clientId) {
    // Almost always a deployment mistake: GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET are from
    // different OAuth clients.
    throw new ApiError('server_error', 'The ID token was issued for a different OAuth client.');
  }
  if (typeof claims.exp !== 'number' || claims.exp * 1000 <= Date.now()) {
    throw new ApiError('invalid_credentials', 'The Google sign-in expired. Try again.');
  }
  if (typeof claims.sub !== 'string' || claims.sub.length === 0) {
    throw new ApiError('server_error', 'The ID token carried no subject.');
  }

  const verified = claims.email_verified === true || claims.email_verified === 'true';
  if (typeof claims.email !== 'string' || claims.email.length === 0 || !verified) {
    // An unverified address on a Google account is rare and would let someone claim an address they
    // do not own. The address is only used for display, but showing an unowned one is still wrong.
    throw new ApiError('invalid_credentials', 'This Google account has no verified email address.');
  }

  return { subject: claims.sub, email: claims.email.trim().toLowerCase() };
}

/**
 * Exchanges an authorization code for Google's ID token and returns who signed in.
 *
 * `env.GOOGLE_EXCHANGE` replaces the network call in tests. It is a seam, not a fallback: in
 * production the absence of the client credentials is a hard error rather than a degraded mode.
 */
export async function identify(
  env: Env,
  code: string,
  codeVerifier: string,
  redirectUri: string,
): Promise<GoogleIdentity> {
  if (env.GOOGLE_EXCHANGE !== undefined) {
    return env.GOOGLE_EXCHANGE(code, codeVerifier, redirectUri);
  }

  const clientId = env.GOOGLE_CLIENT_ID;
  const clientSecret = env.GOOGLE_CLIENT_SECRET;
  if (typeof clientId !== 'string' || clientId.length === 0) {
    throw new Error('GOOGLE_CLIENT_ID is not configured; see cloud/worker/DEPLOY.md.');
  }
  if (typeof clientSecret !== 'string' || clientSecret.length === 0) {
    throw new Error('GOOGLE_CLIENT_SECRET is not set. Run: wrangler secret put GOOGLE_CLIENT_SECRET');
  }

  const response = await fetch(TOKEN_ENDPOINT, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      code,
      client_id: clientId,
      client_secret: clientSecret,
      code_verifier: codeVerifier,
      redirect_uri: redirectUri,
      grant_type: 'authorization_code',
    }),
  });

  const body = (await response.json().catch(() => ({}))) as Record<string, unknown>;
  if (!response.ok) {
    // `invalid_grant` is the ordinary case: a code that was already used, expired, or belongs to a
    // different verifier. It is the caller's problem, not a server fault, so it must not be a 500.
    if (body['error'] === 'invalid_grant') {
      throw new ApiError('invalid_credentials', 'That Google sign-in is no longer valid. Try again.');
    }
    console.error('google token exchange failed', response.status, JSON.stringify(body).slice(0, 300));
    throw new ApiError('server_error', 'Google would not complete the sign-in.');
  }

  const idToken = body['id_token'];
  if (typeof idToken !== 'string') {
    throw new ApiError('server_error', 'Google returned no ID token.');
  }

  return readClaims(idToken, clientId);
}
