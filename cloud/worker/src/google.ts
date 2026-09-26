import { ApiError } from './http';
import type { Env } from './env';

/**
 * Google sign-in: the authorization-code half of the OAuth flow, for every app Daynote ships.
 *
 * The app opens the browser, receives the code on a redirect it owns, and posts it here with its
 * PKCE verifier. The exchange happens in the Worker rather than in the app because the desktop
 * client has a secret: Google documents the secret of an "installed app" client as not
 * confidential, but a value shipped inside a WPF binary can be lifted out of it with a hex editor,
 * and there is no reason to publish one when the Worker can hold it.
 *
 * The phone clients have no secret at all — Google does not issue one for the iOS or Android client
 * types — so for those the exchange carries the client id alone. That is not a weaker flow: PKCE is
 * what binds the code to the attempt either way, and it is the reason the desktop redirect (a
 * loopback port any local process could have listened on) and the phone redirect (a custom scheme
 * another app could have registered) are both safe to use.
 *
 * Which client a code belongs to is decided by the caller, and a wrong answer cannot be turned into
 * an attack: Google issued the code to one client and refuses to exchange it against another.
 */

const TOKEN_ENDPOINT = 'https://oauth2.googleapis.com/token';
const ISSUERS = new Set(['accounts.google.com', 'https://accounts.google.com']);

/** What the Worker needs about the person who just signed in. */
/** Which of Daynote's OAuth clients a sign-in belongs to. */
export type OAuthClient = 'desktop' | 'ios' | 'android';

/** The client's id, and its secret where Google issues one. */
interface ClientCredentials {
  readonly id: string;
  readonly secret?: string;
}

/**
 * Picks the credentials for one client, failing loudly when that platform is not configured.
 *
 * A missing phone client id is a deployment state rather than a bug — the app ships with sign-in
 * switched off until the clients exist — so it reads as a refusal the caller can show, not a 500.
 */
function credentialsFor(env: Env, client: OAuthClient): ClientCredentials {
  if (client === 'desktop') {
    const id = env.GOOGLE_CLIENT_ID;
    const secret = env.GOOGLE_CLIENT_SECRET;
    if (typeof id !== 'string' || id.length === 0) {
      throw new Error('GOOGLE_CLIENT_ID is not configured; see cloud/worker/DEPLOY.md.');
    }
    if (typeof secret !== 'string' || secret.length === 0) {
      throw new Error('GOOGLE_CLIENT_SECRET is not set. Run: wrangler secret put GOOGLE_CLIENT_SECRET');
    }
    return { id, secret };
  }

  const id = client === 'ios' ? env.GOOGLE_IOS_CLIENT_ID : env.GOOGLE_ANDROID_CLIENT_ID;
  if (typeof id !== 'string' || id.length === 0) {
    throw new ApiError(
      'bad_request',
      `Signing in from ${client} is not configured on this server yet.`,
    );
  }

  // No secret: Google issues none for these client types.
  return { id };
}

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
  client: OAuthClient = 'desktop',
): Promise<GoogleIdentity> {
  if (env.GOOGLE_EXCHANGE !== undefined) {
    return env.GOOGLE_EXCHANGE(code, codeVerifier, redirectUri, client);
  }

  const { id: clientId, secret } = credentialsFor(env, client);

  const form = new URLSearchParams({
    code,
    client_id: clientId,
    code_verifier: codeVerifier,
    redirect_uri: redirectUri,
    grant_type: 'authorization_code',
  });
  if (secret !== undefined) {
    form.set('client_secret', secret);
  }

  const response = await fetch(TOKEN_ENDPOINT, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: form,
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
