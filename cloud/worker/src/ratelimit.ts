import { ApiError } from './http';
import { canonicalUtc } from './time';
import type { Env } from './env';

/**
 * Fixed-window rate limiting on D1.
 *
 * A fixed window lets a caller burst up to 2x the limit across a boundary. That is acceptable here:
 * the point is to make online password guessing uneconomic, not to shape traffic precisely. A
 * Durable Object would give exact sliding windows at the cost of another binding and another
 * failure mode — worth revisiting only if abuse actually shows up.
 */

const WINDOW_SECONDS = 15 * 60;

export interface Limit {
  readonly action: string;
  readonly scope: string;
  readonly value: string;
  readonly max: number;
}

function bucketKey(limit: Limit, now: Date): string {
  const window = Math.floor(now.getTime() / 1000 / WINDOW_SECONDS);
  return `${limit.action}:${limit.scope}:${limit.value}:${window}`;
}

function retryAfterSeconds(now: Date): number {
  const elapsed = Math.floor(now.getTime() / 1000) % WINDOW_SECONDS;
  return WINDOW_SECONDS - elapsed;
}

/**
 * Counts one hit against each limit and throws `rate_limited` if any is exceeded. Counting happens
 * before the credential check so that a wrong guess still costs the attacker a slot.
 */
export async function enforce(env: Env, limits: readonly Limit[], now: Date): Promise<void> {
  const expires = canonicalUtc(new Date(now.getTime() + WINDOW_SECONDS * 2 * 1000));

  const results = await env.DB.batch(
    limits.map((limit) =>
      env.DB.prepare(
        `INSERT INTO rate_limits (bucket, hits, expires_utc) VALUES (?1, 1, ?2)
         ON CONFLICT(bucket) DO UPDATE SET hits = hits + 1
         RETURNING hits`,
      ).bind(bucketKey(limit, now), expires),
    ),
  );

  for (let i = 0; i < limits.length; i += 1) {
    const hits = (results[i]?.results?.[0] as { hits?: number } | undefined)?.hits ?? 0;
    if (hits > limits[i]!.max) {
      throw new ApiError(
        'rate_limited',
        'Too many attempts. Try again later.',
        retryAfterSeconds(now),
      );
    }
  }
}

/**
 * Opportunistic cleanup of expired buckets. Called on a small fraction of requests rather than from
 * a cron trigger, so Phase 1 needs no scheduled worker.
 */
export async function sweep(env: Env, now: Date): Promise<void> {
  await env.DB.prepare('DELETE FROM rate_limits WHERE expires_utc < ?1').bind(canonicalUtc(now)).run();
}

export const LOGIN_LIMITS = (email: string, ip: string): readonly Limit[] => [
  { action: 'login', scope: 'email', value: email, max: 10 },
  { action: 'login', scope: 'ip', value: ip, max: 30 },
];

export const REGISTER_LIMITS = (ip: string): readonly Limit[] => [
  { action: 'register', scope: 'ip', value: ip, max: 5 },
];
