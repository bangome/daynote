import { beforeEach, describe, expect, it } from 'vitest';
import {
  env,
  expireEntitlement,
  get,
  grantSubscription,
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
    expect(me.body.entitlement.can_sync).toBe(true);
    expect(me.body.entitlement.has_subscribed).toBe(false);
    // A date, because the app has to warn before the trial takes syncing away (policy 10.8.4).
    expect(Date.parse(me.body.entitlement.until)).toBeGreaterThan(Date.now());
    expect((await pushOne(account.accessToken)).status).toBe(200);
  });

  it('is granted once and not renewed by signing in again', async () => {
    const account = await signIn();
    const first = (await get('/v1/auth/me', { token: account.accessToken })).body.entitlement.until;

    const again = await get('/v1/auth/me', { token: account.accessToken });

    expect(again.body.entitlement.until).toBe(first);
  });
});

describe('the gate', () => {
  it('answers 402 once the trial is over, and keeps every note it already had', async () => {
    const account = await signIn();
    await pushOne(account.accessToken);
    await expireEntitlement(account.userId);

    const push = await pushOne(account.accessToken, '00000000-0000-4000-8000-000000000002');
    const pull = await get('/v1/sync/pull?since=0', { token: account.accessToken });

    // 402, not 403: the caller is who they say they are; what is missing is payment.
    expect(push.status).toBe(402);
    expect(push.body.error).toBe('subscription_required');
    expect(pull.status).toBe(402);

    // The whole promise of the lapse policy: nothing was deleted.
    const kept = await env.DB.prepare('SELECT COUNT(*) AS n FROM notes WHERE user_id = ?1')
      .bind(account.userId)
      .first<{ n: number }>();
    expect(kept?.n).toBe(1);
  });

  it('resumes the moment a subscription exists, from the same cursor', async () => {
    const account = await signIn();
    await pushOne(account.accessToken);
    await expireEntitlement(account.userId);
    expect((await get('/v1/sync/pull?since=0', { token: account.accessToken })).status).toBe(402);

    await grantSubscription(account.userId);

    const pull = await get('/v1/sync/pull?since=0', { token: account.accessToken });
    expect(pull.status).toBe(200);
    expect(pull.body.changes).toHaveLength(1);
  });

  it('does not gate signing in, so a lapsed user can still see their account', async () => {
    const account = await signIn();
    await expireEntitlement(account.userId);

    const me = await get('/v1/auth/me', { token: account.accessToken });

    expect(me.status).toBe(200);
    expect(me.body.entitlement.state).toBe('expired');
    expect(me.body.entitlement.can_sync).toBe(false);
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
    expect(me.body.entitlement.can_sync).toBe(false);
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
      data: { subscription_id: 'sub_abc', customer_id: 'ctm_abc' },
    });

    const me = await get('/v1/auth/me', { token: account.accessToken });
    // A declined card is not a cancellation on the day it happens.
    expect(me.body.entitlement.state).toBe('grace');
    expect(me.body.entitlement.can_sync).toBe(true);
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
