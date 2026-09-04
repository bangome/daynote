import type { Env } from './env';

/**
 * Paddle publishes the addresses its webhooks come from at https://api.paddle.com/ips. The list is
 * the source of truth and can change, so it is fetched rather than copied into this file, and
 * cached per isolate for a while so a burst of deliveries does not turn into a burst of fetches.
 *
 * The check is a second fence in front of the signature, not a replacement for it: the signature
 * proves the body came from Paddle, the address check refuses to even read a body from anywhere
 * else. If the list cannot be fetched and nothing is cached, the address check is skipped and the
 * signature alone decides — refusing every delivery because Paddle's own endpoint hiccuped would
 * silently stop subscriptions from being recorded.
 */

const IPS_URL = 'https://api.paddle.com/ips';
const CACHE_TTL_MS = 60 * 60 * 1000;

let cached: { cidrs: string[]; fetchedAt: number } | null = null;

async function fetchCidrs(): Promise<string[]> {
  const response = await fetch(IPS_URL, { headers: { accept: 'application/json' } });
  if (!response.ok) {
    throw new Error(`GET ${IPS_URL} -> ${response.status}`);
  }
  const body = (await response.json()) as { data?: { ipv4_cidrs?: unknown } };
  const cidrs = body.data?.ipv4_cidrs;
  if (!Array.isArray(cidrs) || cidrs.some((entry) => typeof entry !== 'string')) {
    throw new Error(`GET ${IPS_URL} returned no ipv4_cidrs`);
  }
  return cidrs as string[];
}

/** The current allow-list, or null when it is unknown right now. */
export async function paddleCidrs(env: Env, now: Date): Promise<string[] | null> {
  if (env.PADDLE_IPS !== undefined) {
    return env.PADDLE_IPS();
  }
  if (cached !== null && now.getTime() - cached.fetchedAt < CACHE_TTL_MS) {
    return cached.cidrs;
  }
  try {
    const cidrs = await fetchCidrs();
    cached = { cidrs, fetchedAt: now.getTime() };
    return cidrs;
  } catch (error) {
    console.error('paddle ip list unavailable', error instanceof Error ? error.message : String(error));
    return cached?.cidrs ?? null;
  }
}

function ipv4ToInt(address: string): number | null {
  const parts = address.split('.');
  if (parts.length !== 4) {
    return null;
  }
  let value = 0;
  for (const part of parts) {
    if (!/^\d{1,3}$/.test(part)) {
      return null;
    }
    const octet = Number(part);
    if (octet > 255) {
      return null;
    }
    value = value * 256 + octet;
  }
  return value;
}

/** True when `address` falls inside `cidr` (IPv4 only; Paddle publishes /32s but any prefix works). */
export function inCidr(address: string, cidr: string): boolean {
  const [network, prefixText = '32'] = cidr.split('/');
  const prefix = Number(prefixText);
  const a = ipv4ToInt(address);
  const n = ipv4ToInt(network ?? '');
  if (a === null || n === null || !Number.isInteger(prefix) || prefix < 0 || prefix > 32) {
    return false;
  }
  if (prefix === 0) {
    return true;
  }
  const mask = (0xffffffff << (32 - prefix)) >>> 0;
  return ((a & mask) >>> 0) === ((n & mask) >>> 0);
}

/**
 * Whether the request may be a Paddle webhook, judged by its source address.
 *
 * `cf-connecting-ip` is set by Cloudflare's edge and cannot be forged by the client, which is what
 * makes it usable here. Returns true when the allow-list is unknown (see the file comment).
 */
export async function isFromPaddle(request: Request, env: Env, now: Date): Promise<boolean> {
  const cidrs = await paddleCidrs(env, now);
  if (cidrs === null) {
    return true;
  }
  const address = request.headers.get('cf-connecting-ip');
  if (address === null) {
    return false;
  }
  return cidrs.some((cidr) => inCidr(address, cidr));
}
