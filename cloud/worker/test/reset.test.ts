import { beforeEach, describe, expect, it } from 'vitest';
import {
  KDF_PARAMS,
  env,
  fakeWrappedDek,
  get,
  loginAccount,
  post,
  randomAuthKey,
  registerAccount,
  resetDatabase,
} from './helpers';
import type { OutgoingEmail } from '../src/email';

/**
 * Password reset, and the re-wrap that has to follow it.
 *
 * The point these tests protect: resetting a password must NOT touch the wrapped data key. The server
 * cannot re-wrap a key it cannot read, so overwriting that column with anything would destroy the
 * only copy the recovery key can still open.
 */

const sent: OutgoingEmail[] = [];

beforeEach(async () => {
  await resetDatabase();
  sent.length = 0;
  // Injected sender, so the flow runs without a provider and the code is readable by the test.
  (env as unknown as Record<string, unknown>)['EMAIL_SENDER'] = {
    send: (message: OutgoingEmail) => {
      sent.push(message);
      return Promise.resolve();
    },
  };
});

function codeFrom(message: OutgoingEmail): string {
  const match = /([0-9A-Z]{4}-[0-9A-Z]{4})/.exec(message.text);
  if (match === null) {
    throw new Error(`No reset code in: ${message.text}`);
  }
  return match[1]!;
}

async function requestReset(email: string, ip = '203.0.113.9') {
  return post('/v1/auth/reset/request', { email }, { ip });
}

describe('requesting a reset', () => {
  it('emails a code', async () => {
    const account = await registerAccount();

    const response = await requestReset(account.email);

    expect(response.status).toBe(204);
    expect(sent).toHaveLength(1);
    expect(sent[0]!.to).toBe(account.email);
    expect(codeFrom(sent[0]!)).toMatch(/^[0-9A-Z]{4}-[0-9A-Z]{4}$/);
  });

  it('says what will happen to the notes', async () => {
    // Finding out after the reset that the notes are locked would be the worst possible moment.
    const account = await registerAccount();
    await requestReset(account.email);

    expect(sent[0]!.text).toContain('recovery key');
    expect(sent[0]!.text).toContain('cannot read or recover');
  });

  it('answers the same for an unknown address, and sends nothing', async () => {
    const known = await registerAccount();
    const forKnown = await requestReset(known.email);
    sent.length = 0;

    const forUnknown = await requestReset('nobody-here@example.test');

    // Any difference here turns a reset endpoint into an account-enumeration probe.
    expect(forUnknown.status).toBe(forKnown.status);
    expect(forUnknown.body).toEqual(forKnown.body);
    expect(sent).toHaveLength(0);
  });

  it('rate-limits per address, so we cannot be used to flood someone else\'s inbox', async () => {
    const account = await registerAccount();

    let last = { status: 204 } as { status: number };
    for (let attempt = 0; attempt < 5; attempt += 1) {
      last = await requestReset(account.email);
    }

    expect(last.status).toBe(429);
  });

  it('invalidates the previous code when a new one is requested', async () => {
    const account = await registerAccount();
    await requestReset(account.email);
    const firstCode = codeFrom(sent[0]!);
    await requestReset(account.email);

    const stale = await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: firstCode,
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });

    expect(stale.status).toBe(401);
  });
});

describe('confirming a reset', () => {
  it('changes the password and reports that the notes are still locked', async () => {
    const account = await registerAccount();
    await requestReset(account.email);
    const newAuthKey = randomAuthKey();

    const confirmed = await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: codeFrom(sent[0]!),
      new_auth_key: newAuthKey,
      kdf_params: KDF_PARAMS,
    });

    expect(confirmed.status).toBe(200);
    expect(confirmed.body.rewrap_pending).toBe(true);

    const oldPassword = await loginAccount(account);
    expect(oldPassword.status).toBe(401);

    const newPassword = await post('/v1/auth/login', {
      email: account.email,
      auth_key: newAuthKey,
    });
    expect(newPassword.status).toBe(200);
    expect(newPassword.body.rewrap_pending).toBe(true);
  });

  it('leaves the wrapped data key untouched', async () => {
    // The whole reason the reset cannot restore data access. Overwriting this column would destroy
    // the one copy the recovery key can still open.
    const account = await registerAccount();
    const before = await env.DB.prepare('SELECT wrapped_dek_pw, wrapped_dek_rk FROM users')
      .first<{ wrapped_dek_pw: string; wrapped_dek_rk: string }>();

    await requestReset(account.email);
    await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: codeFrom(sent[0]!),
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });

    const after = await env.DB.prepare('SELECT wrapped_dek_pw, wrapped_dek_rk FROM users')
      .first<{ wrapped_dek_pw: string; wrapped_dek_rk: string }>();
    expect(after).toEqual(before);
  });

  it('signs every device out', async () => {
    const account = await registerAccount();
    const laptop = await loginAccount(account, 'Laptop');
    await requestReset(account.email);

    await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: codeFrom(sent[0]!),
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });

    const stillValid = await post('/v1/auth/refresh', {
      refresh_token: laptop.body.refresh_token,
    });
    expect(stillValid.status).toBe(401);
  });

  it('accepts the code in any case, with or without the separator', async () => {
    const account = await registerAccount();
    await requestReset(account.email);
    const code = codeFrom(sent[0]!).toLowerCase().replace('-', '');

    const confirmed = await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: code,
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });

    expect(confirmed.status).toBe(200);
  });

  it('cannot be used twice', async () => {
    const account = await registerAccount();
    await requestReset(account.email);
    const code = codeFrom(sent[0]!);
    const body = {
      email: account.email,
      reset_token: code,
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    };

    expect((await post('/v1/auth/reset/confirm', body)).status).toBe(200);
    expect((await post('/v1/auth/reset/confirm', body)).status).toBe(401);
  });

  it('locks out after five wrong codes', async () => {
    // Eight base32 characters is 40 bits, but an unbounded attempt count would still be a mistake.
    const account = await registerAccount();
    await requestReset(account.email);
    const realCode = codeFrom(sent[0]!);

    for (let attempt = 0; attempt < 5; attempt += 1) {
      const wrong = await post('/v1/auth/reset/confirm', {
        email: account.email,
        reset_token: 'ZZZZ-ZZZZ',
        new_auth_key: randomAuthKey(),
        kdf_params: KDF_PARAMS,
      });
      expect(wrong.status).toBe(401);
    }

    // Even the correct code is refused now: the token is burned, and the user asks for a new one.
    const correct = await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: realCode,
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });
    expect(correct.status).toBe(401);
  });

  it('rejects an expired code', async () => {
    const account = await registerAccount();
    await requestReset(account.email);
    await env.DB.prepare("UPDATE reset_tokens SET expires_utc = '2020-01-01T00:00:00.0000000Z'").run();

    const expired = await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: codeFrom(sent[0]!),
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });

    expect(expired.status).toBe(401);
  });

  it('rejects a malformed code without touching the account', async () => {
    const account = await registerAccount();
    await requestReset(account.email);

    const malformed = await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: 'not a code!!',
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });

    expect(malformed.status).toBe(400);
    expect((await loginAccount(account)).status).toBe(200);
  });

  it('gives one account\'s code no power over another', async () => {
    const alice = await registerAccount();
    const bob = await registerAccount();
    await requestReset(alice.email);
    const aliceCode = codeFrom(sent[0]!);

    const crossed = await post('/v1/auth/reset/confirm', {
      email: bob.email,
      reset_token: aliceCode,
      new_auth_key: randomAuthKey(),
      kdf_params: KDF_PARAMS,
    });

    expect(crossed.status).toBe(401);
    expect((await loginAccount(bob)).status).toBe(200);
  });
});

describe('re-wrapping after a reset', () => {
  async function resetAndSignIn(): Promise<{ token: string; generation: number }> {
    const account = await registerAccount();
    await requestReset(account.email);
    const newAuthKey = randomAuthKey();
    await post('/v1/auth/reset/confirm', {
      email: account.email,
      reset_token: codeFrom(sent[0]!),
      new_auth_key: newAuthKey,
      kdf_params: KDF_PARAMS,
    });

    const session = await post('/v1/auth/login', { email: account.email, auth_key: newAuthKey });
    return { token: session.body.access_token, generation: session.body.dek_generation };
  }

  it('clears the locked state and bumps the generation', async () => {
    const { token, generation } = await resetAndSignIn();
    const rewrapped = fakeWrappedDek();

    const response = await post(
      '/v1/auth/rewrap',
      { new_wrapped_dek_pw: rewrapped, dek_generation: generation },
      { token },
    );

    expect(response.status).toBe(200);
    expect(response.body.rewrap_pending).toBe(false);
    expect(response.body.dek_generation).toBe(generation + 1);

    const me = await get('/v1/auth/me', { token });
    expect(me.body.rewrap_pending).toBe(false);
  });

  it('stores the envelope the client supplied', async () => {
    const { token, generation } = await resetAndSignIn();
    const rewrapped = fakeWrappedDek();

    await post(
      '/v1/auth/rewrap',
      { new_wrapped_dek_pw: rewrapped, dek_generation: generation },
      { token },
    );

    const row = await env.DB.prepare('SELECT wrapped_dek_pw FROM users').first<{ wrapped_dek_pw: string }>();
    expect(row!.wrapped_dek_pw).toBe(rewrapped);
  });

  it('refuses a stale generation, so two devices unlocking at once cannot clobber each other', async () => {
    const { token, generation } = await resetAndSignIn();
    await post(
      '/v1/auth/rewrap',
      { new_wrapped_dek_pw: fakeWrappedDek(), dek_generation: generation },
      { token },
    );

    const second = await post(
      '/v1/auth/rewrap',
      { new_wrapped_dek_pw: fakeWrappedDek(), dek_generation: generation },
      { token },
    );

    expect(second.status).toBe(400);
  });

  it('requires a session', async () => {
    const response = await post('/v1/auth/rewrap', {
      new_wrapped_dek_pw: fakeWrappedDek(),
      dek_generation: 1,
    });
    expect(response.status).toBe(401);
  });

  it('rejects an envelope that is not a v1 wrapped key', async () => {
    const { token, generation } = await resetAndSignIn();

    const response = await post(
      '/v1/auth/rewrap',
      { new_wrapped_dek_pw: 'v1.short.short', dek_generation: generation },
      { token },
    );

    expect(response.status).toBe(400);
  });
});
