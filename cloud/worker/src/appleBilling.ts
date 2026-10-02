import { authenticate } from './auth';
import { sha256Hex } from './bytes';
import { graceEnd, type Plan, type Tier } from './entitlement';
import { ApiError, json, readJsonObject } from './http';
import { markDuplicate, ownership, ownershipGuard, statusBody } from './billing';
import {
  APPLE_PRODUCTS,
  appStoreGet,
  appStoreGetWhenKnown,
  iapConfigured,
  verifyJws,
  type AppleNotification,
  type AppleRenewalInfo,
  type AppleTransaction,
  type AppStoreEnvironment,
} from './appStore';
import { APPLE_TRANSACTION_LIMITS, enforce } from './ratelimit';
import { canonicalUtc } from './time';
import type { Env } from './env';

/**
 * Subscriptions bought in the iPhone app through In-App Purchase (docs/CLOUD_SYNC.md §14.8).
 *
 * Two ways in, one way to write:
 *
 * - `POST /v1/billing/apple/transaction` — the app, after StoreKit reports a purchase, a renewal or
 *   a restore. It sends the transaction id and nothing else that is believed; the Worker asks the
 *   App Store Server API for the transaction and for the subscription's current status.
 * - `POST /v1/billing/apple/notifications` — App Store Server Notifications V2, signed by Apple.
 *
 * Both reduce what Apple says to one snapshot of the subscription and write it onto the account's
 * `subscriptions` row as provider 'apple', so entitlement, the tier's quota and the paywall are the
 * ones Paddle subscribers already have.
 *
 * The purchase is tied to the account by `appAccountToken`, which the app sets to the account id
 * when it starts the purchase. A transaction whose token is another existing account's is refused.
 */

/** Notification types that say something about entitlement. Anything else is recorded and ignored. */
const HANDLED = new Set([
  'SUBSCRIBED',
  'DID_RENEW',
  'DID_CHANGE_RENEWAL_PREF',
  'DID_CHANGE_RENEWAL_STATUS',
  'OFFER_REDEEMED',
  'EXPIRED',
  'GRACE_PERIOD_EXPIRED',
  'DID_FAIL_TO_RENEW',
  'REFUND',
  'REVOKE',
  'REFUND_REVERSED',
  'RENEWAL_EXTENDED',
]);

/** A notification is three JWS with their certificate chains: a few kilobytes, never more. */
const NOTIFICATION_BODY_LIMIT = 64 * 1024;

/** What the row is written from: one subscription, as Apple last described it. */
interface Snapshot {
  readonly originalTransactionId: string;
  readonly productId: string;
  readonly tier: Tier;
  readonly plan: Plan;
  readonly status: string;
  readonly periodEnd: string | null;
  readonly graceEnd: string | null;
  /** When Apple signed it: the order snapshots are applied in. */
  readonly signedUtc: string;
  readonly environment: string | null;
  /** Still billing — what decides a clash with another subscription (billing.ts, `ownership`). */
  readonly live: boolean;
}

function utc(epochMs: number | undefined): string | null {
  return typeof epochMs === 'number' && Number.isFinite(epochMs) ? canonicalUtc(new Date(epochMs)) : null;
}

/** The App Store status a notification implies when it does not carry one. */
function statusFromType(type: string | undefined, subtype: string | undefined): number | undefined {
  switch (type) {
    case 'DID_FAIL_TO_RENEW':
      return subtype === 'GRACE_PERIOD' ? 4 : 3;
    case 'EXPIRED':
    case 'GRACE_PERIOD_EXPIRED':
      return 2;
    case 'REFUND':
    case 'REVOKE':
      return 5;
    default:
      return undefined;
  }
}

/**
 * Reduces a transaction, its renewal info and Apple's status number to the row's vocabulary, which
 * is Paddle's: `active`, `canceled` (paid up, not renewing), `past_due` (payment being retried, with
 * a grace window), and anything else failing closed (`expired`, `revoked`).
 *
 * Billing retry gets the same grace a Paddle subscriber gets for a failed card: Apple's own grace
 * period when the app has it switched on, otherwise GRACE_DAYS from the end of the paid period —
 * counted from that date, not from when the news arrives, so a late delivery grants nothing extra.
 */
function snapshotOf(
  transaction: AppleTransaction,
  renewal: AppleRenewalInfo | null,
  appleStatus: number | undefined,
  environment: string | null,
  now: Date,
): Snapshot | null {
  const offer = transaction.productId === undefined ? undefined : APPLE_PRODUCTS[transaction.productId];
  const originalTransactionId = transaction.originalTransactionId;
  if (offer === undefined || typeof originalTransactionId !== 'string' || originalTransactionId.length === 0
    || transaction.inAppOwnershipType === 'FAMILY_SHARED') {
    return null;
  }

  const periodEnd = utc(transaction.expiresDate);
  const expired = periodEnd === null || Date.parse(periodEnd) <= now.getTime();
  // A revocation date on a transaction Apple still calls active (1) or retrying (3, 4) is a refund
  // of an earlier period, not of the subscription; the status decides.
  const revoked = transaction.revocationDate !== undefined && ![1, 3, 4].includes(appleStatus ?? -1);
  const code = revoked ? 5 : appleStatus ?? (expired ? 2 : 1);

  let status: string;
  let grace: string | null = null;
  switch (code) {
    case 1:
      status = renewal?.autoRenewStatus === 0 ? 'canceled' : 'active';
      break;
    case 3:
    case 4:
      status = 'past_due';
      grace = utc(renewal?.gracePeriodExpiresDate)
        ?? (transaction.expiresDate === undefined ? null : graceEnd(new Date(transaction.expiresDate)));
      break;
    case 5:
      status = 'revoked';
      break;
    default:
      status = 'expired';
  }

  // Paid up counts as live whether or not it renews: a subscriber who turned auto-renew off still
  // holds the account until the period ends (billing.ts, isLive).
  const live = ((status === 'active' || status === 'canceled') && !expired)
    || (status === 'past_due' && grace !== null && Date.parse(grace) > now.getTime());

  return {
    originalTransactionId,
    productId: transaction.productId!,
    tier: offer.tier,
    plan: offer.plan,
    status,
    periodEnd,
    graceEnd: grace,
    signedUtc: utc(transaction.signedDate) ?? canonicalUtc(now),
    environment: environment ?? transaction.environment ?? null,
    live,
  };
}

/**
 * The statement that writes a snapshot, or null when it must not be written (see `ownership`).
 *
 * Apple's payloads are whole descriptions of the subscription rather than deltas, so the newest one
 * wins outright: every column follows the snapshot with the latest `signedDate`, and one signed
 * earlier — a notification delivered late, a retry — changes nothing. That is the App Store form of
 * the Paddle rules (§14.2, §14.7): a stale event never shortens the period nor moves the tier back.
 * A newer snapshot may shorten the period, and must: an upgrade from annual Pro to monthly Premium
 * really does end sooner, and Apple refunds the rest.
 */
async function snapshotStatement(
  env: Env,
  userId: string,
  snapshot: Snapshot,
  now: Date,
): Promise<D1PreparedStatement | null> {
  const verdict = await ownership(
    env,
    userId,
    { provider: 'apple', subscriptionId: snapshot.originalTransactionId, live: snapshot.live },
    now,
  );
  if (verdict === 'ignore') {
    return null;
  }
  if (verdict === 'duplicate') {
    return markDuplicate(env, userId, snapshot.originalTransactionId, now);
  }

  const fresh = `(provider <> 'apple' OR subscription_id IS NOT excluded.subscription_id
      OR price_occurred_utc IS NULL OR excluded.price_occurred_utc >= price_occurred_utc)`;
  const follow = (column: string) => `${column} = CASE WHEN ${fresh} THEN excluded.${column} ELSE ${column} END`;

  return env.DB.prepare(
    `INSERT INTO subscriptions
       (user_id, provider, subscription_id, status, current_period_end_utc, grace_ends_utc,
        updated_utc, tier, plan, price_id, price_occurred_utc, environment)
     VALUES (?1, 'apple', ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11)
     ON CONFLICT(user_id) DO UPDATE SET
       -- A different subscription taking the row clears a duplicate flag that was about the old one.
       duplicate_subscription_id = CASE WHEN subscription_id IS NOT excluded.subscription_id
           THEN NULL ELSE duplicate_subscription_id END,
       ${['provider', 'subscription_id', 'status', 'current_period_end_utc', 'grace_ends_utc', 'tier', 'plan',
         'price_id', 'environment', 'price_occurred_utc'].map(follow).join(',\n       ')},
       updated_utc = excluded.updated_utc
     ${ownershipGuard('?12')}`,
  ).bind(
    userId,
    snapshot.originalTransactionId,
    snapshot.status,
    snapshot.periodEnd,
    snapshot.graceEnd,
    canonicalUtc(now),
    snapshot.tier,
    snapshot.plan,
    snapshot.productId,
    snapshot.signedUtc,
    snapshot.environment,
    canonicalUtc(now),
  );
}

/**
 * Writes a snapshot, and sets it aside as a duplicate when it lost a race with another
 * subscription: the upsert's guard refused it (`meta.changes === 0`) after `ownership` had read the
 * row as free.
 */
async function applySnapshot(
  env: Env,
  userId: string,
  snapshot: Snapshot,
  now: Date,
  record?: D1PreparedStatement,
): Promise<void> {
  const change = await snapshotStatement(env, userId, snapshot, now);
  const statements = [...(record === undefined ? [] : [record]), ...(change === null ? [] : [change])];
  if (statements.length === 0) {
    return;
  }
  const results = await env.DB.batch(statements);
  const changeResult = change === null ? undefined : results[record === undefined ? 0 : 1];
  if (changeResult !== undefined && changeResult.meta.changes === 0) {
    await markDuplicate(env, userId, snapshot.originalTransactionId, now).run();
  }
}

/** The account a token names, when it is an account that exists. */
async function accountOfToken(env: Env, token: string | undefined): Promise<string | null> {
  if (typeof token !== 'string' || token.length === 0) {
    return null;
  }
  const row = await env.DB.prepare('SELECT id FROM users WHERE id = ?1')
    .bind(token.toLowerCase())
    .first<{ id: string }>();
  return row?.id ?? null;
}

async function accountOfSubscription(env: Env, originalTransactionId: string): Promise<string | null> {
  const row = await env.DB.prepare(
    "SELECT user_id FROM subscriptions WHERE provider = 'apple' AND subscription_id = ?1",
  )
    .bind(originalTransactionId)
    .first<{ user_id: string }>();
  return row?.user_id ?? null;
}

const OTHER_ACCOUNT =
  'This App Store subscription belongs to a different Daynote account. Sign in with that account to use it.';

/**
 * Refuses a subscription that belongs to someone else. One App Store subscription (one
 * `originalTransactionId`) entitles one account; the partial unique index of migration 0012 holds
 * the rows to that, and this is the check that answers before the index would refuse.
 *
 * - Another account already holds it: refused, whatever the tokens say. Nothing moves silently; an
 *   Apple ID that changes product while signed in to a second account does not take the
 *   subscription from the first, and the first re-posting an old transaction cannot double it.
 * - The newest transaction's token names another existing account: refused. The newest decides,
 *   not the one posted, because StoreKit stamps each new transaction with the account signed in.
 * - The posted transaction's token names another existing account: refused.
 *
 * With no token at all (an offer code redeemed outside the app) or a deleted account's token, it is
 * free to claim by whoever restores it first; exploiting that needs the transaction id.
 */
async function requireOwnership(
  env: Env,
  userId: string,
  purchased: AppleTransaction,
  latest: AppleTransaction,
): Promise<void> {
  const holder = await accountOfSubscription(env, latest.originalTransactionId ?? '');
  if (holder !== null && holder !== userId) {
    throw new ApiError('forbidden', OTHER_ACCOUNT);
  }
  for (const transaction of [latest, purchased]) {
    const owner = await accountOfToken(env, transaction.appAccountToken);
    if (owner !== null && owner !== userId) {
      throw new ApiError('forbidden', OTHER_ACCOUNT);
    }
  }
}

/** A transaction of this app, for one of its subscriptions, or a refusal saying which it is not. */
function requireOurs(env: Env, transaction: AppleTransaction): void {
  if (transaction.bundleId !== env.APPLE_BUNDLE_ID?.trim()) {
    throw new ApiError('bad_request', 'That purchase was made in a different app.');
  }
  if (transaction.productId === undefined || APPLE_PRODUCTS[transaction.productId] === undefined
    || transaction.type !== 'Auto-Renewable Subscription') {
    throw new ApiError('bad_request', 'That purchase is not a Daynote subscription.');
  }
  // Family Sharing is off for the group. A shared transaction carries no account token, and each
  // family member could otherwise claim it for a different Daynote account.
  if (transaction.inAppOwnershipType === 'FAMILY_SHARED') {
    throw new ApiError('bad_request', 'A subscription shared through Family Sharing cannot be used here.');
  }
}

interface StatusItem {
  originalTransactionId?: string;
  status?: number;
  signedTransactionInfo?: string;
  signedRenewalInfo?: string;
}

/**
 * Where a subscription stands now, from Get All Subscription Statuses: its newest transaction, its
 * renewal info and Apple's status number, each verified. Null when Apple lists nothing for it.
 */
async function currentState(
  env: Env,
  transactionId: string,
  originalTransactionId: string | undefined,
  environment: AppStoreEnvironment,
  now: Date,
): Promise<{ latest: AppleTransaction; renewal: AppleRenewalInfo | null; status: number | undefined } | null> {
  const statuses = await appStoreGet(env, `/inApps/v1/subscriptions/${transactionId}`, now, environment);
  const groups = Array.isArray(statuses.body['data']) ? statuses.body['data'] as { lastTransactions?: StatusItem[] }[] : [];
  const item = groups
    .flatMap((group) => group.lastTransactions ?? [])
    .find((candidate) => candidate.originalTransactionId === originalTransactionId);
  if (item === undefined) {
    return null;
  }
  const latest = await verifyJws<AppleTransaction>(env, item.signedTransactionInfo, now);
  requireOurs(env, latest);
  const renewal = item.signedRenewalInfo === undefined
    ? null
    : await verifyJws<AppleRenewalInfo>(env, item.signedRenewalInfo, now);
  return { latest, renewal, status: item.status };
}

/**
 * `POST /v1/billing/apple/transaction` `{ transaction_id }`, after a purchase, renewal or restore.
 * Answers with the billing status, as `/v1/billing/change` does, so the app shows the result at
 * once.
 *
 * The app finishes the StoreKit transaction on a 200 — the purchase is recorded, or set aside as a
 * duplicate — and on a 403, where it belongs to another account and nothing here will ever change
 * that. Anything else leaves it unfinished, and StoreKit hands it back on the next launch.
 */
export async function transaction(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  await enforce(env, APPLE_TRANSACTION_LIMITS(user.id), now);

  const body = await readJsonObject(request);
  const transactionId = body['transaction_id'];
  if (typeof transactionId !== 'string' || !/^\d{1,32}$/.test(transactionId)) {
    throw new ApiError('bad_request', 'transaction_id must be the App Store transaction id.');
  }

  const found = await appStoreGetWhenKnown(env, `/inApps/v1/transactions/${transactionId}`, now);
  const purchased = await verifyJws<AppleTransaction>(env, found.body['signedTransactionInfo'], now);
  requireOurs(env, purchased);

  // The transaction is one moment; the subscription's status says where it stands now — renewed
  // since, upgraded, refunded, retrying a card. Asked in the environment the purchase was found in.
  const state = await currentState(env, transactionId, purchased.originalTransactionId, found.environment, now);
  const latest = state?.latest ?? purchased;

  await requireOwnership(env, user.id, purchased, latest);

  const snapshot = snapshotOf(latest, state?.renewal ?? null, state?.status, found.environment, now);
  if (snapshot !== null) {
    await applySnapshot(env, user.id, snapshot, now);
  }

  return json(await statusBody(env, user.id, now));
}

/**
 * `POST /v1/billing/apple/notifications`: App Store Server Notifications V2.
 *
 * The body is `{ signedPayload }`, a JWS whose chain must end at the pinned Apple root; the
 * transaction and renewal info inside are JWS of their own and are verified the same way. Anything
 * that does not verify is a 401 — a forged notification is how a free subscription would be had.
 *
 * Once verified, the answer is always 200, including for types this does not act on and for an
 * account that cannot be identified: Apple retries anything else for days, and those will never
 * succeed. Each notification is recorded by its `notificationUUID` in `billing_events`, in the same
 * D1 batch as its change, so a retry is a no-op and a failed write is retried rather than lost —
 * the Paddle webhook's rules.
 */
export async function notifications(request: Request, env: Env, now: Date): Promise<Response> {
  // The route is unauthenticated until the signature is checked, so an oversized body is refused
  // on its declared length before any of it is read.
  if (Number(request.headers.get('content-length') ?? '0') > NOTIFICATION_BODY_LIMIT) {
    throw new ApiError('payload_too_large', 'The request body is too large.');
  }
  const rawBody = await request.text();
  if (rawBody.length > NOTIFICATION_BODY_LIMIT) {
    throw new ApiError('payload_too_large', 'The request body is too large.');
  }
  const body = await readJsonObject(
    new Request(request.url, { method: 'POST', body: rawBody }),
    NOTIFICATION_BODY_LIMIT,
  );
  const payload = await verifyJws<AppleNotification>(env, body['signedPayload'], now);
  const data = payload.data ?? {};

  if (data.bundleId !== env.APPLE_BUNDLE_ID?.trim()) {
    console.error('apple notification for another app', data.bundleId);
    return json({ ok: true });
  }
  const appId = env.APPLE_APP_ID?.trim();
  if (data.environment === 'Production' && appId && data.appAppleId !== undefined && String(data.appAppleId) !== appId) {
    console.error('apple notification for another app id', data.appAppleId);
    return json({ ok: true });
  }

  const eventId = payload.notificationUUID ?? (await sha256Hex(rawBody));
  const eventType = `apple.${payload.notificationType ?? 'unknown'}${payload.subtype ? `.${payload.subtype}` : ''}`;

  const seen = await env.DB.prepare('SELECT 1 AS seen FROM billing_events WHERE event_id = ?1')
    .bind(eventId)
    .first<{ seen: number }>();
  if (seen !== null) {
    return json({ ok: true });
  }

  const transaction = data.signedTransactionInfo === undefined
    ? null
    : await verifyJws<AppleTransaction>(env, data.signedTransactionInfo, now);
  const renewal = data.signedRenewalInfo === undefined
    ? null
    : await verifyJws<AppleRenewalInfo>(env, data.signedRenewalInfo, now);

  const handled = HANDLED.has(payload.notificationType ?? '');
  const ours = transaction !== null && transaction.bundleId === env.APPLE_BUNDLE_ID?.trim();

  // A notification describes one transaction — perhaps an older one being refunded — so the row is
  // written from where the subscription stands now, read from Apple as the transaction endpoint
  // does. Without the In-App Purchase key (no API access) the notification's own payload is used.
  // An API failure is a 500, so Apple delivers it again later rather than it being applied wrong.
  const environment: AppStoreEnvironment = data.environment === 'Sandbox' ? 'Sandbox' : 'Production';
  const state = handled && ours && iapConfigured(env) && transaction!.transactionId !== undefined
    ? await currentState(env, transaction!.transactionId, transaction!.originalTransactionId, environment, now)
    : null;

  const snapshot = !ours
    ? null
    : state !== null
      ? snapshotOf(state.latest, state.renewal, state.status, environment, now)
      : snapshotOf(
        transaction!,
        renewal,
        data.status ?? statusFromType(payload.notificationType, payload.subtype),
        data.environment ?? null,
        now,
      );

  // The account already holding the subscription first: one subscription, one account, and a token
  // on a newer transaction does not move it (see requireOwnership). Then the token, for a
  // subscription no account holds yet.
  const userId = snapshot === null
    ? null
    : await accountOfSubscription(env, snapshot.originalTransactionId)
      ?? await accountOfToken(env, (state?.latest ?? transaction!).appAccountToken);

  const record = env.DB.prepare(
    'INSERT INTO billing_events (event_id, event_type, user_id, received_utc) VALUES (?1, ?2, ?3, ?4)',
  ).bind(eventId, eventType, userId, canonicalUtc(now));

  const acts = userId !== null && snapshot !== null && handled;

  try {
    if (acts) {
      await applySnapshot(env, userId!, snapshot!, now, record);
    } else {
      await record.run();
    }
  } catch (error) {
    // A concurrent delivery of the same notification won the insert: that one applied it.
    if (error instanceof Error && /UNIQUE/i.test(error.message) && /billing_events/i.test(error.message)) {
      return json({ ok: true });
    }
    throw error;
  }

  return json({ ok: true });
}
