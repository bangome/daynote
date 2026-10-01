import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  decodeJwtPart,
  del,
  env,
  get,
  grantSubscription,
  jsonResponse,
  mockFetch,
  post,
  resetDatabase,
  signIn,
  type OutboundCall,
} from './helpers';
import { APPLE_OIDS, makeAppleChain, signJws, type TestChain } from './appleSigning';
import { verifyJws } from '../src/appStore';
import { APPLE_ROOT_CA_G3, decodeBase64, parseCertificate } from '../src/x509';

/**
 * App Store subscriptions (docs/CLOUD_SYNC.md §14.8).
 *
 * Apple is never contacted. The App Store Server API is a mocked `fetch`, and every payload it
 * returns — like every notification — is a JWS signed with a test chain shaped like Apple's, whose
 * root the Worker is told to trust. So what runs is the real verification: the chain, the marker
 * extensions, the signature, the bundle and product checks.
 */

const BUNDLE = 'cc.arachat.daynote';
const PRO_MONTHLY = 'cc.arachat.daynote.pro.monthly';
const PREMIUM_MONTHLY = 'cc.arachat.daynote.premium.monthly';
const DAY = 24 * 60 * 60 * 1000;

let chain: TestChain;

beforeAll(async () => {
  chain = await makeAppleChain();
});

async function iapKeyPem(): Promise<string> {
  const pair = (await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign'])) as CryptoKeyPair;
  const pkcs8 = new Uint8Array((await crypto.subtle.exportKey('pkcs8', pair.privateKey)) as ArrayBuffer);
  return `-----BEGIN PRIVATE KEY-----\n${btoa(String.fromCharCode(...pkcs8))}\n-----END PRIVATE KEY-----\n`;
}

beforeEach(async () => {
  await resetDatabase();
  const target = env as unknown as Record<string, unknown>;
  target.APPLE_IAP_KEY_ID = 'F47T589ZC4';
  target.APPLE_IAP_ISSUER_ID = '11111111-2222-3333-4444-555555555555';
  target.APPLE_IAP_PRIVATE_KEY = await iapKeyPem();
  target.APPLE_IAP_PRODUCTS = [
    PRO_MONTHLY, 'cc.arachat.daynote.pro.annual', PREMIUM_MONTHLY, 'cc.arachat.daynote.premium.annual',
  ].join(',');
  target.APPLE_APP_ID = '6817146422';
  target.APPLE_ROOT_CERTIFICATES = [chain.root.der];
  target.PADDLE_PRICE_ID_MONTHLY = 'pri_test_monthly';
  target.PADDLE_PRICE_ID_ANNUAL = 'pri_test_annual';
  target.PADDLE_CHECKOUT_SESSION = async (userId: string) => `https://pay.paddle.test/checkout?user=${userId}`;
});

afterEach(() => {
  vi.restoreAllMocks();
});

let serial = 0;

function transaction(userId: string | undefined, overrides: Record<string, unknown> = {}) {
  serial += 1;
  const id = `20000000${String(serial).padStart(8, '0')}`;
  return {
    transactionId: id,
    originalTransactionId: id,
    bundleId: BUNDLE,
    productId: PREMIUM_MONTHLY,
    type: 'Auto-Renewable Subscription',
    purchaseDate: Date.now() - DAY,
    expiresDate: Date.now() + 29 * DAY,
    ...(userId === undefined ? {} : { appAccountToken: userId }),
    environment: 'Production',
    signedDate: Date.now(),
    ...overrides,
  } as Record<string, unknown> & { transactionId: string; originalTransactionId: string };
}

interface AppleState {
  environment?: 'Production' | 'Sandbox';
  transaction: Record<string, unknown> & { transactionId: string; originalTransactionId: string };
  /** The subscription's newest transaction, when it is not the one posted. */
  latest?: Record<string, unknown>;
  status?: number;
  autoRenewStatus?: number;
  signingChain?: TestChain;
}

/** The App Store Server API, knowing one purchase in one environment. */
function appleApi(state: AppleState): OutboundCall[] {
  const environment = state.environment ?? 'Production';
  const signingChain = state.signingChain ?? chain;
  return mockFetch(async (call) => {
    const url = new URL(call.url);
    const inEnvironment = environment === 'Production'
      ? url.host === 'api.storekit.itunes.apple.com'
      : url.host === 'api.storekit-sandbox.itunes.apple.com';
    if (!inEnvironment) {
      return jsonResponse({ errorCode: 4040010, errorMessage: 'Transaction id not found.' }, 404);
    }
    if (url.pathname === `/inApps/v1/transactions/${state.transaction.transactionId}`) {
      return jsonResponse({ signedTransactionInfo: await signJws(signingChain, state.transaction) });
    }
    if (url.pathname === `/inApps/v1/subscriptions/${state.transaction.transactionId}`) {
      return jsonResponse({
        environment,
        bundleId: BUNDLE,
        data: [{
          subscriptionGroupIdentifier: '22432564',
          lastTransactions: [{
            originalTransactionId: state.transaction.originalTransactionId,
            status: state.status ?? 1,
            signedTransactionInfo: await signJws(signingChain, state.latest ?? state.transaction),
            signedRenewalInfo: await signJws(signingChain, {
              originalTransactionId: state.transaction.originalTransactionId,
              autoRenewProductId: state.transaction.productId,
              autoRenewStatus: state.autoRenewStatus ?? 1,
              signedDate: Date.now(),
            }),
          }],
        }],
      });
    }
    return jsonResponse({ errorCode: 4040010 }, 404);
  });
}

async function notify(payload: Record<string, unknown>, signingChain: TestChain = chain) {
  const worker = (await import('../src/index')).default;
  const request = new Request('https://daynote.test/v1/billing/apple/notifications', {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'cf-connecting-ip': '17.0.0.1' },
    body: JSON.stringify({ signedPayload: await signJws(signingChain, payload) }),
  });
  const ctx = { waitUntil: () => {}, passThroughOnException: () => {} } as unknown as ExecutionContext;
  const response = await worker.fetch(request, env as any, ctx);
  return { status: response.status };
}

async function notification(
  type: string,
  txn: Record<string, unknown>,
  options: { subtype?: string; status?: number; autoRenewStatus?: number; uuid?: string; signedDate?: number } = {},
) {
  return {
    notificationType: type,
    ...(options.subtype === undefined ? {} : { subtype: options.subtype }),
    notificationUUID: options.uuid ?? crypto.randomUUID(),
    version: '2.0',
    signedDate: options.signedDate ?? Date.now(),
    data: {
      appAppleId: 6817146422,
      bundleId: BUNDLE,
      environment: 'Production',
      ...(options.status === undefined ? {} : { status: options.status }),
      signedTransactionInfo: await signJws(chain, txn),
      signedRenewalInfo: await signJws(chain, {
        originalTransactionId: txn['originalTransactionId'],
        autoRenewProductId: txn['productId'],
        autoRenewStatus: options.autoRenewStatus ?? 1,
        signedDate: txn['signedDate'],
      }),
    },
  };
}

function row(userId: string) {
  return env.DB.prepare(
    `SELECT provider, subscription_id, status, current_period_end_utc, grace_ends_utc, tier, plan, price_id,
            environment, duplicate_subscription_id
       FROM subscriptions WHERE user_id = ?1`,
  ).bind(userId).first<Record<string, string | null>>();
}

describe('the signature check', () => {
  it('pins the real Apple Root CA - G3', () => {
    const root = parseCertificate(decodeBase64(APPLE_ROOT_CA_G3));
    expect(root.curve.name).toBe('P-384');
    expect(new Date(root.notAfter).toISOString()).toBe('2039-04-30T18:19:06.000Z');
  });

  it('accepts a payload signed by a chain to the trusted root', async () => {
    const payload = await verifyJws<{ hello: string }>(env, await signJws(chain, { hello: 'apple' }), new Date());
    expect(payload.hello).toBe('apple');
  });

  it('refuses a chain that ends at another root', async () => {
    const stranger = await makeAppleChain();
    await expect(verifyJws(env, await signJws(stranger, {}), new Date())).rejects.toMatchObject({ code: 'unauthorized' });
  });

  it('refuses the real pinned root when the chain is not Apple’s', async () => {
    delete (env as { APPLE_ROOT_CERTIFICATES?: unknown }).APPLE_ROOT_CERTIFICATES;
    await expect(verifyJws(env, await signJws(chain, {}), new Date())).rejects.toMatchObject({ code: 'unauthorized' });
  });

  it('refuses a chain whose intermediate the trusted root did not sign', async () => {
    const stranger = await makeAppleChain();
    const spliced = [stranger.leaf.der, stranger.intermediate.der, chain.root.der];
    await expect(verifyJws(env, await signJws(stranger, {}, spliced), new Date())).rejects.toMatchObject({ code: 'unauthorized' });
  });

  it('refuses a leaf without the App Store marker', async () => {
    const unmarked = await makeAppleChain({ leafExtensions: [APPLE_OIDS.appleIntermediate] });
    (env as { APPLE_ROOT_CERTIFICATES?: unknown }).APPLE_ROOT_CERTIFICATES = [unmarked.root.der];
    await expect(verifyJws(env, await signJws(unmarked, {}), new Date())).rejects.toMatchObject({ code: 'unauthorized' });
  });

  it('refuses an expired leaf', async () => {
    const expired = await makeAppleChain({ leafNotAfter: new Date(Date.now() - DAY) });
    (env as { APPLE_ROOT_CERTIFICATES?: unknown }).APPLE_ROOT_CERTIFICATES = [expired.root.der];
    await expect(verifyJws(env, await signJws(expired, {}), new Date())).rejects.toMatchObject({ code: 'unauthorized' });
  });

  it('refuses a payload altered after signing', async () => {
    const [header, , signature] = (await signJws(chain, { productId: PRO_MONTHLY })).split('.');
    const forged = btoa(JSON.stringify({ productId: PREMIUM_MONTHLY })).replace(/=+$/, '');
    await expect(verifyJws(env, `${header}.${forged}.${signature}`, new Date())).rejects.toMatchObject({ code: 'unauthorized' });
  });
});

describe('POST /v1/billing/apple/transaction', () => {
  it('records a Premium purchase, with Premium’s quota', async () => {
    const account = await signIn();
    const txn = transaction(account.userId);
    const calls = appleApi({ transaction: txn });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });

    expect(response.status).toBe(200);
    expect(response.body).toMatchObject({
      state: 'active',
      can_sync_files: true,
      tier: 'premium',
      plan: 'monthly',
      quota_bytes: 200 * 1024 * 1024 * 1024,
      provider: 'apple',
      apple_product_id: PREMIUM_MONTHLY,
      apple_can_purchase: true,
      can_manage: false,
      can_change: false,
    });
    expect(await row(account.userId)).toMatchObject({
      provider: 'apple',
      subscription_id: txn.originalTransactionId,
      status: 'active',
      environment: 'Production',
    });

    // Both calls went to production with an App Store Server API token for this app.
    expect(calls.map((call) => new URL(call.url).pathname)).toEqual([
      `/inApps/v1/transactions/${txn.transactionId}`,
      `/inApps/v1/subscriptions/${txn.transactionId}`,
    ]);
    const [header, claims] = calls[0]!.headers.get('authorization')!.slice('Bearer '.length).split('.');
    expect(decodeJwtPart(header!)).toMatchObject({ alg: 'ES256', kid: 'F47T589ZC4', typ: 'JWT' });
    expect(decodeJwtPart(claims!)).toMatchObject({
      iss: '11111111-2222-3333-4444-555555555555',
      aud: 'appstoreconnect-v1',
      bid: BUNDLE,
    });
  });

  it('falls back to the sandbox, where TestFlight buys, and says so', async () => {
    const account = await signIn();
    const txn = transaction(account.userId, { productId: PRO_MONTHLY, environment: 'Sandbox' });
    const calls = appleApi({ environment: 'Sandbox', transaction: txn });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });

    expect(response.status).toBe(200);
    expect(response.body).toMatchObject({ tier: 'pro', quota_bytes: 2 * 1024 * 1024 * 1024 });
    expect(calls.map((call) => new URL(call.url).host)).toEqual([
      'api.storekit.itunes.apple.com',
      'api.storekit-sandbox.itunes.apple.com',
      'api.storekit-sandbox.itunes.apple.com',
    ]);
    expect((await row(account.userId))?.environment).toBe('Sandbox');
  });

  it('refuses a purchase made for another account, and writes nothing', async () => {
    const owner = await signIn();
    const other = await signIn();
    const txn = transaction(owner.userId);
    appleApi({ transaction: txn });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: other.accessToken });

    expect(response.status).toBe(403);
    expect(response.body.error).toBe('forbidden');
    expect(await row(other.userId)).toBeNull();
  });

  it('lets a purchase without a token be claimed once, and only once', async () => {
    const first = await signIn();
    const second = await signIn();
    const txn = transaction(undefined);
    appleApi({ transaction: txn });

    expect((await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: first.accessToken })).status).toBe(200);
    const again = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: second.accessToken });
    expect(again.status).toBe(403);
  });

  it('honours a token whose account was deleted, for the account restoring it', async () => {
    const restorer = await signIn();
    const txn = transaction(crypto.randomUUID());
    appleApi({ transaction: txn });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: restorer.accessToken });
    expect(response.status).toBe(200);
    expect(response.body.provider).toBe('apple');
  });

  it('refuses a payload that is not signed by Apple', async () => {
    const account = await signIn();
    const txn = transaction(account.userId);
    appleApi({ transaction: txn, signingChain: await makeAppleChain() });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });
    expect(response.status).toBe(401);
    expect(await row(account.userId)).toBeNull();
  });

  it('refuses a product that is not one of ours', async () => {
    const account = await signIn();
    const txn = transaction(account.userId, { productId: 'cc.arachat.daynote.lifetime' });
    appleApi({ transaction: txn });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });
    expect(response.status).toBe(400);
  });

  it('records a cancelled-but-paid subscription as entitled until its end', async () => {
    const account = await signIn();
    const txn = transaction(account.userId);
    appleApi({ transaction: txn, autoRenewStatus: 0 });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });
    expect(response.body).toMatchObject({ state: 'active', can_sync_files: true });
    expect((await row(account.userId))?.status).toBe('canceled');
  });

  it('is refused with a message while the In-App Purchase key is missing', async () => {
    delete (env as { APPLE_IAP_PRIVATE_KEY?: string }).APPLE_IAP_PRIVATE_KEY;
    const account = await signIn();

    const response = await post('/v1/billing/apple/transaction', { transaction_id: '2000000000000001' }, { token: account.accessToken });
    // 503, so the app keeps the transaction and sends it again once the key is set.
    expect(response.status).toBe(503);
    expect(response.body.message).toMatch(/not configured/);
  });

  it('rejects a transaction id that is not one', async () => {
    const account = await signIn();
    const response = await post('/v1/billing/apple/transaction', { transaction_id: '../etc' }, { token: account.accessToken });
    expect(response.status).toBe(400);
  });
});

describe('one App Store subscription, one account', () => {
  it('follows the newest transaction’s account, and refuses the old one re-posted', async () => {
    const first = await signIn();
    const second = await signIn();
    const bought = transaction(first.userId, { productId: PRO_MONTHLY, signedDate: Date.now() - 2000 });
    appleApi({ transaction: bought });
    expect((await post('/v1/billing/apple/transaction', { transaction_id: bought.transactionId }, { token: first.accessToken })).status).toBe(200);
    vi.restoreAllMocks();

    // The same Apple ID changes product from the second account: the new transaction is its.
    const switched = {
      ...bought, transactionId: '3000000000000009', productId: PREMIUM_MONTHLY,
      appAccountToken: second.userId, signedDate: Date.now(),
    };
    appleApi({ transaction: switched });
    expect((await post('/v1/billing/apple/transaction', { transaction_id: switched.transactionId }, { token: second.accessToken })).status).toBe(200);
    vi.restoreAllMocks();

    expect(await row(second.userId)).toMatchObject({ provider: 'apple', status: 'active', tier: 'premium' });
    expect((await row(first.userId))?.status).toBe('transferred');

    // The first account posting its own old purchase again finds the subscription is no longer its.
    appleApi({ transaction: bought, latest: switched });
    const replay = await post('/v1/billing/apple/transaction', { transaction_id: bought.transactionId }, { token: first.accessToken });
    expect(replay.status).toBe(403);
    expect((await get('/v1/billing/status', { token: first.accessToken })).body.can_sync_files).toBe(false);
  });
});

describe('a subscription that will not renew, but is paid up', () => {
  it('still holds the account against Paddle and against a late event for another one', async () => {
    const account = await signIn();
    const txn = transaction(account.userId, { signedDate: Date.now() - 1000 });
    appleApi({ transaction: txn, autoRenewStatus: 0 });
    await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });
    vi.restoreAllMocks();

    const checkout = await post('/v1/billing/checkout', { tier: 'pro', plan: 'monthly' }, { token: account.accessToken });
    expect(checkout.status).toBe(409);

    // An older App Store subscription's expiry, delivered late, must not take the row.
    const older = transaction(account.userId, { expiresDate: Date.now() - DAY, signedDate: Date.now() });
    await notify(await notification('EXPIRED', older, { status: 2 }));
    expect(await row(account.userId)).toMatchObject({ subscription_id: txn.originalTransactionId, status: 'canceled' });
  });
});

describe('the upsert guard', () => {
  it('refuses a write over a live subscription that ownership() did not see', async () => {
    const { ownershipGuard } = await import('../src/billing');
    const account = await signIn();
    await env.DB.prepare(
      `INSERT INTO subscriptions (user_id, provider, subscription_id, status, current_period_end_utc, updated_utc, tier)
       VALUES (?1, 'apple', '2000000000000555', 'active', ?2, ?2, 'premium')`,
    ).bind(account.userId, new Date(Date.now() + 10 * DAY).toISOString()).run();

    const write = (provider: string, subscriptionId: string) => env.DB.prepare(
      `INSERT INTO subscriptions (user_id, provider, subscription_id, status, updated_utc)
       VALUES (?1, ?2, ?3, 'active', ?4)
       ON CONFLICT(user_id) DO UPDATE SET subscription_id = excluded.subscription_id, provider = excluded.provider
       ${ownershipGuard('?4')}`,
    ).bind(account.userId, provider, subscriptionId, new Date().toISOString()).run();

    expect((await write('paddle', 'sub_racer')).meta.changes).toBe(0);
    expect((await write('apple', '2000000000000555')).meta.changes).toBe(1);
  });
});

describe('one subscription per account, across stores', () => {
  it('sets an App Store purchase aside beside a live Paddle subscription', async () => {
    const account = await signIn();
    await grantSubscription(account.userId, 30, 'pro');
    const txn = transaction(account.userId);
    appleApi({ transaction: txn });

    const response = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });

    expect(response.status).toBe(200);
    expect(response.body).toMatchObject({
      provider: 'paddle',
      tier: 'pro',
      duplicate_subscription: true,
      duplicate_provider: 'apple',
      apple_can_purchase: false,
    });
    expect(await row(account.userId)).toMatchObject({ provider: 'paddle', subscription_id: 'sub_test' });
  });

  it('tells the iPhone not to sell while a Paddle subscription is live', async () => {
    const account = await signIn();
    await grantSubscription(account.userId);

    const response = await get('/v1/billing/status', { token: account.accessToken });
    expect(response.body).toMatchObject({ provider: 'paddle', apple_can_purchase: false });
    expect(response.body.apple_products).toHaveLength(4);
  });

  it('refuses a Paddle checkout while an App Store subscription is live', async () => {
    const account = await signIn();
    const txn = transaction(account.userId);
    appleApi({ transaction: txn });
    await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });

    const response = await post('/v1/billing/checkout', { tier: 'pro', plan: 'monthly' }, { token: account.accessToken });
    expect(response.status).toBe(409);
    expect(response.body.message).toMatch(/App Store/);
  });

  it('lets Paddle sell again once the App Store subscription has run out', async () => {
    const account = await signIn();
    const txn = transaction(account.userId, { expiresDate: Date.now() - DAY, purchaseDate: Date.now() - 31 * DAY });
    appleApi({ transaction: txn, status: 2 });
    const recorded = await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });
    expect(recorded.body).toMatchObject({ state: 'expired', provider: 'apple' });

    const response = await post('/v1/billing/checkout', { tier: 'pro', plan: 'monthly' }, { token: account.accessToken });
    expect(response.status).toBe(200);
  });

  it('offers nothing on the iPhone when the key or the product list is missing', async () => {
    (env as { APPLE_IAP_PRODUCTS?: string }).APPLE_IAP_PRODUCTS = '';
    const account = await signIn();

    const response = await get('/v1/billing/status', { token: account.accessToken });
    expect(response.body).toMatchObject({ apple_products: [], apple_can_purchase: false, provider: null });
  });

  it('deletes an account with an App Store subscription without calling Paddle', async () => {
    const account = await signIn();
    const txn = transaction(account.userId);
    appleApi({ transaction: txn });
    await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });

    vi.restoreAllMocks();
    const calls = mockFetch(() => jsonResponse({}, 500));
    const response = await del('/v1/account', { token: account.accessToken });

    expect(response.status).toBe(204);
    expect(calls).toHaveLength(0);
  });
});

describe('POST /v1/billing/apple/notifications', () => {
  it('applies SUBSCRIBED, and ignores the same notification delivered again', async () => {
    const account = await signIn();
    const txn = transaction(account.userId);
    const payload = await notification('SUBSCRIBED', txn, { subtype: 'INITIAL_BUY', status: 1 });

    expect((await notify(payload)).status).toBe(200);
    expect(await row(account.userId)).toMatchObject({ provider: 'apple', status: 'active', tier: 'premium' });

    await env.DB.prepare("UPDATE subscriptions SET status = 'probe' WHERE user_id = ?1").bind(account.userId).run();
    expect((await notify(payload)).status).toBe(200);
    expect((await row(account.userId))?.status).toBe('probe');

    const event = await env.DB.prepare('SELECT event_type, user_id FROM billing_events WHERE event_id = ?1')
      .bind(payload.notificationUUID).first();
    expect(event).toEqual({ event_type: 'apple.SUBSCRIBED.INITIAL_BUY', user_id: account.userId });
  });

  it('extends the period on DID_RENEW, and a stale event does not shorten it again', async () => {
    const account = await signIn();
    const first = transaction(account.userId, { signedDate: Date.now() - 2000 });
    await notify(await notification('SUBSCRIBED', first, { status: 1 }));

    const renewed = { ...first, transactionId: '3000000000000001', expiresDate: Date.now() + 59 * DAY, signedDate: Date.now() };
    await notify(await notification('DID_RENEW', renewed, { status: 1 }));
    const extended = (await row(account.userId))?.current_period_end_utc;
    expect(Date.parse(extended!)).toBeGreaterThan(Date.now() + 58 * DAY);

    // The first renewal's predecessor arrives late, signed before the renewal.
    const late = { ...first, signedDate: Date.now() - 1000, productId: PRO_MONTHLY };
    await notify(await notification('DID_CHANGE_RENEWAL_PREF', late, { status: 1 }));
    expect(await row(account.userId)).toMatchObject({ current_period_end_utc: extended, tier: 'premium' });
  });

  it('moves to Premium on an upgrade', async () => {
    const account = await signIn();
    const pro = transaction(account.userId, { productId: PRO_MONTHLY, signedDate: Date.now() - 1000 });
    await notify(await notification('SUBSCRIBED', pro, { status: 1 }));
    expect((await row(account.userId))?.tier).toBe('pro');

    const premium = { ...pro, transactionId: '3000000000000002', productId: PREMIUM_MONTHLY, signedDate: Date.now() };
    await notify(await notification('DID_CHANGE_RENEWAL_PREF', premium, { subtype: 'UPGRADE', status: 1 }));
    expect(await row(account.userId)).toMatchObject({ tier: 'premium', price_id: PREMIUM_MONTHLY });

    const status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body.quota_bytes).toBe(200 * 1024 * 1024 * 1024);
  });

  it('keeps file sync through a failed renewal, then stops it when the grace runs out', async () => {
    const account = await signIn();
    const lapsed = transaction(account.userId, { expiresDate: Date.now() - 60_000, signedDate: Date.now() - 2000 });
    await notify(await notification('DID_FAIL_TO_RENEW', lapsed, { signedDate: Date.now() - 2000 }));

    let status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body).toMatchObject({ state: 'grace', can_sync_files: true });

    await notify(await notification('GRACE_PERIOD_EXPIRED', { ...lapsed, signedDate: Date.now() }));
    status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body).toMatchObject({ state: 'expired', can_sync_files: false, has_subscribed: true });
  });

  it('stops file sync on a refund', async () => {
    const account = await signIn();
    const txn = transaction(account.userId, { signedDate: Date.now() - 1000 });
    await notify(await notification('SUBSCRIBED', txn, { status: 1 }));

    await notify(await notification('REFUND', { ...txn, revocationDate: Date.now(), signedDate: Date.now() }));
    const status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body).toMatchObject({ state: 'expired', can_sync_files: false });
    expect((await row(account.userId))?.status).toBe('revoked');
  });

  it('keeps access when auto-renew is turned off, until the period ends', async () => {
    const account = await signIn();
    const txn = transaction(account.userId, { signedDate: Date.now() - 1000 });
    await notify(await notification('SUBSCRIBED', txn, { status: 1 }));

    await notify(await notification('DID_CHANGE_RENEWAL_STATUS', { ...txn, signedDate: Date.now() }, {
      subtype: 'AUTO_RENEW_DISABLED',
      status: 1,
      autoRenewStatus: 0,
    }));
    const status = await get('/v1/billing/status', { token: account.accessToken });
    expect(status.body).toMatchObject({ state: 'active', can_sync_files: true });
    expect((await row(account.userId))?.status).toBe('canceled');
  });

  it('finds the account by the subscription when the purchase carried no token', async () => {
    const account = await signIn();
    const txn = transaction(undefined, { signedDate: Date.now() - 1000 });
    appleApi({ transaction: txn });
    await post('/v1/billing/apple/transaction', { transaction_id: txn.transactionId }, { token: account.accessToken });
    vi.restoreAllMocks();

    await notify(await notification('EXPIRED', { ...txn, expiresDate: Date.now() - 1000, signedDate: Date.now() }, {
      subtype: 'VOLUNTARY',
      status: 2,
    }));
    expect((await row(account.userId))?.status).toBe('expired');
  });

  it('records a notification for an account it cannot identify, and changes nothing', async () => {
    const payload = await notification('SUBSCRIBED', transaction(crypto.randomUUID()), { status: 1 });
    expect((await notify(payload)).status).toBe(200);

    const event = await env.DB.prepare('SELECT user_id FROM billing_events WHERE event_id = ?1')
      .bind(payload.notificationUUID).first();
    expect(event).toEqual({ user_id: null });
    expect((await env.DB.prepare('SELECT COUNT(*) AS n FROM subscriptions').first<{ n: number }>())?.n).toBe(0);
  });

  it('answers a TEST notification', async () => {
    const response = await notify({
      notificationType: 'TEST',
      notificationUUID: crypto.randomUUID(),
      signedDate: Date.now(),
      data: { bundleId: BUNDLE, environment: 'Sandbox' },
    });
    expect(response.status).toBe(200);
  });

  it('refuses a notification not signed by Apple, and records nothing', async () => {
    const account = await signIn();
    const payload = await notification('SUBSCRIBED', transaction(account.userId), { status: 1 });

    expect((await notify(payload, await makeAppleChain())).status).toBe(401);
    expect(await row(account.userId)).toBeNull();
    expect((await env.DB.prepare('SELECT COUNT(*) AS n FROM billing_events').first<{ n: number }>())?.n).toBe(0);
  });

  it('ignores a notification for another app', async () => {
    const account = await signIn();
    const payload = await notification('SUBSCRIBED', transaction(account.userId), { status: 1 });
    (payload.data as Record<string, unknown>).bundleId = 'com.example.other';

    expect((await notify(payload)).status).toBe(200);
    expect(await row(account.userId)).toBeNull();
  });
});
