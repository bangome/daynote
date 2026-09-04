import { sha256Hex, timingSafeEqual } from './bytes';
import { graceEnd, resolve, toWire } from './entitlement';
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
}

interface PaddleEvent {
  event_id?: string;
  event_type?: string;
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
async function resolveUser(env: Env, data: PaddleSubscriptionData): Promise<string | null> {
  const fromCustomData = data.custom_data?.user_id;
  if (typeof fromCustomData === 'string' && fromCustomData.length > 0) {
    const exists = await env.DB.prepare('SELECT id FROM users WHERE id = ?1')
      .bind(fromCustomData)
      .first<{ id: string }>();
    if (exists !== null) {
      return exists.id;
    }
  }

  const subscriptionId = data.id ?? data.subscription_id;
  if (typeof subscriptionId === 'string') {
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
  const userId = await resolveUser(env, data);

  // Idempotency first: a retried delivery must not be applied twice. INSERT OR IGNORE returns no
  // rows when the event has been seen, which is the whole check.
  const inserted = await env.DB.prepare(
    `INSERT OR IGNORE INTO billing_events (event_id, event_type, user_id, received_utc)
     VALUES (?1, ?2, ?3, ?4) RETURNING event_id`,
  )
    .bind(eventId, eventType, userId, canonicalUtc(now))
    .first<{ event_id: string }>();

  if (inserted === null) {
    return noContent();
  }

  if (userId === null || !HANDLED.has(eventType)) {
    // Recorded, not acted on. An event for an account we cannot identify is kept so it can be
    // reconciled by hand rather than vanishing.
    return noContent();
  }

  const status = eventType === 'transaction.payment_failed' ? 'past_due' : data.status ?? 'unknown';
  const periodEnd = data.current_billing_period?.ends_at ?? data.next_billed_at ?? null;
  const grace = status === 'past_due' ? graceEnd(now) : null;

  await env.DB.prepare(
    `INSERT INTO subscriptions
       (user_id, provider, customer_id, subscription_id, status, current_period_end_utc,
        grace_ends_utc, updated_utc)
     VALUES (?1, 'paddle', ?2, ?3, ?4, ?5, ?6, ?7)
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
       updated_utc = excluded.updated_utc`,
  )
    .bind(
      userId,
      data.customer_id ?? null,
      data.id ?? data.subscription_id ?? null,
      status,
      periodEnd,
      grace,
      canonicalUtc(now),
    )
    .run();

  return noContent();
}

/** The billing intervals on offer. The wire names are also the app's; keep them lower-case. */
export type Plan = 'monthly' | 'annual';

const PLANS: readonly Plan[] = ['monthly', 'annual'];

function priceIdFor(env: Env, plan: Plan): string | null {
  const id = plan === 'monthly' ? env.PADDLE_PRICE_ID_MONTHLY : env.PADDLE_PRICE_ID_ANNUAL;
  return typeof id === 'string' && id.length > 0 ? id : null;
}

/** The plans that actually have a price configured, in display order. */
function availablePlans(env: Env): Plan[] {
  return PLANS.filter((plan) => priceIdFor(env, plan) !== null);
}

/**
 * Reads `{ plan }` from the checkout request. An empty body means the annual plan: it is the one
 * the pricing page leads with, and the one an older app that sends no body should get.
 */
async function readPlan(request: Request, env: Env): Promise<Plan> {
  const raw = await request.text();
  let plan: unknown = 'annual';
  if (raw.trim().length > 0) {
    const body = await readJsonObject(new Request(request.url, { method: 'POST', body: raw, headers: request.headers }));
    plan = body.plan ?? 'annual';
  }
  if (typeof plan !== 'string' || !(PLANS as readonly string[]).includes(plan)) {
    throw new ApiError('bad_request', 'plan must be "monthly" or "annual".');
  }
  if (priceIdFor(env, plan as Plan) === null && env.PADDLE_CHECKOUT_SESSION === undefined) {
    throw new ApiError('bad_request', `The ${plan} plan is not on sale.`);
  }
  return plan as Plan;
}

/** What the app shows in its settings panel: the state, and where to go next. */
export async function status(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const entitlement = await resolve(env, user.id, now);
  const customerId = await findCustomer(env, user.id);

  return json({
    ...toWire(entitlement),
    // Neither link is a URL here. Both are minted per click — the checkout because it has to carry
    // this account's id, the portal because Paddle's links are single-use and expire. These two
    // flags only say which buttons make sense.
    can_checkout: availablePlans(env).length > 0,
    // Which intervals the app may offer buttons for. Prices themselves are not repeated here: the
    // checkout shows them in the buyer's currency, and the site states them for the listing.
    plans: availablePlans(env),
    can_manage: customerId !== null,
    server_utc: canonicalUtc(now),
  });
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
  const plan = await readPlan(request, env);

  if (env.PADDLE_CHECKOUT_SESSION !== undefined) {
    return json({
      url: await env.PADDLE_CHECKOUT_SESSION(user.id, user.email, plan),
      server_utc: canonicalUtc(now),
    });
  }

  const priceId = priceIdFor(env, plan);
  if (priceId === null) {
    throw new Error(`PADDLE_PRICE_ID_${plan.toUpperCase()} is not configured; see cloud/worker/DEPLOY.md §2b.`);
  }

  const apiKey = requireApiKey(env);
  const customerId = await findCustomer(env, user.id);

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
