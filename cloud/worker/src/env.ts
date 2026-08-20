export interface Env {
  /** The D1 binding declared in wrangler.toml. */
  DB: D1Database;

  /** HS256 signing secret for access tokens. Set with `wrangler secret put JWT_SECRET`. */
  JWT_SECRET: string;

  /** Optional overrides, provided as strings because Workers vars are always strings. */
  ACCESS_TOKEN_TTL_SECONDS?: string;
  REFRESH_TOKEN_TTL_DAYS?: string;

  /**
   * Transactional email for password resets. Absent means the reset endpoints return a server error
   * rather than pretending to have sent something.
   */
  MAILCHANNELS_API_KEY?: string;
  EMAIL_FROM?: string;
  EMAIL_FROM_NAME?: string;

  /** Without DKIM the reset code lands in spam, which users read as "reset is broken". */
  DKIM_DOMAIN?: string;
  DKIM_SELECTOR?: string;
  DKIM_PRIVATE_KEY?: string;

  /** Test seam: an in-memory sender, so the reset flow can be exercised without sending mail. */
  EMAIL_SENDER?: import('./email').EmailSender;
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
