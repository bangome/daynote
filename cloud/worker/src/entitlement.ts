import { canonicalUtc } from './time';
import type { Env } from './env';

/**
 * Who is allowed to sync (docs/CLOUD_SYNC.md §14).
 *
 * Resolved from two rows and a clock, and nothing else: this module never looks at note content,
 * which is why the opt-in lock (§4.1b) is irrelevant to billing.
 *
 * The rule when the answer is "no" is set by the shipping decision, not by convenience: **the cloud
 * copy is kept**. Sync stops; nothing is deleted, ever, by lapsing. A user who resubscribes finds
 * their notes where they left them, and a user who never does still has every note on their own PC,
 * because the local database is the source of truth and works with no account at all.
 */

/** How long a new account may sync before it needs a subscription. */
export const TRIAL_DAYS = 14;

/**
 * How long access survives a failed payment. The provider retries over several days; treating the
 * first decline as an expiry would cut off a paying customer over an expired card.
 */
const GRACE_DAYS = 7;

export type EntitlementState = 'trial' | 'active' | 'grace' | 'expired';

/**
 * The two paid tiers. Both sync images and files; they differ only in how much may be stored.
 * The wire names are also the app's; keep them lower-case.
 */
export type Tier = 'pro' | 'premium';

/** The billing intervals on offer. The wire names are also the app's; keep them lower-case. */
export type Plan = 'monthly' | 'annual';

export const TIERS: readonly Tier[] = ['pro', 'premium'];

export const PLANS: readonly Plan[] = ['monthly', 'annual'];

const GIB = 1024 * 1024 * 1024;

/**
 * The ceiling behind Premium's "unlimited". Sold as unlimited because no ordinary use of a notes app
 * comes near it; enforced because R2 is billed by the byte and one account must not be able to run
 * up an unbounded bill (docs/CLOUD_SYNC.md §12). One constant, so raising it is a one-line change.
 */
export const FAIR_USE_BYTES = 200 * GIB;

/**
 * Attachment storage per tier. The trial is Pro-level, so it gets Pro's figure. Pro's 2 GiB is the
 * `users.quota_bytes` default every account was created with, so moving the quota here changed
 * nothing for an existing account.
 */
export const TIER_QUOTA_BYTES: Readonly<Record<Tier, number>> = {
  pro: 2 * GIB,
  premium: FAIR_USE_BYTES,
};

export interface Entitlement {
  readonly state: EntitlementState;

  /** When this state runs out, if it does. */
  readonly until: string | null;

  /**
   * True when the paid tier is in force right now. Text sync never consults this: notes, to-dos,
   * tags and favorites sync for every signed-in account. It gates image and file sync.
   */
  readonly canSyncFiles: boolean;

  /** True once a subscription has ever existed, so the UI can say "renew" rather than "subscribe". */
  readonly hasSubscribed: boolean;

  /**
   * The tier in force: `pro` during the trial, the subscription's tier while it is entitled, and
   * null once nothing is. Decides the storage quota, never whether text syncs.
   */
  readonly tier: Tier | null;

  /** The subscription's billing interval, when its price is one this deployment knows. */
  readonly plan: Plan | null;
}

interface SubscriptionRow {
  status: string;
  current_period_end_utc: string | null;
  grace_ends_utc: string | null;
  customer_id: string | null;
  subscription_id: string | null;
  tier: string;
  plan: string | null;
}

/** Statuses the provider uses for a subscription that is paying its way. */
const ACTIVE = new Set(['active', 'trialing']);

/** Statuses that mean "payment is being retried", not "gone". */
const RETRYING = new Set(['past_due']);

function isFuture(value: string | null, now: Date): boolean {
  return value !== null && Date.parse(value) > now.getTime();
}

export async function resolve(env: Env, userId: string, now: Date): Promise<Entitlement> {
  const row = await env.DB.prepare(
    `SELECT status, current_period_end_utc, grace_ends_utc, customer_id, subscription_id, tier, plan
       FROM subscriptions WHERE user_id = ?1`,
  )
    .bind(userId)
    .first<SubscriptionRow>();

  if (row !== null) {
    // A tier this version does not know reads as Pro: it is the smaller quota, and the only tier
    // there was before 0010, so it can never grant more than was sold.
    const tier: Tier = row.tier === 'premium' ? 'premium' : 'pro';
    const plan = (PLANS as readonly string[]).includes(row.plan ?? '') ? (row.plan as Plan) : null;

    if (ACTIVE.has(row.status) && isFuture(row.current_period_end_utc, now)) {
      return entitled('active', row.current_period_end_utc, true, tier, plan);
    }

    // Cancelled but paid up: access runs to the end of the period already bought. Required by Store
    // policy 10.8.6, and the right thing regardless.
    if (row.status === 'canceled' && isFuture(row.current_period_end_utc, now)) {
      return entitled('active', row.current_period_end_utc, true, tier, plan);
    }

    if (RETRYING.has(row.status) && isFuture(row.grace_ends_utc, now)) {
      return entitled('grace', row.grace_ends_utc, true, tier, plan);
    }

    // Anything else — expired, paused, refunded, or a status this version does not know — fails
    // closed. The row is kept as written, so a later webhook can correct it.
    return {
      state: 'expired',
      until: row.current_period_end_utc,
      canSyncFiles: false,
      hasSubscribed: true,
      tier: null,
      plan: null,
    };
  }

  const trial = await env.DB.prepare('SELECT trial_ends_utc FROM users WHERE id = ?1')
    .bind(userId)
    .first<{ trial_ends_utc: string | null }>();

  if (isFuture(trial?.trial_ends_utc ?? null, now)) {
    // The trial is Pro-level: file sync with Pro's quota.
    return entitled('trial', trial!.trial_ends_utc, false, 'pro', null);
  }

  return {
    state: 'expired',
    until: trial?.trial_ends_utc ?? null,
    canSyncFiles: false,
    hasSubscribed: false,
    tier: null,
    plan: null,
  };
}

function entitled(
  state: EntitlementState,
  until: string | null,
  hasSubscribed: boolean,
  tier: Tier,
  plan: Plan | null,
): Entitlement {
  return { state, until, canSyncFiles: true, hasSubscribed, tier, plan };
}

/** How much attachment storage an account has, and how much of it is in use. */
export interface Storage {
  readonly quotaBytes: number;
  readonly usedBytes: number;
}

/**
 * The effective quota is the tier's, raised by `users.quota_override_bytes` when an operator has
 * granted one account more. The override only ever raises: a grant made before Premium existed
 * (0010 carries every hand-set `quota_bytes` over) must not cap an account that later pays for
 * Premium below what Premium includes. Holding an account to less than its tier is not something
 * a column should do quietly; it would be a support conversation. An account with no tier in force
 * (expired) is shown Pro's figure: it cannot upload at all, and "used / 2GB" is the honest picture
 * of what resubscribing to Pro buys.
 *
 * Nothing here deletes. An account over its quota — after a downgrade from Premium, or a lapse —
 * keeps every stored byte and can still download all of it; `assets.put` just refuses anything that
 * adds bytes until enough is deleted to be back under.
 */
export async function storage(env: Env, userId: string, entitlement: Entitlement): Promise<Storage> {
  const used = await env.DB.prepare(
    'SELECT COALESCE(SUM(stored_bytes), 0) AS used FROM assets WHERE user_id = ?1',
  )
    .bind(userId)
    .first<{ used: number }>();
  const user = await env.DB.prepare('SELECT quota_override_bytes FROM users WHERE id = ?1')
    .bind(userId)
    .first<{ quota_override_bytes: number | null }>();

  return {
    quotaBytes: Math.max(user?.quota_override_bytes ?? 0, TIER_QUOTA_BYTES[entitlement.tier ?? 'pro']),
    usedBytes: used?.used ?? 0,
  };
}

/** The trial window granted to a brand-new account. */
export function trialEnd(now: Date): string {
  return canonicalUtc(new Date(now.getTime() + TRIAL_DAYS * 24 * 60 * 60 * 1000));
}

/**
 * The shape the client reads. Deliberately small: state, a date, the tier, and the storage.
 *
 * `tier`, `plan`, `quota_bytes` and `used_bytes` arrived with Premium. Clients that predate it
 * ignore them, and read the first four fields exactly as before.
 */
export function toWire(entitlement: Entitlement, used: Storage): Record<string, unknown> {
  return {
    state: entitlement.state,
    until: entitlement.until,
    // Text sync is free, so there is no `can_sync`; only the paid tier is reported.
    can_sync_files: entitlement.canSyncFiles,
    has_subscribed: entitlement.hasSubscribed,
    tier: entitlement.tier,
    plan: entitlement.plan,
    quota_bytes: used.quotaBytes,
    used_bytes: used.usedBytes,
  };
}

/** Resolves the entitlement and its storage together, in the wire shape. */
export async function describe(env: Env, userId: string, now: Date): Promise<Record<string, unknown>> {
  const entitlement = await resolve(env, userId, now);
  return toWire(entitlement, await storage(env, userId, entitlement));
}

/** Records the grace window when a payment starts failing. */
export function graceEnd(now: Date): string {
  return canonicalUtc(new Date(now.getTime() + GRACE_DAYS * 24 * 60 * 60 * 1000));
}
