/**
 * Canonical UTC wire format: `yyyy-MM-ddTHH:mm:ss.fffffffZ`.
 *
 * This is deliberately NOT `Date.toISOString()` (3 fractional digits) and NOT .NET's round-trip
 * `"O"` format (`+00:00` offset). Mixing those two breaks ordinal string comparison — after the
 * shared `.123` prefix, `"4567+00:00"` sorts before `"Z"`, so a .NET timestamp would compare as
 * older than a JavaScript timestamp taken at the same instant. Every timestamp crossing the wire
 * uses the format below so that the server's stale-push check can be a plain string comparison.
 *
 * See docs/CLOUD_SYNC.md §7.3.
 */

const CANONICAL = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$/;

/** 7 fractional digits, matching .NET's tick resolution; JS only fills the first three. */
export function canonicalUtc(instant: Date = new Date()): string {
  return `${instant.toISOString().slice(0, -1)}0000Z`;
}

export function isCanonicalUtc(value: unknown): value is string {
  return typeof value === 'string' && CANONICAL.test(value);
}

export function addSeconds(instant: Date, seconds: number): Date {
  return new Date(instant.getTime() + seconds * 1000);
}

export const DAY_SECONDS = 86_400;
