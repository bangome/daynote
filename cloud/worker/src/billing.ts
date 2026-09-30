import { sha256Hex, timingSafeEqual } from './bytes';
import {
  PLANS,
  TIERS,
  graceEnd,
  resolve,
  storage,
  toWire,
  type Plan,
  type Tier,
} from './entitlement';
import { ApiError, json, noContent, readJsonObject } from './http';
import { authenticate } from './auth';
import { canonicalUtc } from './time';
import { isFromPaddle } from './paddleIps';
import type { Env } from './env';

/**
 * Subscriptions, over Paddle (docs/CLOUD_SYNC.md §14).
 *
 * Paddle is the merchant of record, which is the whole reason it was chosen: it collects and remits
 * VAT and sales tax in every jurisdiction it sells into, which a solo publisher otherwise has to do
 * personally. The practical consequence for this file is that **no card data ever reaches Daynote**.
 * What arrives here is a signed webhook carrying a status and a date.
 *
 * Microsoft Store policy 10.8.1 and 10.8.6 (v7.19) permit a third-party purchase API for non-game
 * PC apps, which is what this is. The obligations that come with it are in docs/STORE.md, not here,
 * except the one that is code: the purchase starts inside the app and continues in the browser
 * (10.8.2), which is what `checkout` returns a URL for.
 */

/** Events that change entitlement. Anything else is recorded and ignored. */
const HANDLED = new Set([
  'subscription.created',
  'subscription.activated',
  'subscription.updated',
  'subscription.canceled',
  'subscription.paused',
  'subscription.resumed',
  'subscription.past_due',
  'transaction.payment_failed',
]);

interface PaddleSubscriptionData {
  id?: string;
  status?: string;
  customer_id?: string;
  current_billing_period?: { ends_at?: string };
  next_billed_at?: string;
  custom_data?: { user_id?: string } | null;
  subscription_id?: string;
  items?: { price?: { id?: string } | null }[];
  updated_at?: string;
  /** On a transaction: what raised it — `subscription_recurring` for a renewal, `web` for a checkout. */
  origin?: string;
}

interface PaddleEvent {
  event_id?: string;
  event_type?: string;
  occurred_at?: string;
  data?: PaddleSubscriptionData;
}

/**
 * Verifies the `Paddle-Signature` header: `ts=<unix>;h1=<hex>`, an HMAC-SHA256 over
 * `<ts>:<raw body>`.
 *
 * The raw body is used exactly as received — parsing it first and re-serialising would change a
 * byte somewhere and fail every signature. That is why this function takes the text, and why the
 * handler parses only after verifying.
 */
async function verify(env: Env, request: Request, rawBody: string, now: Date): Promise<void> {
  const secret = env.PADDLE_WEBHOOK_SECRET;
  if (typeof secret !== 'string' || secret.length === 0) {
    // Refuse rather than accept unsigned billing events: an unauthenticated endpoint that grants
    // entitlement is a way to get a free subscription.
    throw new Error('PADDLE_WEBHOOK_SECRET is not set. Run: wrangler secret put PADDLE_WEBHOOK_SECRET');
  }

  const header = request.headers.get('paddle-signature');
  if (header === null) {
    throw new ApiError('unauthorized', 'The webhook is not signed.');
  }

  let timestamp: string | null = null;
  let presented: string | null = null;
  for (const part of header.split(';')) {
    const [key, value] = part.split('=', 2);
    if (key === 'ts') {
      timestamp = value ?? null;
    } else if (key === 'h1') {
      presented = value ?? null;
    }
  }

  if (timestamp === null || presented === null) {
    throw new ApiError('unauthorized', 'The webhook signature is malformed.');
  }

  // A signature is only valid for a few minutes, so a captured request cannot be replayed later.
  const age = Math.abs(now.getTime() / 1000 - Number(timestamp));
  if (!Number.isFinite(age) || age > 5 * 60) {
    throw new ApiError('unauthorized', 'The webhook signature has expired.');
  }

  const key = await crypto.subtle.importKey(
    'raw',
    new TextEncoder().encode(secret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign'],
  );
  const digest = await crypto.subtle.sign(
    'HMAC',
    key,
    new TextEncoder().encode(`${timestamp}:${rawBody}`),
  );

  const computed = new Uint8Array(digest);
  const expected = new Uint8Array(
    (presented.match(/../g) ?? []).map((byte) => Number.parseInt(byte, 16)),
  );

  if (!timingSafeEqual(computed, expected)) {
    throw new ApiError('unauthorized', 'The webhook signature does not match.');
  }
}

/**
 * Finds the account an event belongs to.
 *
 * `custom_data.user_id` is set on the checkout, so it is present for a subscription created through
 * the app. Later events for the same subscription may not carry it, which is what the stored
 * provider ids are for.
 */
async function resolveUser(
  env: Env,
  data: PaddleSubscriptionData,
  subscriptionId: string | null,
): Promise<string | null> {
  const fromCustomData = data.custom_data?.user_id;
  if (typeof fromCustomData === 'string' && fromCustomData.length > 0) {
    const exists = await env.DB.prepare('SELECT id FROM users WHERE id = ?1')
      .bind(fromCustomData)
      .first<{ id: string }>();
    if (exists !== null) {
      return exists.id;
    }
  }

  if (subscriptionId !== null) {
    const row = await env.DB.prepare('SELECT user_id FROM subscriptions WHERE subscription_id = ?1')
      .bind(subscriptionId)
      .first<{ user_id: string }>();
    if (row !== null) {
      return row.user_id;
    }
  }

  if (typeof data.customer_id === 'string') {
    const row = await env.DB.prepare('SELECT user_id FROM subscriptions WHERE customer_id = ?1')
      .bind(data.customer_id)
      .first<{ user_id: string }>();
    if (row !== null) {
      return row.user_id;
    }
  }

  return null;
}

/**
 * The provider's webhook. Always answers 204 once the signature checks out, including for events it
 * does not act on: a 4xx would make Paddle retry something that will never succeed.
 */
export async function webhook(request: Request, env: Env, now: Date): Promise<Response> {
  // Address first, signature second. The address list comes from Paddle's own endpoint; see
  // src/paddleIps.ts for why an unavailable list falls back to the signature alone.
  if (!(await isFromPaddle(request, env, now))) {
    throw new ApiError('unauthorized', 'Webhooks are accepted from Paddle addresses only.');
  }

  const rawBody = await request.text();
  await verify(env, request, rawBody, now);

  let event: PaddleEvent;
  try {
    event = JSON.parse(rawBody) as PaddleEvent;
  } catch {
    throw new ApiError('bad_request', 'The webhook body is not JSON.');
  }

  const eventId = event.event_id ?? (await sha256Hex(rawBody));
  const eventType = event.event_type ?? 'unknown';
  const data = event.data ?? {};
  const subscriptionId = subscriptionIdOf(eventType, data);
  const userId = await resolveUser(env, data, subscriptionId);

  // Idempotency: a retried delivery must not be applied twice. A delivery already recorded is done.
  const seen = await env.DB.prepare('SELECT 1 AS seen FROM billing_events WHERE event_id = ?1')
    .bind(eventId)
    .first<{ seen: number }>();
  if (seen !== null) {
    return noContent();
  }

  const record = env.DB.prepare(
    `INSERT INTO billing_events (event_id, event_type, user_id, received_utc) VALUES (?1, ?2, ?3, ?4)`,
  ).bind(eventId, eventType, userId, canonicalUtc(now));

  // Recorded, not acted on: an event for an account we cannot identify is kept so it can be
  // reconciled by hand rather than vanishing. So is one that changes nothing (see `acts`).
  const change = userId !== null && acts(eventType, data, subscriptionId)
    ? await applyStatement(env, userId, eventType, data, subscriptionId, event.occurred_at, now)
    : null;

  // The record and the change commit together (a D1 batch is one transaction). If applying fails —
  // a schema the deploy got ahead of, say — the event is not marked seen, the 500 makes Paddle
  // retry, and the retry applies it. Recording first would have swallowed it for good.
  try {
    await env.DB.batch(change === null ? [record] : [record, change]);
  } catch (error) {
    // A concurrent delivery of the same event won the insert: that one applied it.
    if (error instanceof Error && /UNIQUE/i.test(error.message)) {
      return noContent();
    }
    throw error;
  }

  return noContent();
}

/**
 * Whether an event changes the account's row.
 *
 * A failed transaction counts only when it is a subscription's own renewal. A declined card on the
 * checkout page is a `transaction.payment_failed` too — it carries the account id, and no
 * subscription exists behind it — and treating it as `past_due` handed out a free grace period and
 * then, with the live-subscription guard on the checkout, blocked every later attempt to buy. A
 * declined proration charge from `/v1/billing/change` is also not a renewal: `prevent_change` leaves
 * the paid subscription exactly as it was, so marking it past due would have cut off a paid period.
 */
function acts(eventType: string, data: PaddleSubscriptionData, subscriptionId: string | null): boolean {
  if (!HANDLED.has(eventType)) {
    return false;
  }
  if (eventType === 'transaction.payment_failed') {
    return data.origin === 'subscription_recurring' && subscriptionId?.startsWith('sub_') === true;
  }
  return true;
}

/**
 * Writes what a subscription event says onto the account's row. Shared by the webhook and by
 * `change`, which gets the same subscription entity back from Paddle's PATCH.
 *
 * Status and period follow the rules §14.2 lists. The tier, interval and price follow the event
 * that *occurred* last (`occurred_at`), not the one delivered last: an upgrade and a quick
 * downgrade can arrive in either order, and the account must end up on the one chosen second.
 */
async function applyStatement(
  env: Env,
  userId: string,
  eventType: string,
  data: PaddleSubscriptionData,
  subscriptionId: string | null,
  occurredAt: string | undefined,
  now: Date,
): Promise<D1PreparedStatement> {
  // One account, one subscription. Two checkouts paid close together (a second tab, a reopened
  // page before the first webhook landed) make two Paddle subscriptions, and letting the second
  // overwrite the first would hide one that keeps billing. So an event for a different `sub_...`
  // than the live one on record leaves the row alone and is set aside for the operator: the id goes
  // to `duplicate_subscription_id`, the log says so, and status reports it. It is not cancelled
  // here — that subscription was paid for, and cancelling without a refund is not ours to decide.
  if (subscriptionId?.startsWith('sub_')) {
    const stored = await env.DB.prepare('SELECT subscription_id, status FROM subscriptions WHERE user_id = ?1')
      .bind(userId)
      .first<{ subscription_id: string | null; status: string }>();
    if (
      stored !== null
      && stored.subscription_id?.startsWith('sub_')
      && stored.subscription_id !== subscriptionId
      && LIVE.has(stored.status)
    ) {
      console.error('second paddle subscription for one account', userId, stored.subscription_id, subscriptionId);
      return env.DB.prepare(
        'UPDATE subscriptions SET duplicate_subscription_id = ?2, updated_utc = ?3 WHERE user_id = ?1',
      ).bind(userId, subscriptionId, canonicalUtc(now));
    }
  }

  const status = eventType === 'transaction.payment_failed' ? 'past_due' : data.status ?? 'unknown';
  const periodEnd = data.current_billing_period?.ends_at ?? data.next_billed_at ?? null;
  const grace = status === 'past_due' ? graceEnd(now) : null;

  // Only a subscription entity says which price it is on; a transaction's items are one bill.
  const price = eventType.startsWith('subscription.') ? priceOf(env, data) : null;
  const occurred = price === null ? null : canonicalUtc(parseOr(occurredAt, now));

  return env.DB.prepare(
    `INSERT INTO subscriptions
       (user_id, provider, customer_id, subscription_id, status, current_period_end_utc,
        grace_ends_utc, updated_utc, tier, plan, price_id, price_occurred_utc)
     VALUES (?1, 'paddle', ?2, ?3, ?4, ?5, ?6, ?7, COALESCE(?8, 'pro'), ?9, ?10, ?11)
     ON CONFLICT(user_id) DO UPDATE SET
       customer_id = COALESCE(excluded.customer_id, customer_id),
       subscription_id = COALESCE(excluded.subscription_id, subscription_id),
       status = excluded.status,
       -- Keep the furthest known period end: events can arrive out of order, and moving it
       -- backwards would cut off access the customer has already paid for.
       current_period_end_utc = CASE
           WHEN excluded.current_period_end_utc IS NULL THEN current_period_end_utc
           WHEN current_period_end_utc IS NULL THEN excluded.current_period_end_utc
           WHEN excluded.current_period_end_utc > current_period_end_utc
               THEN excluded.current_period_end_utc
           ELSE current_period_end_utc
       END,
       grace_ends_utc = excluded.grace_ends_utc,
       updated_utc = excluded.updated_utc,
       -- The price columns move together, and only for an event that named a price and did not
       -- happen before the one already recorded. SQLite reads every right-hand side from the row
       -- as it was, so the shared condition sees the old price_occurred_utc in all four.
       tier = CASE WHEN ?12 THEN excluded.tier ELSE tier END,
       plan = CASE WHEN ?12 THEN excluded.plan ELSE plan END,
       price_id = CASE WHEN ?12 THEN excluded.price_id ELSE price_id END,
       price_occurred_utc = CASE WHEN ?12 THEN excluded.price_occurred_utc ELSE price_occurred_utc END`
      // ?12 is spelled out rather than bound: it has to compare against the stored row.
      .replaceAll(
        '?12',
        `(excluded.price_occurred_utc IS NOT NULL AND
          (price_occurred_utc IS NULL OR excluded.price_occurred_utc >= price_occurred_utc))`,
      ),
  )
    .bind(
      userId,
      data.customer_id ?? null,
      subscriptionId,
      status,
      periodEnd,
      grace,
      canonicalUtc(now),
      price?.tier ?? null,
      price?.plan ?? null,
      price?.priceId ?? null,
      occurred,
    );
}

function parseOr(value: string | undefined, fallback: Date): Date {
  const parsed = typeof value === 'string' ? Date.parse(value) : Number.NaN;
  return Number.isFinite(parsed) ? new Date(parsed) : fallback;
}

/**
 * Which price a subscription entity is on, and so which tier. Null when the entity lists no items —
 * a status-only event must leave the recorded tier alone.
 *
 * A price this deployment does not know still counts as a price, and reads as Pro: it is the smaller
 * quota, so a misconfigured id can never hand out Premium's. The id is kept verbatim for
 * reconciliation, and the next event after the id is configured puts the tier right.
 */
function priceOf(
  env: Env,
  data: PaddleSubscriptionData,
): { priceId: string; tier: Tier; plan: Plan | null } | null {
  const ids = (data.items ?? [])
    .map((item) => item.price?.id)
    .filter((id): id is string => typeof id === 'string' && id.length > 0);
  if (ids.length === 0) {
    return null;
  }

  const known = ids
    .map((priceId) => ({ priceId, offer: offerOfPrice(env, priceId) }))
    .filter((entry) => entry.offer !== null)
    // One subscription has one price in practice; if it ever carries two, the larger tier wins.
    .sort((a, b) => TIERS.indexOf(b.offer!.tier) - TIERS.indexOf(a.offer!.tier));

  const best = known[0];
  return best === undefined
    ? { priceId: ids[0]!, tier: 'pro', plan: null }
    : { priceId: best.priceId, tier: best.offer!.tier, plan: best.offer!.plan };
}

/**
 * The subscription an event is about. `data.id` means the subscription only on `subscription.*`
 * events; on `transaction.*` it is the transaction (`txn_...`), and the subscription is
 * `data.subscription_id`. Reading `data.id` for both once let a failed payment overwrite the stored
 * `sub_...` with a `txn_...` (COALESCE keeps any non-null), after which nothing could cancel it.
 */
function subscriptionIdOf(eventType: string, data: PaddleSubscriptionData): string | null {
  const id = eventType.startsWith('subscription.') ? data.id ?? data.subscription_id : data.subscription_id;
  return typeof id === 'string' && id.length > 0 ? id : null;
}

export type { Plan, Tier } from './entitlement';

/** One thing the app can buy: a tier at an interval. */
export interface Offer {
  readonly tier: Tier;
  readonly plan: Plan;
}

/**
 * The four prices. Pro keeps the two variable names it has always had, so a deployment configured
 * before Premium existed goes on selling Pro without a change.
 */
function priceIdFor(env: Env, offer: Offer): string | null {
  const id = offer.tier === 'premium'
    ? offer.plan === 'monthly' ? env.PADDLE_PRICE_ID_PREMIUM_MONTHLY : env.PADDLE_PRICE_ID_PREMIUM_ANNUAL
    : offer.plan === 'monthly' ? env.PADDLE_PRICE_ID_MONTHLY : env.PADDLE_PRICE_ID_ANNUAL;
  return typeof id === 'string' && id.length > 0 ? id : null;
}

/** Every tier at every interval, in display order: Pro before Premium, monthly before annual. */
const ALL_OFFERS: readonly Offer[] = TIERS.flatMap((tier) => PLANS.map((plan) => ({ tier, plan })));

/** Which offer a Paddle price is, or null for a price this deployment does not sell. */
export function offerOfPrice(env: Env, priceId: string): Offer | null {
  return ALL_OFFERS.find((offer) => priceIdFor(env, offer) === priceId) ?? null;
}

/** One price, as Paddle writes it: an ISO 4217 code and the amount in the currency's minor unit. */
export interface Money {
  readonly currency: string;
  readonly amount: string;
}

/**
 * What each offer costs, for the app to display. The app shows prices from here rather than from
 * its own constants, so a price change is one edit and a redeploy, not an app release.
 *
 * These are the catalog prices, and they must match the Paddle prices the ids above point at: the
 * checkout charges what Paddle says, so this list only ever describes it. Minor units, as Paddle
 * does — KRW has none, so ₩2,900 is "2900"; USD has cents, so $2.49 is "249". The site's pricing
 * page and the Store listing state the same numbers.
 */
const PRICE_LIST: Readonly<Record<Tier, Readonly<Record<Plan, readonly Money[]>>>> = {
  pro: {
    monthly: [{ currency: 'KRW', amount: '2900' }, { currency: 'USD', amount: '249' }],
    annual: [{ currency: 'KRW', amount: '24000' }, { currency: 'USD', amount: '1999' }],
  },
  premium: {
    monthly: [{ currency: 'KRW', amount: '5900' }, { currency: 'USD', amount: '499' }],
    annual: [{ currency: 'KRW', amount: '48000' }, { currency: 'USD', amount: '3999' }],
  },
};

/** The offers that actually have a price configured, in display order. */
function availableOffers(env: Env): Offer[] {
  return ALL_OFFERS.filter((offer) => priceIdFor(env, offer) !== null);
}

/**
 * Reads `{ tier, plan }` from a checkout or change request.
 *
 * Both fields are optional, for the apps already installed: they send `{ plan }` alone, or no body
 * at all, and knew only one paid tier. So no tier means Pro, and no body means Pro annual — the one
 * the pricing page leads with, and what those apps have always been sold.
 */
async function readOffer(request: Request, env: Env): Promise<Offer> {
  const raw = await request.text();
  let tier: unknown = 'pro';
  let plan: unknown = 'annual';
  if (raw.trim().length > 0) {
    const body = await readJsonObject(new Request(request.url, { method: 'POST', body: raw, headers: request.headers }));
    tier = body.tier ?? 'pro';
    plan = body.plan ?? 'annual';
  }
  if (typeof tier !== 'string' || !(TIERS as readonly string[]).includes(tier)) {
    throw new ApiError('bad_request', 'tier must be "pro" or "premium".');
  }
  if (typeof plan !== 'string' || !(PLANS as readonly string[]).includes(plan)) {
    throw new ApiError('bad_request', 'plan must be "monthly" or "annual".');
  }
  const offer = { tier: tier as Tier, plan: plan as Plan };
  if (priceIdFor(env, offer) === null && env.PADDLE_CHECKOUT_SESSION === undefined) {
    throw new ApiError('bad_request', `The ${tier} ${plan} plan is not on sale.`);
  }
  return offer;
}

/**
 * Statuses of a subscription that is still billing. A second checkout for one of these would start
 * a second subscription and charge twice, so the checkout refuses and the app changes the plan.
 */
const LIVE = new Set(['active', 'trialing', 'past_due', 'paused']);

/**
 * Statuses Paddle lets a plan change be made on. A subscription being retried or paused has to be
 * put right in the portal first; Paddle refuses item changes on either.
 */
const CHANGEABLE = new Set(['active', 'trialing']);

interface OwnSubscription {
  customer_id: string | null;
  subscription_id: string | null;
  status: string;
  duplicate_subscription_id: string | null;
}

async function findSubscription(env: Env, userId: string): Promise<OwnSubscription | null> {
  return env.DB.prepare(
    'SELECT customer_id, subscription_id, status, duplicate_subscription_id FROM subscriptions WHERE user_id = ?1',
  )
    .bind(userId)
    .first<OwnSubscription>();
}

function canChange(row: OwnSubscription | null): row is OwnSubscription & { subscription_id: string } {
  return row !== null && CHANGEABLE.has(row.status) && row.subscription_id?.startsWith('sub_') === true;
}

/** What the app shows in its settings panel: the state, and where to go next. */
export async function status(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  return json(await statusBody(env, user.id, now));
}

async function statusBody(env: Env, userId: string, now: Date): Promise<Record<string, unknown>> {
  const entitlement = await resolve(env, userId, now);
  const subscription = await findSubscription(env, userId);
  const offers = availableOffers(env);

  return {
    ...toWire(entitlement, await storage(env, userId, entitlement)),
    // Neither link is a URL here. Both are minted per click — the checkout because it has to carry
    // this account's id, the portal because Paddle's links are single-use and expire. These flags
    // only say which buttons make sense.
    can_checkout: offers.length > 0,
    // Every tier at every interval that has a price, as `{ tier, plan, prices }`, in display order.
    // `prices` is the catalog list above; the checkout itself charges in the buyer's currency.
    offers: offers.map((offer) => ({ ...offer, prices: PRICE_LIST[offer.tier][offer.plan] })),
    // The Pro intervals alone, under the name and shape the apps installed before Premium read.
    // They know one paid tier and send `{ plan }`, which the checkout still sells as Pro.
    plans: offers.filter((offer) => offer.tier === 'pro').map((offer) => offer.plan),
    can_manage: subscription?.customer_id != null,
    // True when the running subscription can move to another offer through `/v1/billing/change`,
    // which is how an existing subscriber upgrades — never through a second checkout.
    can_change: canChange(subscription) && offers.length > 0,
    // A second subscription was paid for beside the live one (see `applyStatement`). The app says
    // so and points at the portal; the operator refunds it.
    duplicate_subscription: subscription?.duplicate_subscription_id != null,
    server_utc: canonicalUtc(now),
  };
}

/**
 * Creates a checkout for this account and returns the URL to open in the browser.
 *
 * Done server-side rather than by linking to a hosted checkout, because the transaction is where
 * `custom_data` can be set — and `custom_data.user_id` is what the webhook uses to tie the
 * subscription back to a Daynote account. A hosted-checkout link cannot carry it, which would leave
 * the account matched by email or not at all.
 */
export async function checkout(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const offer = await readOffer(request, env);

  // A second checkout would be a second subscription, billed alongside the first. Moving between
  // tiers or intervals is `change`; a subscription being retried or paused is fixed in the portal.
  const existing = await findSubscription(env, user.id);
  // Only a real subscription counts: a row without a `sub_...` id is no subscription to bill twice.
  if (existing !== null && LIVE.has(existing.status) && existing.subscription_id?.startsWith('sub_')) {
    throw new ApiError(
      'subscription_active',
      'This account already has a subscription. Change its plan, or manage it in the portal, instead.',
    );
  }

  if (env.PADDLE_CHECKOUT_SESSION !== undefined) {
    return json({
      url: await env.PADDLE_CHECKOUT_SESSION(user.id, user.email, offer.plan, offer.tier),
      server_utc: canonicalUtc(now),
    });
  }

  const priceId = priceIdFor(env, offer);
  if (priceId === null) {
    throw new Error(`The ${offer.tier} ${offer.plan} price is not configured; see cloud/worker/DEPLOY.md §2b.`);
  }

  const apiKey = requireApiKey(env);
  const customerId = existing?.customer_id ?? null;

  const response = await fetch('https://api.paddle.com/transactions', {
    method: 'POST',
    headers: { authorization: `Bearer ${apiKey}`, 'content-type': 'application/json' },
    body: JSON.stringify({
      items: [{ price_id: priceId, quantity: 1 }],
      // Reusing the customer keeps one Paddle customer per account instead of accumulating a new
      // one on every visit to the checkout.
      ...(customerId === null ? {} : { customer_id: customerId }),
      custom_data: { user_id: user.id },
      // null means "use the default payment link", which is set once in the Paddle dashboard.
      checkout: { url: null },
    }),
  });

  const body = (await response.json().catch(() => ({}))) as {
    data?: { checkout?: { url?: string } };
  };

  const checkoutUrl = body.data?.checkout?.url;
  if (!response.ok || typeof checkoutUrl !== 'string') {
    console.error('paddle checkout failed', response.status, JSON.stringify(body).slice(0, 300));
    throw new ApiError('server_error', 'The checkout could not be opened.');
  }

  // A returning customer's Paddle id rides along so the checkout page can hand it to Paddle.js as
  // `pwCustomer` (Retain). It is Paddle's own public identifier, not ours, and not the email.
  const url = new URL(checkoutUrl);
  if (customerId !== null) {
    url.searchParams.set('ctm', customerId);
  }

  return json({ url: url.toString(), server_utc: canonicalUtc(now) });
}

/**
 * Moves the running subscription to another tier or interval: Pro to Premium and back, monthly to
 * annual and back.
 *
 * Done here, through Paddle's subscription update (`PATCH /subscriptions/{id}` with new items),
 * rather than in the customer portal or through a second checkout. Paddle's portal lets a customer
 * cancel, change the card and find invoices, but not switch price; a second checkout would create a
 * second subscription and bill both. The update keeps the one subscription, the card and the
 * renewal date, and `prorated_immediately` settles the difference now: an upgrade is charged for the
 * rest of the period, a downgrade leaves a credit against the next bills. `prevent_change` means a
 * declined charge leaves the plan as it was rather than granting Premium unpaid.
 *
 * The PATCH answers with the updated subscription, which is applied at once through the same path as
 * a webhook, so the app shows the new tier on its next status call. The `subscription.updated`
 * webhook that follows says the same thing and is harmless.
 */
export async function change(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const offer = await readOffer(request, env);

  const row = await findSubscription(env, user.id);
  if (!canChange(row)) {
    throw new ApiError('not_found', 'There is no running subscription to change. Subscribe instead.');
  }

  const priceId = priceIdFor(env, offer);
  if (priceId === null) {
    throw new ApiError('bad_request', `The ${offer.tier} ${offer.plan} plan is not on sale.`);
  }

  const updated = env.PADDLE_CHANGE_SUBSCRIPTION !== undefined
    ? await env.PADDLE_CHANGE_SUBSCRIPTION(row.subscription_id, priceId)
    : await patchSubscription(requireApiKey(env), row.subscription_id, priceId);

  await (await applyStatement(env, user.id, 'subscription.updated', updated, row.subscription_id, updated.updated_at, now)).run();

  return json(await statusBody(env, user.id, now));
}

async function patchSubscription(
  apiKey: string,
  subscriptionId: string,
  priceId: string,
): Promise<PaddleSubscriptionData> {
  const response = await fetch(`https://api.paddle.com/subscriptions/${encodeURIComponent(subscriptionId)}`, {
    method: 'PATCH',
    headers: { authorization: `Bearer ${apiKey}`, 'content-type': 'application/json' },
    body: JSON.stringify({
      items: [{ price_id: priceId, quantity: 1 }],
      proration_billing_mode: 'prorated_immediately',
      on_payment_failure: 'prevent_change',
    }),
  });

  const body = (await response.json().catch(() => ({}))) as { data?: PaddleSubscriptionData };
  if (!response.ok || body.data === undefined) {
    console.error('paddle subscription change failed', response.status, JSON.stringify(body).slice(0, 300));
    throw new ApiError(
      'server_error',
      'The plan could not be changed, and nothing was charged. Try again, or use Manage subscription.',
    );
  }
  return body.data;
}

/**
 * Mints a customer-portal link.
 *
 * Cancelling, changing a card, and finding an invoice all happen at the provider — building our own
 * version would mean handling card data, which is the whole reason for a merchant of record. The
 * link is created per request and returned once: Paddle's portal URLs are single-use and
 * short-lived, so caching one would hand the user an expired page.
 */
export async function portal(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const customerId = await findCustomer(env, user.id);
  if (customerId === null) {
    throw new ApiError('not_found', 'There is no subscription to manage yet.');
  }

  if (env.PADDLE_PORTAL_SESSION !== undefined) {
    return json({ url: await env.PADDLE_PORTAL_SESSION(customerId), server_utc: canonicalUtc(now) });
  }

  const apiKey = requireApiKey(env);

  const response = await fetch(
    `https://api.paddle.com/customers/${encodeURIComponent(customerId)}/portal-sessions`,
    {
      method: 'POST',
      headers: { authorization: `Bearer ${apiKey}`, 'content-type': 'application/json' },
      body: JSON.stringify({}),
    },
  );

  const body = (await response.json().catch(() => ({}))) as {
    data?: { urls?: { general?: { overview?: string } } };
  };

  const url = body.data?.urls?.general?.overview;
  if (!response.ok || typeof url !== 'string') {
    console.error('paddle portal session failed', response.status, JSON.stringify(body).slice(0, 300));
    throw new ApiError('server_error', 'The subscription portal could not be opened.');
  }

  return json({ url, server_utc: canonicalUtc(now) });
}

/**
 * Makes sure an account about to be deleted can never be charged again (src/account.ts).
 *
 * Anything short of `canceled` is treated as able to bill — `active` and `trialing` obviously, but
 * also `past_due` (Paddle is still retrying the card), `paused` (resumable from the portal), and a
 * status this version does not know. Failing towards "cancel it" is the only safe direction: an
 * account that is gone but still renewing is a charge the customer can no longer even see.
 *
 * The cancellation is immediate rather than at the period end. The service the period paid for is
 * being deleted at the customer's request, so there is nothing left to deliver until then.
 *
 * Without an API key this server cannot cancel anything, so it refuses the deletion with
 * `subscription_active` and the customer cancels first, from the portal — as it does when it cannot
 * tell which subscription to cancel. Nothing is deleted in either case: the refusal comes before
 * every destructive step.
 */
export async function cancelForDeletion(env: Env, userId: string, now: Date): Promise<void> {
  const row = await env.DB.prepare(
    'SELECT customer_id, subscription_id, status FROM subscriptions WHERE user_id = ?1',
  )
    .bind(userId)
    .first<{ customer_id: string | null; subscription_id: string | null; status: string }>();

  if (row === null || row.status === 'canceled') {
    return;
  }

  const apiKey = env.PADDLE_API_KEY;
  if (typeof apiKey !== 'string' || apiKey.length === 0) {
    throw cancelFirst();
  }

  // A row written before the webhook fix may hold a transaction id (`txn_...`) where the
  // subscription id belongs, and cancelling that would 404 on every retry. Anything that is not a
  // `sub_...` is looked up again from the customer instead of trusted.
  const subscriptionIds = row.subscription_id?.startsWith('sub_')
    ? [row.subscription_id]
    : await billableSubscriptionsOf(apiKey, row.customer_id);
  if (subscriptionIds === null) {
    throw cancelFirst();
  }

  for (const id of subscriptionIds) {
    const subscriptionId = encodeURIComponent(id);
    const response = await fetch(`https://api.paddle.com/subscriptions/${subscriptionId}/cancel`, {
      method: 'POST',
      headers: { authorization: `Bearer ${apiKey}`, 'content-type': 'application/json' },
      body: JSON.stringify({ effective_from: 'immediately' }),
    });

    // Paddle refuses to cancel what is already cancelled — a webhook we missed, or a retry after
    // the first attempt cancelled and then something else failed. Asking what state it is in
    // settles both without depending on the wording of Paddle's error codes.
    if (!response.ok && !(await isCanceledAtPaddle(apiKey, subscriptionId))) {
      console.error('paddle cancel failed', response.status, (await response.text()).slice(0, 300));
      throw new ApiError(
        'server_error',
        'The subscription could not be cancelled, so nothing was deleted. Try again in a few minutes.',
      );
    }
  }

  // Recorded before anything is deleted, so a retry after a later failure does not cancel twice.
  await env.DB.prepare(
    "UPDATE subscriptions SET status = 'canceled', updated_utc = ?2 WHERE user_id = ?1",
  )
    .bind(userId, canonicalUtc(now))
    .run();
}

/** The refusal when this server cannot be sure the subscription will stop. */
function cancelFirst(): ApiError {
  return new ApiError(
    'subscription_active',
    'Cancel your subscription before deleting the account, or it would keep renewing. ' +
      'Use Manage subscription in the app, or the link in your Paddle receipt, then try again.',
  );
}

/**
 * Every subscription of this customer that could still bill, from Paddle itself. Null when that
 * cannot be established — no customer id, or Paddle would not answer — which the caller turns into
 * a refusal: guessing "nothing to cancel" is the one wrong answer here.
 */
async function billableSubscriptionsOf(
  apiKey: string,
  customerId: string | null,
): Promise<string[] | null> {
  if (customerId === null) {
    return null;
  }

  const query = new URLSearchParams({
    customer_id: customerId,
    status: 'active,trialing,past_due,paused',
  });
  const response = await fetch(`https://api.paddle.com/subscriptions?${query}`, {
    headers: { authorization: `Bearer ${apiKey}` },
  });
  const body = (await response.json().catch(() => ({}))) as { data?: { id?: unknown }[] };
  if (!response.ok || !Array.isArray(body.data)) {
    console.error('paddle subscription lookup failed', response.status);
    return null;
  }

  const ids = body.data.map((subscription) => subscription.id);
  return ids.every((id): id is string => typeof id === 'string' && id.startsWith('sub_')) ? ids : null;
}

async function isCanceledAtPaddle(apiKey: string, subscriptionId: string): Promise<boolean> {
  const response = await fetch(`https://api.paddle.com/subscriptions/${subscriptionId}`, {
    headers: { authorization: `Bearer ${apiKey}` },
  });
  const body = (await response.json().catch(() => ({}))) as { data?: { status?: string } };
  return response.ok && body.data?.status === 'canceled';
}

function requireApiKey(env: Env): string {
  const apiKey = env.PADDLE_API_KEY;
  if (typeof apiKey !== 'string' || apiKey.length === 0) {
    throw new Error('PADDLE_API_KEY is not set. Run: wrangler secret put PADDLE_API_KEY');
  }
  return apiKey;
}

async function findCustomer(env: Env, userId: string): Promise<string | null> {
  const row = await env.DB.prepare('SELECT customer_id FROM subscriptions WHERE user_id = ?1')
    .bind(userId)
    .first<{ customer_id: string | null }>();
  return row?.customer_id ?? null;
}
