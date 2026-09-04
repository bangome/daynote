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

export interface Entitlement {
  readonly state: EntitlementState;

  /** When this state runs out, if it does. */
  readonly until: string | null;

  /** True when sync is allowed right now. */
  readonly canSync: boolean;

  /** True once a subscription has ever existed, so the UI can say "renew" rather than "subscribe". */
  readonly hasSubscribed: boolean;
}

interface SubscriptionRow {
  status: string;
  current_period_end_utc: string | null;
  grace_ends_utc: string | null;
  customer_id: string | null;
  subscription_id: string | null;
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
    `SELECT status, current_period_end_utc, grace_ends_utc, customer_id, subscription_id
       FROM subscriptions WHERE user_id = ?1`,
  )
    .bind(userId)
    .first<SubscriptionRow>();

  if (row !== null) {
    if (ACTIVE.has(row.status) && isFuture(row.current_period_end_utc, now)) {
      return entitled('active', row.current_period_end_utc, true);
    }

    // Cancelled but paid up: access runs to the end of the period already bought. Required by Store
    // policy 10.8.6, and the right thing regardless.
    if (row.status === 'canceled' && isFuture(row.current_period_end_utc, now)) {
      return entitled('active', row.current_period_end_utc, true);
    }

    if (RETRYING.has(row.status) && isFuture(row.grace_ends_utc, now)) {
      return entitled('grace', row.grace_ends_utc, true);
    }

    // Anything else — expired, paused, refunded, or a status this version does not know — fails
    // closed. The row is kept as written, so a later webhook can correct it.
    return { state: 'expired', until: row.current_period_end_utc, canSync: false, hasSubscribed: true };
  }

  const trial = await env.DB.prepare('SELECT trial_ends_utc FROM users WHERE id = ?1')
    .bind(userId)
    .first<{ trial_ends_utc: string | null }>();

  if (isFuture(trial?.trial_ends_utc ?? null, now)) {
    return entitled('trial', trial!.trial_ends_utc, false);
  }

  return { state: 'expired', until: trial?.trial_ends_utc ?? null, canSync: false, hasSubscribed: false };
}

function entitled(
  state: EntitlementState,
  until: string | null,
  hasSubscribed: boolean,
): Entitlement {
  return { state, until, canSync: true, hasSubscribed };
}

/** The trial window granted to a brand-new account. */
export function trialEnd(now: Date): string {
  return canonicalUtc(new Date(now.getTime() + TRIAL_DAYS * 24 * 60 * 60 * 1000));
}

/** The shape the client reads. Deliberately small: state, a date, and what to offer next. */
export function toWire(entitlement: Entitlement): Record<string, unknown> {
  return {
    state: entitlement.state,
    until: entitlement.until,
    can_sync: entitlement.canSync,
    has_subscribed: entitlement.hasSubscribed,
  };
}

/** Records the grace window when a payment starts failing. */
export function graceEnd(now: Date): string {
  return canonicalUtc(new Date(now.getTime() + GRACE_DAYS * 24 * 60 * 60 * 1000));
}
