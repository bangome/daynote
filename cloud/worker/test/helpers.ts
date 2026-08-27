import { env } from './env';

/**
 * Test helpers.
 *
 * The client-side key derivation is NOT reproduced here: these tests exercise the server contract,
 * so an `auth_key` is just 32 random bytes, which is exactly what the server is entitled to assume.
 * Whether the client derives it correctly from a password is Phase 2's problem.
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

export function randomAuthKey(): string {
  return toBase64Url(crypto.getRandomValues(new Uint8Array(32)));
}

/** A structurally valid `v1.<12-byte nonce>.<48-byte ct+tag>` envelope. Opaque to the server. */
export function fakeWrappedDek(): string {
  const nonce = toBase64Url(crypto.getRandomValues(new Uint8Array(12)));
  const ciphertext = toBase64Url(crypto.getRandomValues(new Uint8Array(48)));
  return `v1.${nonce}.${ciphertext}`;
}

export const KDF_PARAMS = { kdf: 'argon2id', m: 65536, t: 3, p: 4, v: 1 } as const;

export async function resetDatabase(): Promise<void> {
  for (const table of ['change_log', 'notes', 'reset_tokens', 'refresh_tokens', 'users', 'rate_limits']) {
    await env.DB.prepare(`DELETE FROM ${table}`).run();
  }
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

export interface Account {
  email: string;
  authKey: string;
  userId: string;
  wrappedPw: string;
}

let counter = 0;

export async function registerAccount(
  overrides: { recoveryKey?: boolean } = {},
): Promise<Account> {
  counter += 1;
  const email = `user${counter}-${crypto.randomUUID().slice(0, 8)}@example.test`;
  const authKey = randomAuthKey();
  const wrappedPw = fakeWrappedDek();

  const response = await post('/v1/auth/register', {
    email,
    auth_key: authKey,
    wrapped_dek_pw: wrappedPw,
    wrapped_dek_rk: overrides.recoveryKey === false ? undefined : fakeWrappedDek(),
    kdf_params: KDF_PARAMS,
  });

  if (response.status !== 201) {
    throw new Error(`register failed: ${response.status} ${JSON.stringify(response.body)}`);
  }

  return { email, authKey, userId: response.body.user_id, wrappedPw };
}

export async function loginAccount(account: Account, device = 'Test PC') {
  return post('/v1/auth/login', {
    email: account.email,
    auth_key: account.authKey,
    device_name: device,
  });
}
