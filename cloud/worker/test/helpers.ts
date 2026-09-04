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
    'change_log', 'notes', 'billing_events', 'subscriptions', 'refresh_tokens', 'users',
    'rate_limits',
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

export function get(path: string, options: { ip?: string; token?: string } = {}) {
  const headers: Record<string, string> = {};
  if (options.token !== undefined) {
    headers['authorization'] = `Bearer ${options.token}`;
  }
  return call(path, { method: 'GET', headers, ip: options.ip });
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
export async function grantSubscription(userId: string, days = 30): Promise<void> {
  const ends = new Date(Date.now() + days * 24 * 60 * 60 * 1000).toISOString();
  await env.DB.prepare(
    `INSERT INTO subscriptions
       (user_id, provider, customer_id, subscription_id, status, current_period_end_utc, updated_utc)
     VALUES (?1, 'paddle', 'ctm_test', 'sub_test', 'active', ?2, ?2)
     ON CONFLICT(user_id) DO UPDATE SET status = 'active', current_period_end_utc = excluded.current_period_end_utc`,
  )
    .bind(userId, ends)
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
