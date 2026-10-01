import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  del,
  env,
  expireEntitlement,
  get,
  grantSubscription,
  jsonResponse,
  mockFetch,
  post,
  resetDatabase,
  signIn,
  toBase64Url,
} from './helpers';

/**
 * Subscriptions (docs/CLOUD_SYNC.md §14).
 *
 * Two properties matter more than the rest and are asserted from several directions: an unsigned or
 * replayed webhook must never grant entitlement, and a lapse must stop sync **without deleting
 * anything**.
 */

const SECRET = 'test-paddle-webhook-secret';

beforeEach(async () => {
  await resetDatabase();
  (env as { PADDLE_WEBHOOK_SECRET?: string }).PADDLE_WEBHOOK_SECRET = SECRET;
  (env as { PADDLE_IPS?: unknown }).PADDLE_IPS = async () => ['203.0.113.1/32', '198.51.100.0/24'];
  (env as { PADDLE_PRICE_ID_MONTHLY?: string }).PADDLE_PRICE_ID_MONTHLY = 'pri_test_monthly';
  (env as { PADDLE_PRICE_ID_ANNUAL?: string }).PADDLE_PRICE_ID_ANNUAL = 'pri_test_annual';
  (env as { PADDLE_PORTAL_SESSION?: unknown }).PADDLE_PORTAL_SESSION = async (customerId: string) =>
    `https://portal.paddle.test/${customerId}?token=single-use`;
  (env as { PADDLE_CHECKOUT_SESSION?: unknown }).PADDLE_CHECKOUT_SESSION =
    async (userId: string, email: string, plan: string) =>
      `https://pay.paddle.test/checkout?user=${userId}&email=${encodeURIComponent(email)}&plan=${plan}`;
});

async function sign(body: string, timestamp = Math.floor(Date.now() / 1000)): Promise<string> {
  const key = await crypto.subtle.importKey(
    'raw',
    new TextEncoder().encode(SECRET),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign'],
  );
  const digest = await crypto.subtle.sign(
    'HMAC',
    key,
    new TextEncoder().encode(`${timestamp}:${body}`),
  );
  const hex = [...new Uint8Array(digest)]
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');
  return `ts=${timestamp};h1=${hex}`;
}

/** Posts a webhook the way Paddle would, with the raw body signed byte for byte. */
async function deliver(
  event: Record<string, unknown>,
  overrides: { signature?: string; timestamp?: number; ip?: string } = {},
) {
  const body = JSON.stringify(event);
  const signature = overrides.signature
    ?? (await sign(body, overrides.timestamp ?? Math.floor(Date.now() / 1000)));

  const worker = (await import('../src/index')).default;
  const request = new Request('https://daynote.test/v1/billing/webhook', {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'paddle-signature': signature,
      'cf-connecting-ip': overrides.ip ?? '203.0.113.1',
    },
    body,
  });
  const ctx = { waitUntil: () => {}, passThroughOnException: () => {} } as unknown as ExecutionContext;
  const response = await worker.fetch(request, env as any, ctx);
  return { status: response.status };
}

function subscriptionEvent(
  userId: string,
  overrides: {
    eventId?: string;
    type?: string;
    status?: string;
    endsAt?: string;
    subscriptionId?: string;
  } = {},
) {
  const endsAt = overrides.endsAt
    ?? new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();

  return {
    event_id: overrides.eventId ?? `evt_${crypto.randomUUID()}`,
    event_type: overrides.type ?? 'subscription.activated',
    data: {
      id: overrides.subscriptionId ?? 'sub_abc',
      status: overrides.status ?? 'active',
      customer_id: 'ctm_abc',
      current_billing_period: { ends_at: endsAt },
      custom_data: { user_id: userId },
    },
  };
}

function envelope(marker = 'x'): string {
  const nonce = toBase64Url(crypto.getRandomValues(new Uint8Array(12)));
  const body = toBase64Url(new TextEncoder().encode(marker.padEnd(32, '.')));
  return `v1.${nonce}.${body}`;
}

function pushOne(token: string, id = '00000000-0000-4000-8000-000000000001') {
  return post(
    '/v1/sync/push',
    { notes: [{ id, payload: envelope(), updated_utc: '2026-09-02T09:00:00.0000000Z' }] },
    { token },
  );
}

describe('trial', () => {
  it('lets a brand-new account sync, and says how long for', async () => {
    const account = await signIn();

    const me = await get('/v1/auth/me', { token: account.accessToken });

    expect(me.body.entitlement.state).toBe('trial');
    expect(me.body.entitlement.can_sync_files).toBe(true);
    expect(me.body.entitlement.has_subscribed).toBe(false);
    // A date, because the app has to warn before the trial takes syncing away (policy 10.8.4).
    expect(Date.parse(me.body.entitlement.until)).toBeGreaterThan(Date.now());
    expect((await pushOne(account.accessToken)).status).toBe(200);
  });

  it('is not silently skipped for an account that predates the column', async () => {
    // 0006 added trial_ends_utc with a bare ALTER and no backfill, so every account already in
    // the database was left with NULL — which resolve() reads as "no trial", indistinguishable
    // from "trial finished". Those accounts were never given the 14 days they were promised.
    //
    // No test caught it because every test creates its account through sign-in, which sets the
    // column, and a fresh database has no rows to miss. It only exists in a database that was
    // migrated rather than created — production. 0008 backfills it from created_utc.
    const account = await signIn();
    await env.DB.prepare('UPDATE users SET trial_ends_utc = NULL WHERE id = ?1')
      .bind(account.userId)
      .run();

    const stranded = await get('/v1/auth/me', { token: account.accessToken });
    expect(stranded.body.entitlement.state).toBe('expired');
    expect(stranded.body.entitlement.can_sync_files).toBe(false);

    // What 0008 does, run here against the same SQLite the migration runs against, so the date
    // arithmetic and the canonical format are exercised rather than assumed.
    await env.DB.prepare(
      `UPDATE users
          SET trial_ends_utc =
              strftime('%Y-%m-%dT%H:%M:%f', substr(created_utc, 1, 23), '+14 days') || '0000Z'
        WHERE trial_ends_utc IS NULL`,
    ).run();

    const healed = await get('/v1/auth/me', { token: account.accessToken });
    expect(healed.body.entitlement.state).toBe('trial');
    expect(healed.body.entitlement.can_sync_files).toBe(true);
    // Canonical, or it would compare wrong against every other timestamp here (src/time.ts).
    expect(healed.body.entitlement.until).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/);
    // From created_utc, not from now: an account old enough to have used its trial gets a lapsed
    // one rather than a windfall.
    const granted = Date.parse(healed.body.entitlement.until) - Date.now();
    expect(granted).toBeLessThanOrEqual(14 * 24 * 60 * 60 * 1000);
  });

  it('is granted once and not renewed by signing in again', async () => {
    const account = await signIn();
    const first = (await get('/v1/auth/me', { token: account.accessToken })).body.entitlement.until;

    const again = await get('/v1/auth/me', { token: account.accessToken });

    expect(again.body.entitlement.until).toBe(first);
  });
});

describe('the gate', () => {
  it('keeps text sync open once the trial is over: notes are free, only files are paid', async () => {
    const account = await signIn();
    await pushOne(account.accessToken);
    await expireEntitlement(account.userId);

    const push = await pushOne(account.accessToken, '00000000-0000-4000-8000-000000000002');
    const pull = await get('/v1/sync/pull?since=0', { token: account.accessToken });

    expect(push.status).toBe(200);
    expect(pull.status).toBe(200);
    expect(pull.body.changes).toHaveLength(2);

    // The paid flag is what the file endpoints will consult; it is off, and the app can say so.
    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.can_sync_files).toBe(false);
  });

  it('turns the paid flag back on the moment a subscription exists', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);
    expect((await get('/v1/auth/me', { token: account.accessToken })).body.entitlement.can_sync_files).toBe(false);

    await grantSubscription(account.userId);

    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.can_sync_files).toBe(true);
  });

  it('does not gate signing in, so a lapsed user can still see their account', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);

    const me = await get('/v1/auth/me', { token: account.accessToken });

    expect(me.status).toBe(200);
    expect(me.body.entitlement.state).toBe('expired');
    expect(me.body.entitlement.can_sync_files).toBe(false);
  });
});

describe('webhook', () => {
  it('refuses a correctly signed delivery from an address that is not Paddle', async () => {
    const account = await signIn();

    const result = await deliver(subscriptionEvent(account.userId), { ip: '192.0.2.10' });

    expect(result.status).toBe(401);
  });

  it('accepts any address inside a published range', async () => {
    const account = await signIn();

    const result = await deliver(subscriptionEvent(account.userId), { ip: '198.51.100.77' });

    expect(result.status).toBe(204);
  });

  it('falls back to the signature alone while the address list is unknown', async () => {
    (env as { PADDLE_IPS?: unknown }).PADDLE_IPS = async () => null;
    const account = await signIn();

    expect((await deliver(subscriptionEvent(account.userId), { ip: '192.0.2.10' })).status).toBe(204);
    expect((await deliver(subscriptionEvent(account.userId), { ip: '192.0.2.10', signature: 'ts=1;h1=00' })).status).toBe(401);
  });

  it('grants entitlement and stitches the subscription to the account', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);

    expect((await deliver(subscriptionEvent(account.userId))).status).toBe(204);

    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.state).toBe('active');
    expect(me.body.entitlement.has_subscribed).toBe(true);
    expect((await pushOne(account.accessToken)).status).toBe(200);
  });

  it('applies a retried delivery exactly once', async () => {
    const account = await signIn();
    const event = subscriptionEvent(account.userId, { eventId: 'evt_repeat' });

    expect((await deliver(event)).status).toBe(204);
    expect((await deliver(event)).status).toBe(204);

    const events = await env.DB.prepare('SELECT COUNT(*) AS n FROM billing_events').first<{ n: number }>();
    expect(events?.n).toBe(1);
  });

  it('refuses an unsigned, mis-signed, or stale delivery', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);
    const event = subscriptionEvent(account.userId);
    const body = JSON.stringify(event);

    const forged = `ts=${Math.floor(Date.now() / 1000)};h1=${'0'.repeat(64)}`;
    expect((await deliver(event, { signature: forged })).status).toBe(401);
    expect((await deliver(event, { signature: 'nonsense' })).status).toBe(401);
    // A captured request replayed the next day must not still work.
    const stale = await sign(body, Math.floor(Date.now() / 1000) - 3600);
    expect((await deliver(event, { signature: stale })).status).toBe(401);

    // None of them granted anything.
    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.can_sync_files).toBe(false);
  });

  it('keeps a cancelled subscription working until the period it paid for ends', async () => {
    const account = await signIn();
    const endsAt = new Date(Date.now() + 10 * 24 * 60 * 60 * 1000).toISOString();

    await deliver(subscriptionEvent(account.userId, { type: 'subscription.activated', endsAt }));
    await deliver(subscriptionEvent(account.userId, {
      type: 'subscription.canceled',
      status: 'canceled',
      endsAt,
    }));

    const me = await get('/v1/auth/me', { token: account.accessToken });
    // Store policy 10.8.6: a discontinued subscription still delivers what it sold.
    expect(me.body.entitlement.state).toBe('active');
    expect((await pushOne(account.accessToken)).status).toBe(200);
  });

  it('cuts off a cancelled subscription once that period has passed', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);

    await deliver(subscriptionEvent(account.userId, {
      type: 'subscription.canceled',
      status: 'canceled',
      endsAt: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
    }));

    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.state).toBe('expired');
  });

  it('keeps a failed payment syncing through the retry window', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);
    await deliver(subscriptionEvent(account.userId));

    await deliver({
      event_id: `evt_${crypto.randomUUID()}`,
      event_type: 'transaction.payment_failed',
      data: { subscription_id: 'sub_abc', customer_id: 'ctm_abc', origin: 'subscription_recurring' },
    });

    const me = await get('/v1/auth/me', { token: account.accessToken });
    // A declined card is not a cancellation on the day it happens.
    expect(me.body.entitlement.state).toBe('grace');
    expect(me.body.entitlement.can_sync_files).toBe(true);
  });

  it('never moves a paid-for period end backwards, however events are ordered', async () => {
    const account = await signIn();
    const far = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000).toISOString();
    const near = new Date(Date.now() + 5 * 24 * 60 * 60 * 1000).toISOString();

    await deliver(subscriptionEvent(account.userId, { endsAt: far }));
    await deliver(subscriptionEvent(account.userId, { type: 'subscription.updated', endsAt: near }));

    const row = await env.DB.prepare(
      'SELECT current_period_end_utc FROM subscriptions WHERE user_id = ?1',
    )
      .bind(account.userId)
      .first<{ current_period_end_utc: string }>();

    expect(row?.current_period_end_utc).toBe(far);
  });

  it('keeps the subscription id when a transaction event arrives, so deletion cancels the right one', async () => {
    // On transaction.* events data.id is the transaction. Reading it as the subscription id once
    // replaced sub_... with txn_..., and every later cancel went to a URL that 404s.
    const account = await signIn();
    await deliver(subscriptionEvent(account.userId, {
      type: 'subscription.created',
      subscriptionId: 'sub_real',
    }));
    await deliver({
      event_id: `evt_${crypto.randomUUID()}`,
      event_type: 'transaction.payment_failed',
      data: { id: 'txn_01failed', subscription_id: 'sub_real', customer_id: 'ctm_abc', status: 'past_due', origin: 'subscription_recurring' },
    });

    const row = await env.DB.prepare('SELECT subscription_id, status FROM subscriptions WHERE user_id = ?1')
      .bind(account.userId)
      .first<{ subscription_id: string; status: string }>();
    expect(row).toEqual({ subscription_id: 'sub_real', status: 'past_due' });

    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const calls = mockFetch(() => jsonResponse({ data: { id: 'sub_real', status: 'canceled' } }));
    try {
      const deleted = await del('/v1/account', { token: account.accessToken });

      expect(deleted.status).toBe(204);
      expect(calls.map((call) => `${call.method} ${call.url}`)).toEqual([
        'POST https://api.paddle.com/subscriptions/sub_real/cancel',
      ]);
    } finally {
      vi.restoreAllMocks();
      delete (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY;
    }
  });

  it('records an event it cannot match to an account instead of dropping it', async () => {
    const orphan = {
      event_id: 'evt_orphan',
      event_type: 'subscription.activated',
      data: { id: 'sub_unknown', status: 'active', customer_id: 'ctm_unknown' },
    };

    expect((await deliver(orphan)).status).toBe(204);

    const row = await env.DB.prepare('SELECT user_id FROM billing_events WHERE event_id = ?1')
      .bind('evt_orphan')
      .first<{ user_id: string | null }>();
    expect(row).not.toBeNull();
    expect(row?.user_id).toBeNull();
  });
});

describe('status', () => {
  it('reports which buttons make sense, and stores no links', async () => {
    const account = await signIn();

    const status = await get('/v1/billing/status', { token: account.accessToken });

    expect(status.status).toBe(200);
    expect(status.body.state).toBe('trial');
    // Neither link is a URL here: both are minted per click, so neither can go stale.
    expect(status.body.can_checkout).toBe(true);
    expect(status.body.can_manage).toBe(false);
    expect(status.body.checkout_url).toBeUndefined();
    expect(status.body.manage_url).toBeUndefined();
  });

  it('creates a checkout carrying the account, per click', async () => {
    const account = await signIn();

    const checkout = await post('/v1/billing/checkout', {}, { token: account.accessToken });

    expect(checkout.status).toBe(200);
    // The account id has to travel with the transaction: it is what the webhook matches on, and a
    // hosted-checkout link could not carry it.
    expect(checkout.body.url).toContain(account.userId);
    // No body means the annual plan: it is the one the pricing page leads with.
    expect(checkout.body.url).toContain('plan=annual');
  });

  it('lets the app pick the monthly plan', async () => {
    const account = await signIn();

    const checkout = await post('/v1/billing/checkout', { plan: 'monthly' }, { token: account.accessToken });

    expect(checkout.status).toBe(200);
    expect(checkout.body.url).toContain('plan=monthly');
  });

  it('rejects a plan that is not on the menu', async () => {
    const account = await signIn();

    const checkout = await post('/v1/billing/checkout', { plan: 'lifetime' }, { token: account.accessToken });

    expect(checkout.status).toBe(400);
    expect(checkout.body.error).toBe('bad_request');
  });

  it('lists only the plans that have a price', async () => {
    delete (env as { PADDLE_PRICE_ID_MONTHLY?: string }).PADDLE_PRICE_ID_MONTHLY;
    const account = await signIn();

    const status = await get('/v1/billing/status', { token: account.accessToken });

    expect(status.body.can_checkout).toBe(true);
    expect(status.body.plans).toEqual(['annual']);
  });

  it('does not offer a checkout when no price is configured', async () => {
    delete (env as { PADDLE_PRICE_ID_MONTHLY?: string }).PADDLE_PRICE_ID_MONTHLY;
    delete (env as { PADDLE_PRICE_ID_ANNUAL?: string }).PADDLE_PRICE_ID_ANNUAL;
    // wrangler.toml puts Premium on sale too, and the test environment reads it.
    delete (env as { PADDLE_PRICE_ID_PREMIUM_MONTHLY?: string }).PADDLE_PRICE_ID_PREMIUM_MONTHLY;
    delete (env as { PADDLE_PRICE_ID_PREMIUM_ANNUAL?: string }).PADDLE_PRICE_ID_PREMIUM_ANNUAL;
    const account = await signIn();

    const status = await get('/v1/billing/status', { token: account.accessToken });

    expect(status.body.can_checkout).toBe(false);
    expect(status.body.plans).toEqual([]);
  });

  it('needs an access token for the checkout too', async () => {
    expect((await post('/v1/billing/checkout', {})).status).toBe(401);
  });

  it('needs an access token', async () => {
    expect((await get('/v1/billing/status')).status).toBe(401);
  });
});

describe('tiers', () => {
  beforeEach(() => {
    (env as { PADDLE_PRICE_ID_PREMIUM_MONTHLY?: string }).PADDLE_PRICE_ID_PREMIUM_MONTHLY = 'pri_test_premium_monthly';
    (env as { PADDLE_PRICE_ID_PREMIUM_ANNUAL?: string }).PADDLE_PRICE_ID_PREMIUM_ANNUAL = 'pri_test_premium_annual';
    (env as { PADDLE_CHECKOUT_SESSION?: unknown }).PADDLE_CHECKOUT_SESSION =
      async (userId: string, _email: string, plan: string, tier: string) =>
        `https://pay.paddle.test/checkout?user=${userId}&tier=${tier}&plan=${plan}`;
  });

  function onPrice(
    userId: string,
    priceId: string,
    overrides: { occurredAt?: string; type?: string; endsAt?: string } = {},
  ) {
    const event = subscriptionEvent(userId, { type: overrides.type ?? 'subscription.updated', endsAt: overrides.endsAt });
    return {
      ...event,
      occurred_at: overrides.occurredAt ?? new Date().toISOString(),
      data: { ...event.data, items: [{ price: { id: priceId } }] },
    };
  }

  async function tierRow(userId: string) {
    return env.DB.prepare('SELECT tier, plan, price_id FROM subscriptions WHERE user_id = ?1')
      .bind(userId)
      .first<{ tier: string; plan: string | null; price_id: string | null }>();
  }

  it('maps each configured price to its tier and interval', async () => {
    const { offerOfPrice } = await import('../src/billing');

    expect(offerOfPrice(env, 'pri_test_monthly')).toEqual({ tier: 'pro', plan: 'monthly' });
    expect(offerOfPrice(env, 'pri_test_annual')).toEqual({ tier: 'pro', plan: 'annual' });
    expect(offerOfPrice(env, 'pri_test_premium_monthly')).toEqual({ tier: 'premium', plan: 'monthly' });
    expect(offerOfPrice(env, 'pri_test_premium_annual')).toEqual({ tier: 'premium', plan: 'annual' });
    expect(offerOfPrice(env, 'pri_someone_elses')).toBeNull();
  });

  it('records the tier a subscription is bought at', async () => {
    const account = await signIn();

    await deliver(onPrice(account.userId, 'pri_test_premium_annual', { type: 'subscription.activated' }));

    expect(await tierRow(account.userId)).toEqual({
      tier: 'premium', plan: 'annual', price_id: 'pri_test_premium_annual',
    });
    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement).toMatchObject({ state: 'active', tier: 'premium', plan: 'annual' });
  });

  it('reads a price it does not know as Pro, never as Premium', async () => {
    const account = await signIn();

    await deliver(onPrice(account.userId, 'pri_not_configured', { type: 'subscription.activated' }));

    expect(await tierRow(account.userId)).toEqual({ tier: 'pro', plan: null, price_id: 'pri_not_configured' });
  });

  it('follows an upgrade and a downgrade', async () => {
    const account = await signIn();
    await deliver(onPrice(account.userId, 'pri_test_monthly', { type: 'subscription.activated' }));

    await deliver(onPrice(account.userId, 'pri_test_premium_monthly'));
    expect((await tierRow(account.userId))?.tier).toBe('premium');

    await deliver(onPrice(account.userId, 'pri_test_annual'));
    expect(await tierRow(account.userId)).toMatchObject({ tier: 'pro', plan: 'annual' });
  });

  it('settles on the change that happened last, whichever arrives last', async () => {
    const account = await signIn();
    const far = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000).toISOString();
    const near = new Date(Date.now() + 20 * 24 * 60 * 60 * 1000).toISOString();

    // The downgrade happened second but is delivered first.
    await deliver(onPrice(account.userId, 'pri_test_monthly', { occurredAt: '2026-09-20T10:05:00.000000Z', endsAt: near }));
    await deliver(onPrice(account.userId, 'pri_test_premium_monthly', { occurredAt: '2026-09-20T10:00:00.000000Z', endsAt: far }));

    const row = await env.DB.prepare('SELECT tier, current_period_end_utc FROM subscriptions WHERE user_id = ?1')
      .bind(account.userId)
      .first<{ tier: string; current_period_end_utc: string }>();
    expect(row?.tier).toBe('pro');
    // The period end still only moves forward, whatever the tier did.
    expect(row?.current_period_end_utc).toBe(far);
  });

  it('leaves the tier alone on an event that names no price', async () => {
    const account = await signIn();
    await deliver(onPrice(account.userId, 'pri_test_premium_annual', { type: 'subscription.activated' }));

    await deliver({
      event_id: `evt_${crypto.randomUUID()}`,
      event_type: 'transaction.payment_failed',
      data: { id: 'txn_1', subscription_id: 'sub_abc', customer_id: 'ctm_abc', origin: 'subscription_recurring', items: [{ price: { id: 'pri_test_monthly' } }] },
    });
    await deliver(subscriptionEvent(account.userId, { type: 'subscription.past_due', status: 'past_due' }));

    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement).toMatchObject({ state: 'grace', tier: 'premium' });
  });

  it('reports no tier once nothing is in force, and Pro during the trial', async () => {
    const account = await signIn();
    expect((await get('/v1/auth/me', { token: account.accessToken })).body.entitlement.tier).toBe('pro');

    await expireEntitlement(account.userId);

    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.tier).toBeNull();
    expect(me.body.entitlement.can_sync_files).toBe(false);
  });

  it('lists every tier at every interval, with prices, and keeps the old list for old apps', async () => {
    const account = await signIn();

    const status = await get('/v1/billing/status', { token: account.accessToken });

    expect(status.body).toMatchObject({
      state: 'trial',
      tier: 'pro',
      plan: null,
      quota_bytes: 2 * 1024 * 1024 * 1024,
      used_bytes: 0,
      can_checkout: true,
      can_manage: false,
      can_change: false,
      plans: ['monthly', 'annual'],
    });
    expect(status.body.offers).toEqual([
      { tier: 'pro', plan: 'monthly', prices: [{ currency: 'KRW', amount: '2900' }, { currency: 'USD', amount: '249' }] },
      { tier: 'pro', plan: 'annual', prices: [{ currency: 'KRW', amount: '24000' }, { currency: 'USD', amount: '1999' }] },
      { tier: 'premium', plan: 'monthly', prices: [{ currency: 'KRW', amount: '5900' }, { currency: 'USD', amount: '499' }] },
      { tier: 'premium', plan: 'annual', prices: [{ currency: 'KRW', amount: '48000' }, { currency: 'USD', amount: '3999' }] },
    ]);
  });

  it('offers only the tiers that have prices', async () => {
    delete (env as { PADDLE_PRICE_ID_PREMIUM_MONTHLY?: string }).PADDLE_PRICE_ID_PREMIUM_MONTHLY;
    delete (env as { PADDLE_PRICE_ID_PREMIUM_ANNUAL?: string }).PADDLE_PRICE_ID_PREMIUM_ANNUAL;
    const account = await signIn();

    const status = await get('/v1/billing/status', { token: account.accessToken });

    expect(status.body.offers.map((offer: { tier: string }) => offer.tier)).toEqual(['pro', 'pro']);
  });

  it('sells Pro to an app that sends only a plan, or nothing', async () => {
    const account = await signIn();

    const bare = await post('/v1/billing/checkout', {}, { token: account.accessToken });
    const monthly = await post('/v1/billing/checkout', { plan: 'monthly' }, { token: account.accessToken });

    expect(bare.body.url).toContain('tier=pro&plan=annual');
    expect(monthly.body.url).toContain('tier=pro&plan=monthly');
  });

  it('sells Premium when asked for it', async () => {
    const account = await signIn();

    const checkout = await post('/v1/billing/checkout', { tier: 'premium', plan: 'monthly' }, { token: account.accessToken });

    expect(checkout.status).toBe(200);
    expect(checkout.body.url).toContain('tier=premium&plan=monthly');
  });

  it('rejects a tier that is not on the menu, or not on sale', async () => {
    const account = await signIn();
    expect((await post('/v1/billing/checkout', { tier: 'gold' }, { token: account.accessToken })).status).toBe(400);

    delete (env as { PADDLE_CHECKOUT_SESSION?: unknown }).PADDLE_CHECKOUT_SESSION;
    delete (env as { PADDLE_PRICE_ID_PREMIUM_ANNUAL?: string }).PADDLE_PRICE_ID_PREMIUM_ANNUAL;
    const refused = await post('/v1/billing/checkout', { tier: 'premium', plan: 'annual' }, { token: account.accessToken });
    expect(refused.status).toBe(400);
  });

  it('will not start a second subscription beside a running one', async () => {
    const account = await signIn();
    await deliver(onPrice(account.userId, 'pri_test_monthly', { type: 'subscription.activated' }));

    const checkout = await post('/v1/billing/checkout', { tier: 'premium', plan: 'annual' }, { token: account.accessToken });

    expect(checkout.status).toBe(409);
    expect(checkout.body.error).toBe('subscription_active');
    expect((await get('/v1/billing/status', { token: account.accessToken })).body.can_change).toBe(true);
  });

  it('upgrades the running subscription in place, and shows it at once', async () => {
    const account = await signIn();
    await deliver(onPrice(account.userId, 'pri_test_monthly', { type: 'subscription.activated' }));
    const seen: string[] = [];
    (env as { PADDLE_CHANGE_SUBSCRIPTION?: unknown }).PADDLE_CHANGE_SUBSCRIPTION =
      async (subscriptionId: string, priceId: string) => {
        seen.push(`${subscriptionId} ${priceId}`);
        return {
          id: subscriptionId,
          status: 'active',
          customer_id: 'ctm_abc',
          items: [{ price: { id: priceId } }],
          updated_at: new Date(Date.now() + 1000).toISOString(),
        };
      };

    try {
      const changed = await post('/v1/billing/change', { tier: 'premium', plan: 'monthly' }, { token: account.accessToken });

      expect(changed.status).toBe(200);
      expect(seen).toEqual(['sub_abc pri_test_premium_monthly']);
      expect(changed.body).toMatchObject({ state: 'active', tier: 'premium', plan: 'monthly', quota_bytes: 200 * 1024 ** 3 });
    } finally {
      delete (env as { PADDLE_CHANGE_SUBSCRIPTION?: unknown }).PADDLE_CHANGE_SUBSCRIPTION;
    }
  });

  it('sends the change to Paddle as a prorated subscription update', async () => {
    const account = await signIn();
    await deliver(onPrice(account.userId, 'pri_test_premium_annual', { type: 'subscription.activated' }));
    (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY = 'pdl_test_key';
    const calls = mockFetch(() => jsonResponse({
      data: { id: 'sub_abc', status: 'active', items: [{ price: { id: 'pri_test_annual' } }], updated_at: new Date(Date.now() + 1000).toISOString() },
    }));

    try {
      const changed = await post('/v1/billing/change', { tier: 'pro', plan: 'annual' }, { token: account.accessToken });

      expect(changed.status).toBe(200);
      expect(changed.body.tier).toBe('pro');
      expect(calls).toHaveLength(1);
      expect(`${calls[0]!.method} ${calls[0]!.url}`).toBe('PATCH https://api.paddle.com/subscriptions/sub_abc');
      expect(JSON.parse(calls[0]!.body)).toEqual({
        items: [{ price_id: 'pri_test_annual', quantity: 1 }],
        proration_billing_mode: 'prorated_immediately',
        on_payment_failure: 'prevent_change',
      });
    } finally {
      vi.restoreAllMocks();
      delete (env as { PADDLE_API_KEY?: string }).PADDLE_API_KEY;
    }
  });

  it('has nothing to change without a running subscription', async () => {
    const account = await signIn();

    const changed = await post('/v1/billing/change', { tier: 'premium', plan: 'annual' }, { token: account.accessToken });

    expect(changed.status).toBe(404);
  });
});

describe('review fixes', () => {
  beforeEach(() => {
    (env as { PADDLE_CHECKOUT_SESSION?: unknown }).PADDLE_CHECKOUT_SESSION =
      async (userId: string, _email: string, plan: string, tier: string) =>
        `https://pay.paddle.test/checkout?user=${userId}&tier=${tier}&plan=${plan}`;
  });

  it('does not treat a card declined at checkout as a subscription, so the next checkout works', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);

    await deliver({
      event_id: `evt_${crypto.randomUUID()}`,
      event_type: 'transaction.payment_failed',
      data: { id: 'txn_declined', customer_id: 'ctm_new', origin: 'web', custom_data: { user_id: account.userId } },
    });

    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.state).toBe('expired');
    expect(me.body.entitlement.can_sync_files).toBe(false);
    const again = await post('/v1/billing/checkout', { plan: 'annual' }, { token: account.accessToken });
    expect(again.status).toBe(200);
  });

  it('does not let a row without a real subscription block a checkout', async () => {
    const account = await signIn();
    // What the old handling left behind for a declined checkout: past_due, and no sub_ id.
    await env.DB.prepare(
      `INSERT INTO subscriptions (user_id, customer_id, subscription_id, status, grace_ends_utc, updated_utc)
       VALUES (?1, 'ctm_x', NULL, 'past_due', '2030-01-01T00:00:00.0000000Z', '2026-09-30T00:00:00.0000000Z')`,
    ).bind(account.userId).run();

    expect((await post('/v1/billing/checkout', {}, { token: account.accessToken })).status).toBe(200);
  });

  it('leaves a paid subscription active when a proration charge is declined', async () => {
    const account = await signIn();
    await deliver(subscriptionEvent(account.userId, { type: 'subscription.activated', subscriptionId: 'sub_paid' }));

    await deliver({
      event_id: `evt_${crypto.randomUUID()}`,
      event_type: 'transaction.payment_failed',
      data: { id: 'txn_prorate', subscription_id: 'sub_paid', customer_id: 'ctm_abc', origin: 'subscription_update' },
    });

    const me = await get('/v1/auth/me', { token: account.accessToken });
    expect(me.body.entitlement.state).toBe('active');
  });

  it('keeps the live subscription when a second one is paid, and says so', async () => {
    const account = await signIn();
    await deliver(subscriptionEvent(account.userId, { type: 'subscription.activated', subscriptionId: 'sub_first' }));
    await deliver(subscriptionEvent(account.userId, { type: 'subscription.activated', subscriptionId: 'sub_second' }));

    const row = await env.DB.prepare('SELECT subscription_id, duplicate_subscription_id FROM subscriptions WHERE user_id = ?1')
      .bind(account.userId)
      .first<{ subscription_id: string; duplicate_subscription_id: string | null }>();
    expect(row).toEqual({ subscription_id: 'sub_first', duplicate_subscription_id: 'sub_second' });
    const status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body.duplicate_subscription).toBe(true);

    // Once the first is cancelled and over, the other is the one to follow.
    await deliver(subscriptionEvent(account.userId, {
      type: 'subscription.canceled', status: 'canceled', subscriptionId: 'sub_first',
      endsAt: new Date(Date.now() - 1000).toISOString(),
    }));
    await deliver(subscriptionEvent(account.userId, { type: 'subscription.updated', subscriptionId: 'sub_second' }));
    const after = await env.DB.prepare('SELECT subscription_id, status FROM subscriptions WHERE user_id = ?1')
      .bind(account.userId)
      .first<{ subscription_id: string; status: string }>();
    expect(after).toEqual({ subscription_id: 'sub_second', status: 'active' });
  });

  it('does not mark an event seen when applying it fails, so the retry applies it', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);
    const event = subscriptionEvent(account.userId, { eventId: 'evt_retry_me' });

    // A Worker deployed ahead of its migration: the table it writes to is not there.
    await env.DB.prepare('ALTER TABLE subscriptions RENAME TO subscriptions_away').run();
    try {
      expect((await deliver(event)).status).toBe(500);
    } finally {
      await env.DB.prepare('ALTER TABLE subscriptions_away RENAME TO subscriptions').run();
    }
    const recorded = await env.DB.prepare('SELECT COUNT(*) AS n FROM billing_events WHERE event_id = ?1')
      .bind('evt_retry_me').first<{ n: number }>();
    expect(recorded?.n).toBe(0);

    expect((await deliver(event)).status).toBe(204);
    expect((await get('/v1/auth/me', { token: account.accessToken })).body.entitlement.state).toBe('active');
  });
});

describe('beside an App Store subscription', () => {
  /** The row `/v1/billing/apple/transaction` writes (appleBilling.test.ts covers how). */
  async function appleRow(userId: string, endsInDays: number, status = 'active') {
    const ends = new Date(Date.now() + endsInDays * 24 * 60 * 60 * 1000).toISOString();
    await env.DB.prepare(
      `INSERT INTO subscriptions
         (user_id, provider, subscription_id, status, current_period_end_utc, updated_utc, tier, plan,
          price_id, price_occurred_utc, environment)
       VALUES (?1, 'apple', '2000000000000777', ?2, ?3, ?3, 'premium', 'monthly',
               'cc.arachat.daynote.premium.monthly', ?4, 'Production')`,
    ).bind(userId, status, ends, new Date(Date.now() + 60_000).toISOString()).run();
  }

  async function storedRow(userId: string) {
    return env.DB.prepare(
      'SELECT provider, subscription_id, status, tier, environment, duplicate_subscription_id FROM subscriptions WHERE user_id = ?1',
    ).bind(userId).first();
  }

  it('sets a Paddle subscription aside while the App Store one is live', async () => {
    const account = await signIn();
    await appleRow(account.userId, 20);

    await deliver(subscriptionEvent(account.userId, { subscriptionId: 'sub_desktop' }));

    expect(await storedRow(account.userId)).toMatchObject({
      provider: 'apple', subscription_id: '2000000000000777', duplicate_subscription_id: 'sub_desktop',
    });
    const status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body).toMatchObject({ provider: 'apple', duplicate_provider: 'paddle', can_manage: false });
  });

  it('ignores a dead Paddle subscription’s late event while the App Store one is live', async () => {
    const account = await signIn();
    await appleRow(account.userId, 20);

    await deliver(subscriptionEvent(account.userId, {
      type: 'subscription.canceled', status: 'canceled', subscriptionId: 'sub_old',
      endsAt: new Date(Date.now() - 1000).toISOString(),
    }));

    expect(await storedRow(account.userId)).toMatchObject({
      provider: 'apple', status: 'active', duplicate_subscription_id: null,
    });
  });

  it('takes the row back for Paddle once the App Store subscription has ended', async () => {
    const account = await signIn();
    await appleRow(account.userId, -1, 'expired');

    await deliver({
      ...subscriptionEvent(account.userId, { subscriptionId: 'sub_desktop' }),
      occurred_at: new Date().toISOString(),
      data: { ...subscriptionEvent(account.userId, { subscriptionId: 'sub_desktop' }).data, items: [{ price: { id: 'pri_test_monthly' } }] },
    });

    expect(await storedRow(account.userId)).toMatchObject({
      provider: 'paddle', subscription_id: 'sub_desktop', status: 'active', tier: 'pro', environment: null,
    });
    const status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body).toMatchObject({ provider: 'paddle', state: 'active', can_manage: true, apple_can_purchase: false });
  });
});
