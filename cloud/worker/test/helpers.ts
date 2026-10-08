import { vi } from 'vitest';
import { env } from './env';

/**
 * Test helpers.
 *
 * Google is never contacted: `env.GOOGLE_EXCHANGE` is a seam on `Env`, and `signIn` installs a stub
 * that turns a code into an identity. What is exercised here is this Worker's contract — account
 * creation, sessions, and the data key — not Google's.
 */

const BASE = 'https://daynote.test';

export { env };

export function toBase64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}

export const REDIRECT_URI = 'http://127.0.0.1:53219/';

export async function resetDatabase(): Promise<void> {
  for (const table of [
    'change_log', 'files', 'assets', 'notes', 'agenda', 'billing_events', 'subscriptions',
    'refresh_tokens', 'users', 'rate_limits',
  ]) {
    await env.DB.prepare(`DELETE FROM ${table}`).run();
  }
  delete (env as { GOOGLE_EXCHANGE?: unknown }).GOOGLE_EXCHANGE;
}

export interface ApiResponse<T = Record<string, any>> {
  status: number;
  body: T;
}

async function call(
  path: string,
  init: RequestInit & { ip?: string } = {},
): Promise<ApiResponse> {
  const headers = new Headers(init.headers);
  headers.set('cf-connecting-ip', init.ip ?? '203.0.113.1');
  if (init.body !== undefined) {
    headers.set('content-type', 'application/json');
  }

  // Imported here so the module graph is loaded inside the worker isolate.
  const worker = (await import('../src/index')).default;
  const request = new Request(`${BASE}${path}`, { ...init, headers });
  const ctx = { waitUntil: () => {}, passThroughOnException: () => {} } as unknown as ExecutionContext;
  const response = await worker.fetch(request, env as any, ctx);

  const text = await response.text();
  return { status: response.status, body: text.length === 0 ? {} : JSON.parse(text) };
}

export function post(path: string, body: unknown, options: { ip?: string; token?: string } = {}) {
  const headers: Record<string, string> = {};
  if (options.token !== undefined) {
    headers['authorization'] = `Bearer ${options.token}`;
  }
  return call(path, { method: 'POST', body: JSON.stringify(body), headers, ip: options.ip });
}

export function del(path: string, options: { ip?: string; token?: string } = {}) {
  const headers: Record<string, string> = {};
  if (options.token !== undefined) {
    headers['authorization'] = `Bearer ${options.token}`;
  }
  return call(path, { method: 'DELETE', headers, ip: options.ip });
}

export function get(path: string, options: { ip?: string; token?: string } = {}) {
  const headers: Record<string, string> = {};
  if (options.token !== undefined) {
    headers['authorization'] = `Bearer ${options.token}`;
  }
  return call(path, { method: 'GET', headers, ip: options.ip });
}

/**
 * The two binary calls, which cannot go through `call`: it parses every response as JSON, and an
 * attachment body is neither JSON nor text. `content-length` is set explicitly because the Worker
 * checks it, and `Request` does not add one for an ArrayBuffer body in this runtime.
 */
async function callRaw(
  path: string,
  init: RequestInit & { token?: string },
): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set('cf-connecting-ip', '203.0.113.1');
  if (init.token !== undefined) {
    headers.set('authorization', `Bearer ${init.token}`);
  }

  const worker = (await import('../src/index')).default;
  const ctx = { waitUntil: () => {}, passThroughOnException: () => {} } as unknown as ExecutionContext;
  return worker.fetch(new Request(`${BASE}${path}`, { ...init, headers }), env as any, ctx);
}

export async function putAsset(
  key: string,
  bytes: Uint8Array,
  token: string,
): Promise<ApiResponse> {
  const response = await callRaw(`/v1/assets/${key}`, {
    method: 'PUT',
    body: bytes,
    token,
    headers: { 'content-length': String(bytes.byteLength) },
  });
  const text = await response.text();
  return { status: response.status, body: text.length === 0 ? {} : JSON.parse(text) };
}

export async function getAsset(
  key: string,
  token: string,
): Promise<{ status: number; bytes: Uint8Array }> {
  const response = await callRaw(`/v1/assets/${key}`, { method: 'GET', token });
  const buffer = await response.arrayBuffer();
  return { status: response.status, bytes: new Uint8Array(buffer) };
}

/**
 * Installs a stubbed Google exchange. `codes` maps an authorization code to the identity Google
 * would have returned for it; an unknown code fails the way a replayed one does.
 */
export function stubGoogle(codes: Record<string, { subject: string; email: string }>): void {
  (env as { GOOGLE_EXCHANGE?: unknown }).GOOGLE_EXCHANGE = async (code: string) => {
    const identity = codes[code];
    if (identity === undefined) {
      const { ApiError } = await import('../src/http');
      throw new ApiError('invalid_credentials', 'That Google sign-in is no longer valid. Try again.');
    }
    return identity;
  };
}

/** The client each stubbed exchange was asked for, so a test can assert the routing. */
export function recordGoogleClient(): { readonly seen: string[] } {
  const seen: string[] = [];
  (env as { GOOGLE_EXCHANGE?: unknown }).GOOGLE_EXCHANGE = async (
    _code: string,
    _verifier: string,
    _redirect: string,
    client: string,
  ) => {
    seen.push(client);
    return { subject: `sub-${client}`, email: `${client}@example.test` };
  };
  return { seen };
}

/** Removes the stub so a test can reach the real credential lookup. */
export function unstubGoogle(): void {
  delete (env as { GOOGLE_EXCHANGE?: unknown }).GOOGLE_EXCHANGE;
}

/** The redirect a phone app receives its code on: its own private scheme (RFC 8252 §7.1). */
export const MOBILE_REDIRECT_URI = 'cc.arachat.daynote:/oauth2redirect';

/** A structurally valid `v1.<12-byte nonce>.<48-byte ct+tag>` envelope. Opaque to the server. */
export function fakeWrappedDek(): string {
  const nonce = toBase64Url(crypto.getRandomValues(new Uint8Array(12)));
  const ciphertext = toBase64Url(crypto.getRandomValues(new Uint8Array(48)));
  return `v1.${nonce}.${ciphertext}`;
}

export const KDF_PARAMS = { kdf: 'argon2id', m: 65536, t: 3, p: 4, v: 1 } as const;

/**
 * Grants an account an active subscription directly, for tests that are about something else.
 * The webhook path is exercised on its own in `billing.test.ts`.
 */
export async function grantSubscription(
  userId: string,
  days = 30,
  tier: 'pro' | 'premium' = 'pro',
): Promise<void> {
  const ends = new Date(Date.now() + days * 24 * 60 * 60 * 1000).toISOString();
  await env.DB.prepare(
    `INSERT INTO subscriptions
       (user_id, provider, customer_id, subscription_id, status, current_period_end_utc, updated_utc, tier)
     VALUES (?1, 'paddle', 'ctm_test', 'sub_test', 'active', ?2, ?2, ?3)
     ON CONFLICT(user_id) DO UPDATE SET status = 'active',
       current_period_end_utc = excluded.current_period_end_utc, tier = excluded.tier`,
  )
    .bind(userId, ends, tier)
    .run();
}

/** Expires both the trial and any subscription, so the account is unentitled. */
export async function expireEntitlement(userId: string): Promise<void> {
  const past = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
  await env.DB.prepare('UPDATE users SET trial_ends_utc = ?2 WHERE id = ?1').bind(userId, past).run();
  await env.DB.prepare('DELETE FROM subscriptions WHERE user_id = ?1').bind(userId).run();
}

export interface Account {
  subject: string;
  email: string;
  userId: string;
  dataKey: string;
  accessToken: string;
  refreshToken: string;
}

let counter = 0;

/** Signs in as a fresh Google account, creating it on the way through. */
export async function signIn(
  overrides: { subject?: string; email?: string; device?: string } = {},
): Promise<Account> {
  counter += 1;
  const subject = overrides.subject ?? `google-sub-${counter}-${crypto.randomUUID().slice(0, 8)}`;
  const email = overrides.email ?? `user${counter}@example.test`;
  const code = `code-${crypto.randomUUID()}`;

  stubGoogle({ [code]: { subject, email } });
  const response = await post('/v1/auth/google', {
    code,
    code_verifier: 'a'.repeat(43),
    redirect_uri: REDIRECT_URI,
    device_name: overrides.device ?? 'Test PC',
  });

  if (response.status !== 200) {
    throw new Error(`sign-in failed: ${response.status} ${JSON.stringify(response.body)}`);
  }

  return {
    subject,
    email,
    userId: response.body.user_id,
    dataKey: response.body.data_key,
    accessToken: response.body.access_token,
    refreshToken: response.body.refresh_token,
  };
}

/** Signs in again as an account that already exists, as a second device would. */
export function signInAgain(account: Account, device = 'Second PC') {
  const code = `code-${crypto.randomUUID()}`;
  stubGoogle({ [code]: { subject: account.subject, email: account.email } });
  return post('/v1/auth/google', {
    code,
    code_verifier: 'b'.repeat(43),
    redirect_uri: REDIRECT_URI,
    device_name: device,
  });
}

/** One outbound request the Worker made, as the mocked `fetch` saw it. */
export interface OutboundCall {
  url: string;
  method: string;
  headers: Headers;
  body: string;
}

/**
 * Replaces the global `fetch` for the rest of the test (undo with `vi.restoreAllMocks()`), so the
 * real Paddle and Apple code paths run — URL, form, headers, client secret — against a stand-in.
 * The Worker is imported into this same isolate, so its `fetch` is this one.
 */
export function mockFetch(
  respond: (call: OutboundCall) => Response | Promise<Response>,
): OutboundCall[] {
  const calls: OutboundCall[] = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const request = new Request(input as RequestInfo, init as RequestInit | undefined);
    const call = {
      url: request.url,
      method: request.method,
      headers: request.headers,
      // Decoded by hand: workerd warns on .text() for a form body, and a form is text anyway.
      body: new TextDecoder().decode(await request.arrayBuffer()),
    };
    calls.push(call);
    return respond(call);
  });
  return calls;
}

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

export const APPLE_TEAM = 'TEAM123456';
export const APPLE_KEY = 'KEY1234567';

/**
 * Configures Sign in with Apple with a freshly generated P-256 key, exported as the PKCS#8 PEM the
 * developer console would have handed out. Returns the public half, so a test can verify the
 * client secret the Worker signed with it.
 */
export async function configureApple(): Promise<CryptoKey> {
  const pair = (await crypto.subtle.generateKey(
    { name: 'ECDSA', namedCurve: 'P-256' },
    true,
    ['sign', 'verify'],
  )) as CryptoKeyPair;
  const pkcs8 = new Uint8Array((await crypto.subtle.exportKey('pkcs8', pair.privateKey)) as ArrayBuffer);
  const base64 = btoa(String.fromCharCode(...pkcs8));
  const pem = `-----BEGIN PRIVATE KEY-----\n${base64.match(/.{1,64}/g)!.join('\n')}\n-----END PRIVATE KEY-----\n`;

  const target = env as { APPLE_TEAM_ID?: string; APPLE_KEY_ID?: string; APPLE_PRIVATE_KEY?: string };
  target.APPLE_TEAM_ID = APPLE_TEAM;
  target.APPLE_KEY_ID = APPLE_KEY;
  target.APPLE_PRIVATE_KEY = pem;
  return pair.publicKey;
}

/** Back to the wrangler.toml state: bundle id set, team and key ids empty, no private key. */
export function unconfigureApple(): void {
  const target = env as { APPLE_TEAM_ID?: string; APPLE_KEY_ID?: string; APPLE_PRIVATE_KEY?: string };
  target.APPLE_TEAM_ID = '';
  target.APPLE_KEY_ID = '';
  delete target.APPLE_PRIVATE_KEY;
}

export async function sha256HexOf(value: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

/**
 * An id_token as Apple's token endpoint returns it. The signature segment is junk on purpose: the
 * Worker trusts this token because it fetched it from Apple itself, and must not depend on one.
 */
export function appleIdToken(claims: Record<string, unknown>): string {
  const encode = (value: unknown) => toBase64Url(new TextEncoder().encode(JSON.stringify(value)));
  return `${encode({ alg: 'RS256', kid: 'apple-key' })}.${encode(claims)}.not-a-signature`;
}

export function decodeJwtPart(segment: string): Record<string, any> {
  const padded = segment.replaceAll('-', '+').replaceAll('_', '/');
  const binary = atob(padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), '='));
  return JSON.parse(new TextDecoder().decode(Uint8Array.from(binary, (c) => c.charCodeAt(0))));
}

/** Apple's token endpoint, answering every code with one identity. */
export async function appleTokenResponse(
  rawNonce: string,
  overrides: Record<string, unknown> = {},
  refreshToken: string | null = 'apple-refresh-token-1',
): Promise<Response> {
  const now = Math.floor(Date.now() / 1000);
  const claims = {
    iss: 'https://appleid.apple.com',
    aud: 'cc.arachat.daynote',
    exp: now + 600,
    iat: now,
    sub: '001234.abcdef0123456789.0123',
    nonce: await sha256HexOf(rawNonce),
    email: 'Relay123@privaterelay.appleid.com',
    email_verified: 'true',
    ...overrides,
  };
  return jsonResponse({
    access_token: 'apple-access',
    token_type: 'Bearer',
    expires_in: 3600,
    ...(refreshToken === null ? {} : { refresh_token: refreshToken }),
    id_token: appleIdToken(claims),
  });
}
