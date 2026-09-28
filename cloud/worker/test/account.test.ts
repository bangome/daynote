import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  appleTokenResponse,
  configureApple,
  del,
  env,
  get,
  grantSubscription,
  jsonResponse,
  mockFetch,
  post,
  putAsset,
  resetDatabase,
  signIn,
  toBase64Url,
  unconfigureApple,
  type Account,
} from './helpers';

/**
 * Account deletion (src/account.ts). The two properties that matter most, asserted from several
 * directions: **everything** the service holds for the account goes, and **nothing** of anyone
 * else's does. Then the billing rule: an account that would keep being charged is never deleted.
 */

beforeEach(async () => {
  await resetDatabase();
  delete (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY;
});

afterEach(() => {
  vi.restoreAllMocks();
  unconfigureApple();
  delete (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY;
});

function envelope(marker = 'x'): string {
  const nonce = toBase64Url(crypto.getRandomValues(new Uint8Array(12)));
  const body = toBase64Url(new TextEncoder().encode(marker.padEnd(32, '.')));
  return `v1.${nonce}.${body}`;
}

const STAMP = '2026-09-20T09:00:00.0000000Z';
const PER_USER_TABLES = ['notes', 'change_log', 'files', 'assets', 'refresh_tokens', 'subscriptions'];

function key(marker: string): string {
  return marker.padStart(64, '0');
}

/** Gives an account one of everything: a note, a file, its bytes, a stray object, a payment record. */
async function populate(account: Account, marker: string): Promise<void> {
  const token = account.accessToken;
  const noteId = `00000000-0000-4000-8000-00000000000${marker}`;

  expect((await post('/v1/sync/push', {
    notes: [{ id: noteId, payload: envelope(marker), updated_utc: STAMP }],
  }, { token })).status).toBe(200);

  expect((await post('/v1/files/push', {
    files: [{
      id: noteId, payload: envelope(marker), blinded_key: key(marker), stored_bytes: 4, updated_utc: STAMP,
    }],
  }, { token })).status).toBe(200);
  expect((await putAsset(key(marker), new Uint8Array([1, 2, 3, 4]), token)).status).toBe(200);

  // An object with no row behind it — an upload whose metadata never arrived. It is still theirs.
  await env.ASSET_BUCKET!.put(`${account.userId}/${key(`${marker}f`)}`, new Uint8Array([9]));

  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO subscriptions (user_id, customer_id, subscription_id, status, updated_utc)
       VALUES (?1, 'ctm_x', 'sub_x', 'canceled', ?2)`,
    ).bind(account.userId, STAMP),
    env.DB.prepare(
      `INSERT INTO billing_events (event_id, event_type, user_id, received_utc)
       VALUES (?1, 'subscription.canceled', ?2, ?3)`,
    ).bind(`evt_${marker}`, account.userId, STAMP),
  ]);
}

async function rowsFor(userId: string): Promise<Record<string, number>> {
  const counts: Record<string, number> = {};
  for (const table of PER_USER_TABLES) {
    const row = await env.DB.prepare(`SELECT COUNT(*) AS n FROM ${table} WHERE user_id = ?1`)
      .bind(userId)
      .first<{ n: number }>();
    counts[table] = row?.n ?? -1;
  }
  const user = await env.DB.prepare('SELECT COUNT(*) AS n FROM users WHERE id = ?1')
    .bind(userId)
    .first<{ n: number }>();
  counts['users'] = user?.n ?? -1;
  const events = await env.DB.prepare('SELECT COUNT(*) AS n FROM billing_events WHERE user_id = ?1')
    .bind(userId)
    .first<{ n: number }>();
  counts['billing_events'] = events?.n ?? -1;
  const limits = await env.DB.prepare('SELECT COUNT(*) AS n FROM rate_limits WHERE bucket LIKE ?1')
    .bind(`%:user:${userId}:%`)
    .first<{ n: number }>();
  counts['rate_limits'] = limits?.n ?? -1;
  return counts;
}

async function objectsFor(userId: string): Promise<string[]> {
  const listed = await env.ASSET_BUCKET!.list({ prefix: `${userId}/` });
  return listed.objects.map((object) => object.key);
}

describe('DELETE /v1/account', () => {
  it('removes every row and every object the account had, and nobody else’s', async () => {
    const doomed = await signIn();
    const bystander = await signIn();
    await populate(doomed, '1');
    await populate(bystander, '2');
    const bystanderBefore = await rowsFor(bystander.userId);

    const response = await del('/v1/account', { token: doomed.accessToken });

    expect(response.status).toBe(204);
    expect(await rowsFor(doomed.userId)).toEqual({
      notes: 0, change_log: 0, files: 0, assets: 0, refresh_tokens: 0, subscriptions: 0,
      users: 0, billing_events: 0, rate_limits: 0,
    });
    expect(await objectsFor(doomed.userId)).toEqual([]);

    expect(await rowsFor(bystander.userId)).toEqual(bystanderBefore);
    expect(await objectsFor(bystander.userId)).toHaveLength(2);
    expect((await get('/v1/sync/pull?since=0', { token: bystander.accessToken })).body.changes)
      .toHaveLength(2);
  });

  it('keeps the payment record but not the account id on it', async () => {
    const account = await signIn();
    await populate(account, '3');

    await del('/v1/account', { token: account.accessToken });

    const event = await env.DB.prepare(
      'SELECT event_type, user_id FROM billing_events WHERE event_id = ?1',
    )
      .bind('evt_3')
      .first();
    expect(event).toEqual({ event_type: 'subscription.canceled', user_id: null });
  });

  it('leaves the old tokens useless', async () => {
    const account = await signIn();

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);

    expect((await get('/v1/auth/me', { token: account.accessToken })).status).toBe(401);
    expect((await post('/v1/auth/refresh', { refresh_token: account.refreshToken })).status).toBe(401);
    // A retry after success reads as unauthorized, which the app treats as "already gone".
    const again = await del('/v1/account', { token: account.accessToken });
    expect(again.status).toBe(401);
    expect(again.body.error).toBe('unauthorized');
  });

  it('needs an access token', async () => {
    expect((await del('/v1/account')).status).toBe(401);
  });

  it('also removes an object another device uploads while the deletion is running', async () => {
    const account = await signIn();
    const late = `${account.userId}/${key('late')}`;

    // The upload lands after the first R2 pass, just before the rows go: the D1 batches are the
    // seam between the two passes (the first one is the rate-limit count, before either pass).
    const batch = env.DB.batch.bind(env.DB);
    vi.spyOn(env.DB, 'batch').mockImplementation(async (statements) => {
      await env.ASSET_BUCKET!.put(late, new Uint8Array([7]));
      return batch(statements);
    });

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);
    vi.restoreAllMocks();

    expect(await env.ASSET_BUCKET!.get(late)).toBeNull();
    expect(await objectsFor(account.userId)).toEqual([]);
  });

  it('lets the same Google account sign up afresh afterwards, as a new account', async () => {
    const account = await signIn({ subject: 'returning-sub' });
    await del('/v1/account', { token: account.accessToken });

    const again = await signIn({ subject: 'returning-sub' });

    expect(again.userId).not.toBe(account.userId);
    expect(again.dataKey).not.toBe(account.dataKey);
  });
});

describe('deleting an account with a subscription', () => {
  it('refuses, deleting nothing, when this server cannot cancel it', async () => {
    const account = await signIn();
    await grantSubscription(account.userId);
    const calls = mockFetch(() => jsonResponse({}));

    const response = await del('/v1/account', { token: account.accessToken });

    expect(response.status).toBe(409);
    expect(response.body.error).toBe('subscription_active');
    expect(response.body.message).toMatch(/Cancel your subscription/);
    expect(calls).toHaveLength(0);
    expect((await get('/v1/auth/me', { token: account.accessToken })).status).toBe(200);
  });

  it('cancels it at Paddle, immediately, and then deletes', async () => {
    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const account = await signIn();
    await grantSubscription(account.userId);
    const calls = mockFetch(() => jsonResponse({ data: { id: 'sub_test', status: 'canceled' } }));

    const response = await del('/v1/account', { token: account.accessToken });

    expect(response.status).toBe(204);
    expect(calls).toHaveLength(1);
    expect(calls[0]!.method).toBe('POST');
    expect(calls[0]!.url).toBe('https://api.paddle.com/subscriptions/sub_test/cancel');
    expect(calls[0]!.headers.get('authorization')).toBe('Bearer pdl_test_key');
    expect(JSON.parse(calls[0]!.body)).toEqual({ effective_from: 'immediately' });
    expect((await rowsFor(account.userId)).users).toBe(0);
  });

  it('carries on when Paddle says it was already cancelled', async () => {
    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const account = await signIn();
    await grantSubscription(account.userId);
    const calls = mockFetch((call) => call.url.endsWith('/cancel')
      ? jsonResponse({ error: { code: 'subscription_update_when_canceled' } }, 400)
      : jsonResponse({ data: { id: 'sub_test', status: 'canceled' } }));

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);
    expect(calls.map((call) => call.method)).toEqual(['POST', 'GET']);
  });

  it('deletes nothing when Paddle cannot cancel it', async () => {
    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const account = await signIn();
    await grantSubscription(account.userId);
    mockFetch((call) => call.url.endsWith('/cancel')
      ? jsonResponse({ error: { code: 'internal_error' } }, 500)
      : jsonResponse({ data: { id: 'sub_test', status: 'active' } }));

    const response = await del('/v1/account', { token: account.accessToken });

    expect(response.status).toBe(500);
    expect(response.body.message).toMatch(/nothing was deleted/);
    const row = await env.DB.prepare('SELECT status FROM subscriptions WHERE user_id = ?1')
      .bind(account.userId)
      .first<{ status: string }>();
    expect(row?.status).toBe('active');
    expect((await get('/v1/auth/me', { token: account.accessToken })).status).toBe(200);
  });

  it('does not call Paddle for a subscription that is already cancelled', async () => {
    const account = await signIn();
    await populate(account, '4');
    const calls = mockFetch(() => jsonResponse({}));

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);
    expect(calls).toHaveLength(0);
  });

  it('treats a past-due subscription as one that can still charge', async () => {
    const account = await signIn();
    await grantSubscription(account.userId);
    await env.DB.prepare("UPDATE subscriptions SET status = 'past_due' WHERE user_id = ?1")
      .bind(account.userId)
      .run();

    const response = await del('/v1/account', { token: account.accessToken });

    expect(response.status).toBe(409);
    expect(response.body.error).toBe('subscription_active');
  });

  it('finishes on a retry after failing half-way, without cancelling twice', async () => {
    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const account = await signIn();
    await populate(account, '5');
    await env.DB.prepare("UPDATE subscriptions SET status = 'active' WHERE user_id = ?1")
      .bind(account.userId)
      .run();
    const calls = mockFetch(() => jsonResponse({ data: { status: 'canceled' } }));

    // R2 falls over after Paddle has cancelled: the account must survive intact enough to retry.
    const list = vi.spyOn(env.ASSET_BUCKET!, 'list').mockRejectedValueOnce(new Error('R2 is down'));
    const first = await del('/v1/account', { token: account.accessToken });
    expect(first.status).toBe(500);
    expect((await rowsFor(account.userId)).users).toBe(1);
    list.mockRestore();

    const second = await del('/v1/account', { token: account.accessToken });

    expect(second.status).toBe(204);
    expect(calls.filter((call) => call.url.endsWith('/cancel'))).toHaveLength(1);
    expect((await rowsFor(account.userId)).users).toBe(0);
    expect(await objectsFor(account.userId)).toEqual([]);
  });

  it('looks the subscription up again when the stored id is not a subscription id', async () => {
    // What a row written before the webhook fix can hold: a transaction id where sub_... belongs.
    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const account = await signIn();
    await grantSubscription(account.userId);
    await env.DB.prepare("UPDATE subscriptions SET subscription_id = 'txn_stale' WHERE user_id = ?1")
      .bind(account.userId)
      .run();
    const calls = mockFetch((call) => call.method === 'GET'
      ? jsonResponse({ data: [{ id: 'sub_found', status: 'active' }] })
      : jsonResponse({ data: { id: 'sub_found', status: 'canceled' } }));

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);

    expect(calls).toHaveLength(2);
    const lookup = new URL(calls[0]!.url);
    expect(lookup.origin + lookup.pathname).toBe('https://api.paddle.com/subscriptions');
    expect(lookup.searchParams.get('customer_id')).toBe('ctm_test');
    expect(calls[1]!.url).toBe('https://api.paddle.com/subscriptions/sub_found/cancel');
    expect(calls.some((call) => call.url.includes('txn_stale'))).toBe(false);
  });

  it('refuses rather than failing forever when that lookup is not possible', async () => {
    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const account = await signIn();
    await grantSubscription(account.userId);
    await env.DB.prepare("UPDATE subscriptions SET subscription_id = 'txn_stale' WHERE user_id = ?1")
      .bind(account.userId)
      .run();
    const calls = mockFetch(() => jsonResponse({ error: { code: 'internal_error' } }, 500));

    const response = await del('/v1/account', { token: account.accessToken });

    expect(response.status).toBe(409);
    expect(response.body.error).toBe('subscription_active');
    expect(calls.every((call) => call.method === 'GET')).toBe(true);
    expect((await get('/v1/auth/me', { token: account.accessToken })).status).toBe(200);
  });

  it('is rate-limited per account, refusals included', async () => {
    const account = await signIn();
    await grantSubscription(account.userId);

    const statuses: number[] = [];
    for (let attempt = 0; attempt < 12; attempt += 1) {
      const response = await del('/v1/account', { token: account.accessToken });
      statuses.push(response.status);
      if (response.status === 429) {
        expect(response.body.error).toBe('rate_limited');
        break;
      }
    }

    expect(statuses.at(-1)).toBe(429);
    expect(statuses.slice(0, -1).every((status) => status === 409)).toBe(true);
  });
});

describe('deleting an account that signed in with Apple', () => {
  const NONCE = 'deletion-nonce';

  async function appleAccount(): Promise<{ userId: string; accessToken: string }> {
    await configureApple();
    mockFetch(() => appleTokenResponse(NONCE, {}, 'apple-refresh-to-revoke'));
    const response = await post('/v1/auth/apple', { authorization_code: 'c', nonce: NONCE });
    expect(response.status).toBe(200);
    vi.restoreAllMocks();
    return { userId: response.body.user_id, accessToken: response.body.access_token };
  }

  it('revokes the stored Apple token', async () => {
    const account = await appleAccount();
    const calls = mockFetch(() => new Response(null, { status: 200 }));

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);

    expect(calls).toHaveLength(1);
    expect(calls[0]!.url).toBe('https://appleid.apple.com/auth/revoke');
    const form = new URLSearchParams(calls[0]!.body);
    expect(form.get('token')).toBe('apple-refresh-to-revoke');
    expect(form.get('token_type_hint')).toBe('refresh_token');
    expect(form.get('client_id')).toBe('cc.arachat.daynote');
    expect(form.get('client_secret')?.split('.')).toHaveLength(3);
    expect((await rowsFor(account.userId)).users).toBe(0);
  });

  it('still deletes when Apple will not revoke', async () => {
    const account = await appleAccount();
    const calls = mockFetch(() => jsonResponse({ error: 'invalid_client' }, 400));

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);
    expect(calls).toHaveLength(1);
    expect((await rowsFor(account.userId)).users).toBe(0);
  });

  it('still deletes when Apple sign-in has since been unconfigured', async () => {
    const account = await appleAccount();
    unconfigureApple();
    const calls = mockFetch(() => new Response(null, { status: 200 }));

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);
    expect(calls).toHaveLength(0);
    expect((await rowsFor(account.userId)).users).toBe(0);
  });

  it('does not call Apple for a Google account', async () => {
    await configureApple();
    const account = await signIn();
    const calls = mockFetch(() => new Response(null, { status: 200 }));

    expect((await del('/v1/account', { token: account.accessToken })).status).toBe(204);
    expect(calls).toHaveLength(0);
  });
});
