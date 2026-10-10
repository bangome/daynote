import { beforeEach, describe, expect, it } from 'vitest';
import {
  REDIRECT_URI,
  env,
  get,
  post,
  resetDatabase,
  signIn,
  signInAgain,
  stubGoogle,
  MOBILE_REDIRECT_URI,
  recordGoogleClient,
  unstubGoogle,
  sha256HexOf,
} from './helpers';
import { REUSE_GRACE_SECONDS } from '../src/sessions';
import { canonicalUtc } from '../src/time';

beforeEach(resetDatabase);

/** Moves the rotation that retired `oldToken` `secondsAgo` into the past. */
async function ageRotation(oldToken: string, secondsAgo: number): Promise<void> {
  const then = canonicalUtc(new Date(Date.now() - secondsAgo * 1000));
  await env.DB.prepare('UPDATE refresh_tokens SET revoked_utc = ?2 WHERE token_hash = ?1')
    .bind(await sha256HexOf(oldToken), then)
    .run();
}

async function liveTokens(userId: string): Promise<number> {
  const row = await env.DB.prepare(
    'SELECT COUNT(*) AS n FROM refresh_tokens WHERE user_id = ?1 AND revoked_utc IS NULL',
  ).bind(userId).first<{ n: number }>();
  return row!.n;
}

function signInBody(code: string, overrides: Record<string, unknown> = {}) {
  return {
    code,
    code_verifier: 'a'.repeat(43),
    redirect_uri: REDIRECT_URI,
    device_name: 'Test PC',
    ...overrides,
  };
}

describe('google sign-in', () => {
  it('creates the account on first use and hands back a session and the data key', async () => {
    stubGoogle({ 'code-1': { subject: 'sub-1', email: 'alice@example.test' } });

    const response = await post('/v1/auth/google', signInBody('code-1'));

    expect(response.status).toBe(200);
    expect(response.body.user_id).toMatch(/^[0-9a-f-]{36}$/);
    expect(response.body.email).toBe('alice@example.test');
    expect(response.body.access_token.split('.')).toHaveLength(3);
    expect(response.body.refresh_token).toBeTypeOf('string');
    // 32 bytes, base64url: the key the client encrypts note bodies with.
    expect(response.body.data_key).toMatch(/^[A-Za-z0-9_-]{43}$/);
  });

  it('reuses the account on the second sign-in rather than creating another', async () => {
    const account = await signIn();
    const again = await signInAgain(account);

    expect(again.status).toBe(200);
    expect(again.body.user_id).toBe(account.userId);
    // Same account, therefore the same data key — a second device must be able to read what the
    // first one wrote.
    expect(again.body.data_key).toBe(account.dataKey);

    const count = await env.DB.prepare('SELECT COUNT(*) AS n FROM users').first<{ n: number }>();
    expect(count?.n).toBe(1);
  });

  it('follows the Google subject, not the address, when the address changes', async () => {
    const account = await signIn({ email: 'old@example.test' });

    const code = 'code-renamed';
    stubGoogle({ [code]: { subject: account.subject, email: 'new@example.test' } });
    const renamed = await post('/v1/auth/google', signInBody(code));

    expect(renamed.status).toBe(200);
    expect(renamed.body.user_id).toBe(account.userId);
    expect(renamed.body.email).toBe('new@example.test');
  });

  it('treats a different Google subject as a different person, even on the same address', async () => {
    const first = await signIn({ email: 'shared@example.test' });
    const second = await signIn({ email: 'shared@example.test' });

    expect(second.userId).not.toBe(first.userId);
    expect(second.dataKey).not.toBe(first.dataKey);
  });

  it('rejects a code Google will not redeem', async () => {
    stubGoogle({ 'good-code': { subject: 'sub-x', email: 'x@example.test' } });

    const response = await post('/v1/auth/google', signInBody('replayed-code'));

    expect(response.status).toBe(401);
    expect(response.body.error).toBe('invalid_credentials');
  });

  it('requires the code, the verifier, and the redirect', async () => {
    stubGoogle({ 'code-2': { subject: 'sub-2', email: 'b@example.test' } });

    for (const missing of ['code', 'code_verifier', 'redirect_uri']) {
      const body: Record<string, unknown> = signInBody('code-2');
      delete body[missing];
      const response = await post('/v1/auth/google', body);
      expect(response.status, missing).toBe(400);
      expect(response.body.error, missing).toBe('bad_request');
    }
  });

  it('refuses a redirect that is not a loopback address', async () => {
    stubGoogle({ 'code-3': { subject: 'sub-3', email: 'c@example.test' } });

    const response = await post(
      '/v1/auth/google',
      signInBody('code-3', { redirect_uri: 'https://attacker.example/callback' }),
    );

    expect(response.status).toBe(400);
    expect(response.body.error).toBe('bad_request');
  });

  it('seals the stored data key, so the row alone does not reveal it', async () => {
    const account = await signIn();

    const row = await env.DB.prepare('SELECT wrapped_dek FROM users WHERE id = ?1')
      .bind(account.userId)
      .first<{ wrapped_dek: string }>();

    expect(row?.wrapped_dek).toMatch(/^s1\.[A-Za-z0-9_-]{16}\.[A-Za-z0-9_-]{64}$/);
    expect(row?.wrapped_dek).not.toContain(account.dataKey);
  });

  it('rate-limits sign-in attempts per IP', async () => {
    stubGoogle({});

    let limited = false;
    for (let attempt = 0; attempt < 25; attempt += 1) {
      const response = await post('/v1/auth/google', signInBody(`junk-${attempt}`), {
        ip: '198.51.100.9',
      });
      if (response.status === 429) {
        limited = true;
        break;
      }
    }

    expect(limited).toBe(true);
  });
});

describe('sessions', () => {
  it('rotates the refresh token and does not re-issue the data key', async () => {
    const account = await signIn();

    const response = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });

    expect(response.status).toBe(200);
    expect(response.body.user_id).toBe(account.userId);
    expect(response.body.refresh_token).not.toBe(account.refreshToken);
    expect(response.body.data_key).toBeUndefined();
  });

  it('revokes the whole family when a rotated token is presented after its successor was used', async () => {
    const account = await signIn();
    const rotated = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    const next = await post('/v1/auth/refresh', { refresh_token: rotated.body.refresh_token });
    expect(next.status).toBe(200);

    const replay = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    expect(replay.status).toBe(401);

    const afterReplay = await post('/v1/auth/refresh', { refresh_token: next.body.refresh_token });
    expect(afterReplay.status).toBe(401);
  });

  it('revokes the whole family when a rotated token is presented after the grace window', async () => {
    const account = await signIn();
    const rotated = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    await ageRotation(account.refreshToken, REUSE_GRACE_SECONDS + 5);

    const replay = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    expect(replay.status).toBe(401);

    const afterReplay = await post('/v1/auth/refresh', { refresh_token: rotated.body.refresh_token });
    expect(afterReplay.status).toBe(401);
  });

  it('answers a retry whose response was lost with a fresh pair in the same family', async () => {
    // The phone was suspended mid-refresh: the server rotated, the app never saw the new token,
    // and on resume it presents the old one again.
    const account = await signIn();
    const lost = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    await ageRotation(account.refreshToken, REUSE_GRACE_SECONDS - 10);

    const retry = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    expect(retry.status).toBe(200);
    expect(retry.body.refresh_token).not.toBe(lost.body.refresh_token);
    expect(retry.body.user_id).toBe(account.userId);

    // The pair the app now holds keeps working; the one it never received is retired.
    const onward = await post('/v1/auth/refresh', { refresh_token: retry.body.refresh_token });
    expect(onward.status).toBe(200);
    const families = await env.DB.prepare(
      'SELECT COUNT(DISTINCT family_id) AS n FROM refresh_tokens WHERE user_id = ?1',
    ).bind(account.userId).first<{ n: number }>();
    expect(families?.n).toBe(1);
    expect(await liveTokens(account.userId)).toBe(1);
  });

  it('lets only one of two concurrent retries of a rotated token through, never two chains', async () => {
    const account = await signIn();
    const lost = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    expect(lost.status).toBe(200);

    const retries = await Promise.all([
      post('/v1/auth/refresh', { refresh_token: account.refreshToken }),
      post('/v1/auth/refresh', { refresh_token: account.refreshToken }),
    ]);

    expect(retries.filter((retry) => retry.status === 200).length).toBeLessThanOrEqual(1);
    expect(await liveTokens(account.userId)).toBeLessThanOrEqual(1);
    for (const retry of retries.filter((retry) => retry.status === 200)) {
      const onward = await post('/v1/auth/refresh', { refresh_token: retry.body.refresh_token });
      // Either the one winner carries on, or the loser's reuse burned the family it belonged to.
      expect([200, 401]).toContain(onward.status);
    }
    expect(await liveTokens(account.userId)).toBeLessThanOrEqual(1);
  });

  it('never forks a chain when the same live token is refreshed twice at once', async () => {
    const account = await signIn();

    const refreshes = await Promise.all([
      post('/v1/auth/refresh', { refresh_token: account.refreshToken }),
      post('/v1/auth/refresh', { refresh_token: account.refreshToken }),
    ]);

    expect(refreshes.some((refresh) => refresh.status === 200)).toBe(true);
    expect(await liveTokens(account.userId)).toBe(1);
    // Exactly one rotation of the presented token: the loser was answered by the grace or refused.
    const rotations = await env.DB.prepare(
      `SELECT COUNT(*) AS n FROM refresh_tokens WHERE user_id = ?1 AND revoke_reason = 'rotated'`,
    ).bind(account.userId).first<{ n: number }>();
    expect(rotations?.n).toBe(1);
    const working = [];
    for (const refresh of refreshes.filter((refresh) => refresh.status === 200)) {
      const row = await env.DB.prepare('SELECT revoked_utc FROM refresh_tokens WHERE token_hash = ?1')
        .bind(await sha256HexOf(refresh.body.refresh_token))
        .first<{ revoked_utc: string | null }>();
      if (row?.revoked_utc === null) {
        working.push(refresh);
      }
    }
    expect(working.length).toBe(1);
  });

  it('revokes the family when the successor a grace retired is used', async () => {
    const account = await signIn();
    const lost = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    const retry = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    expect(retry.status).toBe(200);

    const retired = await post('/v1/auth/refresh', { refresh_token: lost.body.refresh_token });
    expect(retired.status).toBe(401);
    expect(await liveTokens(account.userId)).toBe(0);
    expect((await post('/v1/auth/refresh', { refresh_token: retry.body.refresh_token })).status).toBe(401);
  });

  it('stops two holders from alternating through the grace', async () => {
    // A thief and the client share A. The client rotates A to B; the thief replays A inside the
    // window and gets C, which retires B. B must not then be grace-eligible itself.
    const account = await signIn();
    const client = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    const thief = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    expect(thief.status).toBe(200);

    expect((await post('/v1/auth/refresh', { refresh_token: client.body.refresh_token })).status).toBe(401);
    expect((await post('/v1/auth/refresh', { refresh_token: thief.body.refresh_token })).status).toBe(401);
    expect((await post('/v1/auth/refresh', { refresh_token: account.refreshToken })).status).toBe(401);
    expect(await liveTokens(account.userId)).toBe(0);
  });

  it('does not grant a second grace to the same rotated token', async () => {
    const account = await signIn();
    await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    expect((await post('/v1/auth/refresh', { refresh_token: account.refreshToken })).status).toBe(200);
    expect((await post('/v1/auth/refresh', { refresh_token: account.refreshToken })).status).toBe(401);
    expect(await liveTokens(account.userId)).toBe(0);
  });

  it('logs out the whole family when the token presented has already been rotated', async () => {
    const account = await signIn();
    const rotated = await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    await ageRotation(account.refreshToken, REUSE_GRACE_SECONDS + 5);

    expect((await post('/v1/auth/logout', { refresh_token: account.refreshToken })).status).toBe(204);
    expect(await liveTokens(account.userId)).toBe(0);
    expect((await post('/v1/auth/refresh', { refresh_token: rotated.body.refresh_token })).status).toBe(401);
  });

  it('does not let a token revoked before revoke_reason existed through the grace', async () => {
    const account = await signIn();
    await post('/v1/auth/refresh', { refresh_token: account.refreshToken });
    await env.DB.prepare(
      'UPDATE refresh_tokens SET revoke_reason = NULL, replaced_by = NULL WHERE token_hash = ?1',
    ).bind(await sha256HexOf(account.refreshToken)).run();

    expect((await post('/v1/auth/refresh', { refresh_token: account.refreshToken })).status).toBe(401);
    expect(await liveTokens(account.userId)).toBe(0);
  });

  it('does not let a logged-out token back in through the grace window', async () => {
    const account = await signIn();
    expect((await post('/v1/auth/logout', { refresh_token: account.refreshToken })).status).toBe(204);
    expect((await post('/v1/auth/refresh', { refresh_token: account.refreshToken })).status).toBe(401);
  });

  it('logs out without saying whether the token existed', async () => {
    const account = await signIn();

    expect((await post('/v1/auth/logout', { refresh_token: account.refreshToken })).status).toBe(204);
    expect((await post('/v1/auth/logout', { refresh_token: 'never-issued' })).status).toBe(204);
    expect((await post('/v1/auth/refresh', { refresh_token: account.refreshToken })).status).toBe(401);
  });
});

describe('me', () => {
  it('reports the account and its devices', async () => {
    const account = await signIn({ device: 'Desk PC' });
    await signInAgain(account, 'Laptop');

    const response = await get('/v1/auth/me', { token: account.accessToken });

    expect(response.status).toBe(200);
    expect(response.body.email).toBe(account.email);
    expect(response.body.devices.map((device: { device_name: string }) => device.device_name)).toEqual(
      expect.arrayContaining(['Desk PC', 'Laptop']),
    );
  });

  it('rejects a missing or forged access token', async () => {
    expect((await get('/v1/auth/me')).status).toBe(401);
    expect((await get('/v1/auth/me', { token: 'not.a.token' })).status).toBe(401);
  });
});

describe('data key', () => {
  it('re-issues the same key to a client that lost its copy', async () => {
    const account = await signIn();

    const response = await get('/v1/auth/data-key', { token: account.accessToken });

    expect(response.status).toBe(200);
    expect(response.body.data_key).toBe(account.dataKey);
  });

  it('needs an access token', async () => {
    expect((await get('/v1/auth/data-key')).status).toBe(401);
  });
});

describe('routing', () => {
  it('no longer exposes the password endpoints', async () => {
    for (const path of ['/v1/auth/register', '/v1/auth/login', '/v1/auth/password',
      '/v1/auth/reset/request', '/v1/auth/reset/confirm', '/v1/auth/rewrap']) {
      const response = await post(path, {});
      expect(response.status, path).toBe(404);
    }
  });
});

describe('signing in from a phone', () => {
  it('exchanges the code against that platform’s own OAuth client', async () => {
    const recorded = recordGoogleClient();

    const response = await post('/v1/auth/google', {
      code: 'code-ios',
      code_verifier: 'a'.repeat(43),
      redirect_uri: MOBILE_REDIRECT_URI,
      client: 'ios',
      device_name: 'iPhone',
    });

    expect(response.status).toBe(200);
    expect(recorded.seen).toEqual(['ios']);
    expect(response.body.email).toBe('ios@example.test');
  });

  it('accepts the app’s private scheme as the redirect', async () => {
    recordGoogleClient();

    const response = await post('/v1/auth/google', {
      code: 'code-android',
      code_verifier: 'a'.repeat(43),
      redirect_uri: MOBILE_REDIRECT_URI,
      client: 'android',
      device_name: 'Pixel',
    });

    expect(response.status).toBe(200);
  });

  // The shapes are checked per client so a caller cannot nominate a redirect the app it claims to
  // be could never have received the code on.
  it('refuses a loopback redirect from a phone', async () => {
    recordGoogleClient();

    const response = await post('/v1/auth/google', {
      code: 'code-1',
      code_verifier: 'a'.repeat(43),
      redirect_uri: REDIRECT_URI,
      client: 'ios',
    });

    expect(response.status).toBe(400);
    expect(response.body.message).toMatch(/own URI scheme/);
  });

  it('refuses a private scheme from the desktop app', async () => {
    recordGoogleClient();

    const response = await post('/v1/auth/google', {
      code: 'code-1',
      code_verifier: 'a'.repeat(43),
      redirect_uri: MOBILE_REDIRECT_URI,
    });

    expect(response.status).toBe(400);
    expect(response.body.message).toMatch(/loopback/);
  });

  it('refuses an https redirect dressed up as a private scheme', async () => {
    recordGoogleClient();

    const response = await post('/v1/auth/google', {
      code: 'code-1',
      code_verifier: 'a'.repeat(43),
      redirect_uri: 'https://evil.example/oauth2redirect',
      client: 'android',
    });

    expect(response.status).toBe(400);
  });

  it('rejects a client name it does not know', async () => {
    recordGoogleClient();

    const response = await post('/v1/auth/google', {
      code: 'code-1',
      code_verifier: 'a'.repeat(43),
      redirect_uri: MOBILE_REDIRECT_URI,
      client: 'web',
    });

    expect(response.status).toBe(400);
    expect(response.body.message).toMatch(/desktop, ios or android/);
  });

  // A platform whose client id has not been set has to read as a refusal the phone can show
  // rather than a 500. The id is cleared on env for the one call instead of leaning on
  // wrangler.toml still being blank: it is not blank any more, and a test that passes only until
  // someone fills in a config value is a test that was measuring the config, not the code.
  it('says so plainly when that platform has no client configured', async () => {
    unstubGoogle();

    const configured = env.GOOGLE_IOS_CLIENT_ID;
    (env as { GOOGLE_IOS_CLIENT_ID?: string }).GOOGLE_IOS_CLIENT_ID = '';
    try {
      const response = await post('/v1/auth/google', {
        code: 'code-1',
        code_verifier: 'a'.repeat(43),
        redirect_uri: MOBILE_REDIRECT_URI,
        client: 'ios',
      });

      expect(response.status).toBe(400);
      expect(response.body.message).toMatch(/not configured on this server yet/);
    } finally {
      (env as { GOOGLE_IOS_CLIENT_ID?: string }).GOOGLE_IOS_CLIENT_ID = configured;
    }
  });
});
