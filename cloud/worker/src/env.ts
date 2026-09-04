import type { GoogleIdentity } from './google';

export interface Env {
  /** The D1 binding declared in wrangler.toml. */
  DB: D1Database;

  /**
   * The brand site's static files (cloud/site/public). Requests outside `run_worker_first` never
   * reach this code, so the binding exists only so a handler could hand a page back deliberately.
   */
  ASSETS?: Fetcher;

  /** HS256 signing secret for access tokens. Set with `wrangler secret put JWT_SECRET`. */
  JWT_SECRET: string;

  /**
   * Seals each account's data key before it is stored (src/dek.ts), so a D1 dump is not enough to
   * read notes. Set with `wrangler secret put DEK_WRAP_KEY`.
   *
   * Rotating this invalidates every stored key and is therefore a data-loss event, not a routine
   * operation: the sealed keys would have to be re-sealed under the new secret first.
   */
  DEK_WRAP_KEY: string;

  /**
   * The Google OAuth desktop client. The id is public and lives in wrangler.toml; the secret is a
   * Worker secret so it never ships inside the app, where it could be extracted from the binary.
   */
  GOOGLE_CLIENT_ID?: string;
  GOOGLE_CLIENT_SECRET?: string;

  /**
   * Paddle, the merchant of record for subscriptions (docs/CLOUD_SYNC.md §14). The webhook secret
   * signs incoming events; without it the webhook refuses every delivery rather than granting
   * entitlement on an unauthenticated request.
   */
  PADDLE_WEBHOOK_SECRET?: string;

  /**
   * The Paddle prices the subscription is sold at (`pri_...`), one per billing interval, from
   * Catalog → Products. The app chooses the plan; `/v1/billing/checkout` maps it to one of these.
   *
   * Price ids rather than hosted-checkout links because the checkout is created server-side: a
   * hosted-checkout URL cannot carry `custom_data`, and without that there is nothing tying the
   * resulting subscription back to a Daynote account.
   */
  PADDLE_PRICE_ID_MONTHLY?: string;
  PADDLE_PRICE_ID_ANNUAL?: string;

  /**
   * Paddle API key, used for exactly one thing: minting a customer-portal link.
   *
   * The portal is not a static address. Paddle issues a single-use, short-lived URL per customer
   * (`POST /customers/{id}/portal-sessions`), which must be generated on demand and never stored —
   * so there is no "manage URL" to configure, and this key is needed to produce one.
   */
  PADDLE_API_KEY?: string;

  /** Test seam: stands in for GET https://api.paddle.com/ips (src/paddleIps.ts). */
  PADDLE_IPS?: () => Promise<string[]>;

  /** Test seams: stand in for the two Paddle API calls, so billing can be exercised offline. */
  PADDLE_PORTAL_SESSION?: (customerId: string) => Promise<string>;
  PADDLE_CHECKOUT_SESSION?: (userId: string, email: string, plan: string) => Promise<string>;

  /** Optional overrides, provided as strings because Workers vars are always strings. */
  ACCESS_TOKEN_TTL_SECONDS?: string;
  REFRESH_TOKEN_TTL_DAYS?: string;

  /** Test seam: stands in for the call to Google, so sign-in can be exercised offline. */
  GOOGLE_EXCHANGE?: (
    code: string,
    codeVerifier: string,
    redirectUri: string,
  ) => Promise<GoogleIdentity>;
}

export const DEFAULT_ACCESS_TTL_SECONDS = 15 * 60;
export const DEFAULT_REFRESH_TTL_DAYS = 60;

/** A short secret would make forging an access token feasible; refuse to start rather than warn. */
const MIN_SECRET_LENGTH = 32;

export function accessTtlSeconds(env: Env): number {
  const parsed = Number(env.ACCESS_TOKEN_TTL_SECONDS);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : DEFAULT_ACCESS_TTL_SECONDS;
}

export function refreshTtlDays(env: Env): number {
  const parsed = Number(env.REFRESH_TOKEN_TTL_DAYS);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : DEFAULT_REFRESH_TTL_DAYS;
}

export function requireJwtSecret(env: Env): string {
  if (typeof env.JWT_SECRET !== 'string' || env.JWT_SECRET.length < MIN_SECRET_LENGTH) {
    throw new Error(
      `JWT_SECRET must be set to at least ${MIN_SECRET_LENGTH} characters. ` +
        'Run: wrangler secret put JWT_SECRET',
    );
  }
  return env.JWT_SECRET;
}
