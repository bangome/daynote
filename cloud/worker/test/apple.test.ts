import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  APPLE_KEY,
  APPLE_TEAM,
  REDIRECT_URI,
  appleTokenResponse,
  configureApple,
  decodeJwtPart,
  env,
  get,
  jsonResponse,
  mockFetch,
  post,
  resetDatabase,
  signIn,
  stubGoogle,
  unconfigureApple,
} from './helpers';

/**
 * Sign in with Apple (src/apple.ts). Apple is never contacted: `fetch` is mocked, so the real code
 * path runs — the ES256 client secret signed with a key generated here, the form posted to Apple's
 * token endpoint, and the claims check on what comes back.
 */

const NONCE = 'raw-nonce-0f3c9a7e2b1d4c5f';
const TOKEN_URL = 'https://appleid.apple.com/auth/token';

let publicKey: CryptoKey;

beforeEach(async () => {
  await resetDatabase();
  publicKey = await configureApple();
});

afterEach(() => {
  vi.restoreAllMocks();
  unconfigureApple();
});

function appleBody(overrides: Record<string, unknown> = {}) {
  return { authorization_code: 'c-apple-1', nonce: NONCE, device_name: 'iPhone', ...overrides };
}

describe('apple sign-in', () => {
  it('creates the account, with the same response shape as Google sign-in', async () => {
    const calls = mockFetch(() => appleTokenResponse(NONCE));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.status).toBe(200);
    expect(response.body.email).toBe('relay123@privaterelay.appleid.com');
    expect(response.body.data_key).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(response.body.protection).toBe('server');
    expect(response.body.entitlement.state).toBe('trial');

    // The app parses both with one model, so the key sets must be identical.
    stubGoogle({ 'g-code': { subject: 'g-sub', email: 'g@example.test' } });
    const google = await post('/v1/auth/google', {
      code: 'g-code', code_verifier: 'a'.repeat(43), redirect_uri: REDIRECT_URI,
    });
    expect(google.status).toBe(200);
    expect(Object.keys(response.body).sort()).toEqual(Object.keys(google.body).sort());

    expect(calls).toHaveLength(1);
    expect(calls[0]!.url).toBe(TOKEN_URL);
    const form = new URLSearchParams(calls[0]!.body);
    expect(form.get('grant_type')).toBe('authorization_code');
    expect(form.get('code')).toBe('c-apple-1');
    expect(form.get('client_id')).toBe('cc.arachat.daynote');
  });

  it('signs the client secret the way Apple requires', async () => {
    const calls = mockFetch(() => appleTokenResponse(NONCE));

    await post('/v1/auth/apple', appleBody());

    const secret = new URLSearchParams(calls[0]!.body).get('client_secret')!;
    const [header, claims, signature] = secret.split('.') as [string, string, string];
    expect(decodeJwtPart(header)).toEqual({ alg: 'ES256', kid: APPLE_KEY });

    const payload = decodeJwtPart(claims);
    expect(payload).toMatchObject({
      iss: APPLE_TEAM,
      aud: 'https://appleid.apple.com',
      sub: 'cc.arachat.daynote',
    });
    expect(payload.exp - payload.iat).toBeGreaterThan(0);
    expect(payload.exp - payload.iat).toBeLessThanOrEqual(180 * 24 * 60 * 60);

    // Raw r||s, 64 bytes, verifiable with the public half of the configured key.
    const raw = Uint8Array.from(
      atob(signature.replaceAll('-', '+').replaceAll('_', '/').padEnd(88, '=')),
      (c) => c.charCodeAt(0),
    );
    expect(raw.length).toBe(64);
    const valid = await crypto.subtle.verify(
      { name: 'ECDSA', hash: 'SHA-256' },
      publicKey,
      raw,
      new TextEncoder().encode(`${header}.${claims}`),
    );
    expect(valid).toBe(true);
  });

  it('stores Apple’s refresh token sealed, and no Google subject', async () => {
    mockFetch(() => appleTokenResponse(NONCE));

    const response = await post('/v1/auth/apple', appleBody());

    const row = await env.DB.prepare(
      'SELECT google_sub, apple_sub, apple_refresh_token, trial_ends_utc FROM users WHERE id = ?1',
    )
      .bind(response.body.user_id)
      .first<Record<string, string | null>>();
    expect(row?.google_sub).toBeNull();
    expect(row?.apple_sub).toBe('001234.abcdef0123456789.0123');
    expect(row?.apple_refresh_token).toMatch(/^t1\./);
    expect(row?.apple_refresh_token).not.toContain('apple-refresh-token-1');
    expect(row?.trial_ends_utc).not.toBeNull();
  });

  it('reuses the account on a later sign-in and keeps the address when Apple omits it', async () => {
    mockFetch(() => appleTokenResponse(NONCE));
    const first = await post('/v1/auth/apple', appleBody());
    vi.restoreAllMocks();

    mockFetch(() => appleTokenResponse(NONCE, { email: undefined }, 'apple-refresh-token-2'));
    const second = await post('/v1/auth/apple', appleBody({ authorization_code: 'c-apple-2' }));

    expect(second.status).toBe(200);
    expect(second.body.user_id).toBe(first.body.user_id);
    expect(second.body.data_key).toBe(first.body.data_key);
    expect(second.body.email).toBe('relay123@privaterelay.appleid.com');

    const count = await env.DB.prepare('SELECT COUNT(*) AS n FROM users').first<{ n: number }>();
    expect(count?.n).toBe(1);
    const me = await get('/v1/auth/me', { token: second.body.access_token });
    expect(me.body.email).toBe('relay123@privaterelay.appleid.com');
  });

  it('stores an empty address for an account that has never shared one', async () => {
    mockFetch(() => appleTokenResponse(NONCE, { email: undefined }));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.status).toBe(200);
    expect(response.body.email).toBe('');
  });

  it('keeps an Apple account and a Google account with the same address apart', async () => {
    const google = await signIn({ email: 'same@example.test' });
    mockFetch(() => appleTokenResponse(NONCE, { email: 'same@example.test' }));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.body.user_id).not.toBe(google.userId);
    expect(response.body.data_key).not.toBe(google.dataKey);
  });

  it('refuses a token whose nonce is not the hash of the one presented', async () => {
    mockFetch(() => appleTokenResponse('some-other-attempt'));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.status).toBe(401);
    expect(response.body.error).toBe('invalid_credentials');
    const count = await env.DB.prepare('SELECT COUNT(*) AS n FROM users').first<{ n: number }>();
    expect(count?.n).toBe(0);
  });

  it('refuses the raw nonce echoed back unhashed', async () => {
    mockFetch(() => appleTokenResponse(NONCE, { nonce: NONCE }));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.status).toBe(401);
    expect(response.body.error).toBe('invalid_credentials');
  });

  it('refuses a token issued for a different app', async () => {
    mockFetch(() => appleTokenResponse(NONCE, { aud: 'com.example.other' }));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.status).toBe(500);
    expect(response.body.error).toBe('server_error');
  });

  it('refuses a token from anyone but Apple', async () => {
    mockFetch(() => appleTokenResponse(NONCE, { iss: 'https://accounts.google.com' }));

    expect((await post('/v1/auth/apple', appleBody())).body.error).toBe('server_error');
  });

  it('refuses an expired token', async () => {
    mockFetch(() => appleTokenResponse(NONCE, { exp: Math.floor(Date.now() / 1000) - 60 }));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.status).toBe(401);
    expect(response.body.error).toBe('invalid_credentials');
  });

  it('reads a used or expired code as the caller’s problem, not a 500', async () => {
    mockFetch(() => jsonResponse({ error: 'invalid_grant' }, 400));

    const response = await post('/v1/auth/apple', appleBody());

    expect(response.status).toBe(401);
    expect(response.body.error).toBe('invalid_credentials');
  });

  it('requires the code and the nonce', async () => {
    const calls = mockFetch(() => appleTokenResponse(NONCE));

    for (const missing of ['authorization_code', 'nonce']) {
      const body: Record<string, unknown> = appleBody();
      delete body[missing];
      const response = await post('/v1/auth/apple', body);
      expect(response.status, missing).toBe(400);
      expect(response.body.error, missing).toBe('bad_request');
    }
    expect(calls).toHaveLength(0);
  });

  it('says so plainly, without calling Apple, when it is not configured', async () => {
    const calls = mockFetch(() => appleTokenResponse(NONCE));

    for (const field of ['APPLE_TEAM_ID', 'APPLE_KEY_ID', 'APPLE_PRIVATE_KEY', 'APPLE_BUNDLE_ID']) {
      await configureApple();
      const target = env as unknown as Record<string, string | undefined>;
      const kept = target[field];
      target[field] = '';
      try {
        const response = await post('/v1/auth/apple', appleBody(), { ip: `192.0.2.${field.length}` });
        expect(response.status, field).toBe(400);
        expect(response.body.error, field).toBe('bad_request');
        expect(response.body.message, field).toMatch(/not configured on this server yet/);
      } finally {
        target[field] = kept;
      }
    }
    expect(calls).toHaveLength(0);
  });

  it('shares the sign-in rate limit, counted before the exchange', async () => {
    mockFetch(() => jsonResponse({ error: 'invalid_grant' }, 400));

    let limited = false;
    for (let attempt = 0; attempt < 25; attempt += 1) {
      const response = await post('/v1/auth/apple', appleBody(), { ip: '198.51.100.44' });
      if (response.status === 429) {
        limited = true;
        expect(response.body.error).toBe('rate_limited');
        break;
      }
    }

    expect(limited).toBe(true);
  });
});
