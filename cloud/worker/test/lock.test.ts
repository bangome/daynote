import { beforeEach, describe, expect, it } from 'vitest';
import {
  KDF_PARAMS,
  env,
  fakeWrappedDek,
  get,
  post,
  resetDatabase,
  signIn,
  signInAgain,
  toBase64Url,
} from './helpers';

/**
 * The opt-in lock (docs/CLOUD_SYNC.md §4.1b). The property under test is narrow and absolute: once
 * an account is locked, this Worker holds nothing that opens it.
 */

beforeEach(resetDatabase);

function lockBody() {
  return {
    wrapped_dek_pw: fakeWrappedDek(),
    wrapped_dek_rk: fakeWrappedDek(),
    kdf_params: KDF_PARAMS,
  };
}

function rawKey(): string {
  return toBase64Url(crypto.getRandomValues(new Uint8Array(32)));
}

async function storedRow(userId: string) {
  return env.DB.prepare(
    'SELECT protection, wrapped_dek, wrapped_dek_pw, wrapped_dek_rk, kdf_params FROM users WHERE id = ?1',
  )
    .bind(userId)
    .first<{
      protection: string;
      wrapped_dek: string | null;
      wrapped_dek_pw: string | null;
      wrapped_dek_rk: string | null;
      kdf_params: string | null;
    }>();
}

describe('protect', () => {
  it('stores the envelopes and destroys the server copy in the same write', async () => {
    const account = await signIn();
    const body = lockBody();

    const response = await post('/v1/auth/protect', body, { token: account.accessToken });

    expect(response.status).toBe(200);
    expect(response.body.protection).toBe('passphrase');

    const row = await storedRow(account.userId);
    expect(row?.protection).toBe('passphrase');
    // The whole feature: no spare key on the hook.
    expect(row?.wrapped_dek).toBeNull();
    expect(row?.wrapped_dek_pw).toBe(body.wrapped_dek_pw);
    expect(row?.wrapped_dek_rk).toBe(body.wrapped_dek_rk);
  });

  it('hands a locked account envelopes instead of a key, on sign-in and on demand', async () => {
    const account = await signIn();
    await post('/v1/auth/protect', lockBody(), { token: account.accessToken });

    const again = await signInAgain(account);
    expect(again.status).toBe(200);
    expect(again.body.protection).toBe('passphrase');
    expect(again.body.data_key).toBeUndefined();
    expect(again.body.wrapped_dek_pw).toMatch(/^v1\./);
    expect(again.body.kdf_params.kdf).toBe('argon2id');

    const key = await get('/v1/auth/data-key', { token: account.accessToken });
    expect(key.status).toBe(200);
    expect(key.body.data_key).toBeUndefined();
    expect(key.body.wrapped_dek_rk).toMatch(/^v1\./);
  });

  it('refuses to lock an account that is already locked', async () => {
    const account = await signIn();
    await post('/v1/auth/protect', lockBody(), { token: account.accessToken });

    const again = await post('/v1/auth/protect', lockBody(), { token: account.accessToken });

    expect(again.status).toBe(400);
  });

  it('rejects envelopes that are not the right shape, before anything is destroyed', async () => {
    const account = await signIn();

    for (const body of [
      { ...lockBody(), wrapped_dek_pw: 'not-an-envelope' },
      { ...lockBody(), wrapped_dek_rk: 'v1.short.short' },
      { wrapped_dek_pw: fakeWrappedDek(), wrapped_dek_rk: fakeWrappedDek() },
      { ...lockBody(), kdf_params: { kdf: 'rot13' } },
    ]) {
      const response = await post('/v1/auth/protect', body, { token: account.accessToken });
      expect(response.status, JSON.stringify(body).slice(0, 60)).toBe(400);
    }

    // Still openable: a rejected request must not have taken the key away.
    const row = await storedRow(account.userId);
    expect(row?.protection).toBe('server');
    expect(row?.wrapped_dek).not.toBeNull();
  });

  it('needs an access token', async () => {
    expect((await post('/v1/auth/protect', lockBody())).status).toBe(401);
  });
});

describe('unprotect', () => {
  it('takes the key back and clears the envelopes', async () => {
    const account = await signIn();
    await post('/v1/auth/protect', lockBody(), { token: account.accessToken });

    const key = rawKey();
    const response = await post('/v1/auth/unprotect', { data_key: key }, { token: account.accessToken });

    expect(response.status).toBe(200);
    expect(response.body.protection).toBe('server');

    const row = await storedRow(account.userId);
    expect(row?.protection).toBe('server');
    expect(row?.wrapped_dek_pw).toBeNull();
    expect(row?.wrapped_dek_rk).toBeNull();
    expect(row?.kdf_params).toBeNull();
    // Sealed, not stored raw.
    expect(row?.wrapped_dek).toMatch(/^s1\./);
    expect(row?.wrapped_dek).not.toContain(key);

    // And the key that comes back is the one that was handed over.
    const reissued = await get('/v1/auth/data-key', { token: account.accessToken });
    expect(reissued.body.data_key).toBe(key);
  });

  it('refuses a key that is not 32 bytes', async () => {
    const account = await signIn();
    await post('/v1/auth/protect', lockBody(), { token: account.accessToken });

    for (const data_key of ['', 'short', toBase64Url(new Uint8Array(16))]) {
      const response = await post('/v1/auth/unprotect', { data_key }, { token: account.accessToken });
      expect(response.status, data_key).toBe(400);
    }

    expect((await storedRow(account.userId))?.protection).toBe('passphrase');
  });

  it('refuses to unlock an account that was never locked', async () => {
    const account = await signIn();

    const response = await post(
      '/v1/auth/unprotect',
      { data_key: rawKey() },
      { token: account.accessToken },
    );

    expect(response.status).toBe(400);
  });
});

describe('default accounts', () => {
  it('are unaffected: the server still holds and returns the key', async () => {
    const account = await signIn();

    const again = await signInAgain(account);

    expect(again.body.protection).toBe('server');
    expect(again.body.data_key).toBe(account.dataKey);
  });
});
