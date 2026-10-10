import { encodeJson, importPrivateKey } from './apple';
import { fromBase64Url, toBase64Url } from './bytes';
import { ApiError } from './http';
import { APPLE_ROOT_CA_G3, CertificateError, decodeBase64, importPublicKey, verifyAppleChain } from './x509';
import type { Plan, Tier } from './entitlement';
import type { Env } from './env';

/**
 * The App Store side of subscriptions: the product catalogue, the App Store Server API, and the
 * signed payloads Apple sends (docs/CLOUD_SYNC.md §14.8).
 *
 * The iPhone app sells the same two tiers at the same two intervals as Paddle does on the desktop,
 * because App Store guideline 3.1.3(b) lets an app unlock what was bought elsewhere only if it also
 * sells it through In-App Purchase. The app buys with StoreKit and posts the transaction id here;
 * nothing the app says about the purchase is trusted beyond that id. Everything else is read from
 * Apple — over the authenticated API, or from a notification whose signature chains to the Apple
 * root pinned in src/x509.ts — and every JWS is verified either way, so the API path does not rest
 * on TLS alone.
 */

/**
 * The four App Store products, and the only place a product id is mapped to what it sells. The
 * ids are fixed for good once created in App Store Connect (subscription group "Daynote Cloud",
 * Premium ranked above Pro so a move between them is an upgrade or downgrade, not a second
 * subscription). Display order: Pro before Premium, monthly before annual.
 */
export const APPLE_PRODUCTS: Readonly<Record<string, { readonly tier: Tier; readonly plan: Plan }>> = {
  'cc.arachat.daynote.pro.monthly': { tier: 'pro', plan: 'monthly' },
  'cc.arachat.daynote.pro.annual': { tier: 'pro', plan: 'annual' },
  'cc.arachat.daynote.premium.monthly': { tier: 'premium', plan: 'monthly' },
  'cc.arachat.daynote.premium.annual': { tier: 'premium', plan: 'annual' },
};

/**
 * The products the app may offer, from `APPLE_IAP_PRODUCTS` (comma-separated). Taking one off sale
 * there hides it in the app with no release; it does not change what an existing subscriber to it
 * is entitled to, because that is read from `APPLE_PRODUCTS` above.
 */
export function productsOnSale(env: Env): { productId: string; tier: Tier; plan: Plan }[] {
  const listed = new Set(
    (env.APPLE_IAP_PRODUCTS ?? '').split(',').map((id) => id.trim()).filter((id) => id.length > 0),
  );
  return Object.entries(APPLE_PRODUCTS)
    .filter(([productId]) => listed.has(productId))
    .map(([productId, offer]) => ({ productId, ...offer }));
}

const PRODUCTION_API = 'https://api.storekit.itunes.apple.com';
const SANDBOX_API = 'https://api.storekit-sandbox.itunes.apple.com';

/** "TransactionIdNotFoundError": the id is not from this environment. TestFlight buys in sandbox. */
const TRANSACTION_NOT_FOUND = 4040010;

/** Apple accepts up to an hour; a token minted per call needs seconds. */
const API_TOKEN_TTL_SECONDS = 5 * 60;

export type AppStoreEnvironment = 'Production' | 'Sandbox';

/** A decoded `JWSTransaction`. Dates are milliseconds since the epoch, as Apple sends them. */
export interface AppleTransaction {
  readonly transactionId?: string;
  readonly originalTransactionId?: string;
  readonly bundleId?: string;
  readonly productId?: string;
  readonly type?: string;
  readonly purchaseDate?: number;
  readonly expiresDate?: number;
  readonly revocationDate?: number;
  readonly appAccountToken?: string;
  readonly environment?: string;
  readonly signedDate?: number;
  /** `PURCHASED`, or `FAMILY_SHARED` for a family member's share. */
  readonly inAppOwnershipType?: string;
}

/** A decoded `JWSRenewalInfo`. */
export interface AppleRenewalInfo {
  readonly originalTransactionId?: string;
  readonly autoRenewProductId?: string;
  /** 1 renews at the period end, 0 the subscriber turned it off. */
  readonly autoRenewStatus?: number;
  readonly gracePeriodExpiresDate?: number;
  readonly isInBillingRetryPeriod?: boolean;
  readonly signedDate?: number;
}

/** A decoded `responseBodyV2DecodedPayload`. */
export interface AppleNotification {
  readonly notificationType?: string;
  readonly subtype?: string;
  readonly notificationUUID?: string;
  readonly signedDate?: number;
  readonly data?: {
    readonly appAppleId?: number;
    readonly bundleId?: string;
    readonly environment?: string;
    /** 1 active, 2 expired, 3 billing retry, 4 billing grace period, 5 revoked. */
    readonly status?: number;
    readonly signedTransactionInfo?: string;
    readonly signedRenewalInfo?: string;
  };
}

interface IapConfig {
  readonly bundleId: string;
  readonly keyId: string;
  readonly issuerId: string;
  readonly privateKey: string;
}

function configured(value: string | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/** True when the server can verify purchases: the In-App Purchase key and its ids are all set. */
export function iapConfigured(env: Env): boolean {
  return readConfig(env) !== null;
}

function readConfig(env: Env): IapConfig | null {
  const { APPLE_BUNDLE_ID, APPLE_IAP_KEY_ID, APPLE_IAP_ISSUER_ID, APPLE_IAP_PRIVATE_KEY } = env;
  if (!configured(APPLE_BUNDLE_ID) || !configured(APPLE_IAP_KEY_ID) || !configured(APPLE_IAP_ISSUER_ID) ||
    !configured(APPLE_IAP_PRIVATE_KEY)) {
    return null;
  }
  return {
    bundleId: APPLE_BUNDLE_ID.trim(),
    keyId: APPLE_IAP_KEY_ID.trim(),
    issuerId: APPLE_IAP_ISSUER_ID.trim(),
    privateKey: APPLE_IAP_PRIVATE_KEY,
  };
}

/** A refusal the app can show: nothing is broken, a key is missing. */
function requireConfig(env: Env): IapConfig {
  const config = readConfig(env);
  if (config === null) {
    // 503, not 400: the purchase is fine and the app keeps it to send again once the key is set.
    throw new ApiError('unavailable', 'App Store purchases are not configured on this server yet.');
  }
  return config;
}

/** The bearer token the App Store Server API wants: ES256, signed with the In-App Purchase key. */
async function apiToken(config: IapConfig, now: Date): Promise<string> {
  const iat = Math.floor(now.getTime() / 1000);
  const signingInput = `${encodeJson({ alg: 'ES256', kid: config.keyId, typ: 'JWT' })}.${encodeJson({
    iss: config.issuerId,
    iat,
    exp: iat + API_TOKEN_TTL_SECONDS,
    aud: 'appstoreconnect-v1',
    bid: config.bundleId,
  })}`;
  const signature = await crypto.subtle.sign(
    { name: 'ECDSA', hash: 'SHA-256' },
    await importPrivateKey(config.privateKey, 'APPLE_IAP_PRIVATE_KEY'),
    new TextEncoder().encode(signingInput),
  );
  return `${signingInput}.${toBase64Url(new Uint8Array(signature))}`;
}

/**
 * One GET against the App Store Server API. Production first; a transaction Production has never
 * heard of is retried against the sandbox, which is where TestFlight and App Review buy. Once an
 * environment is known, later calls for the same purchase go straight to it.
 */
export async function appStoreGet(
  env: Env,
  path: string,
  now: Date,
  environment?: AppStoreEnvironment,
): Promise<{ environment: AppStoreEnvironment; body: Record<string, unknown> }> {
  const config = requireConfig(env);
  const token = await apiToken(config, now);
  const order: AppStoreEnvironment[] = environment === undefined ? ['Production', 'Sandbox'] : [environment];

  for (const candidate of order) {
    const base = candidate === 'Production' ? PRODUCTION_API : SANDBOX_API;
    const response = await fetch(`${base}${path}`, { headers: { authorization: `Bearer ${token}` } });
    const body = (await response.json().catch(() => ({}))) as Record<string, unknown>;
    if (response.ok) {
      return { environment: candidate, body };
    }
    // Before the app's first App Store release, Production answers 401 for every request — the
    // same token Sandbox accepts — so a TestFlight purchase never reached the sandbox. A 401 from
    // Production is therefore "ask the sandbox", like an unknown transaction; one from the last
    // environment asked is still a real failure, logged below.
    if (response.status === 401 && candidate === 'Production' && candidate !== order.at(-1)) {
      continue;
    }
    if (response.status === 404 && body['errorCode'] === TRANSACTION_NOT_FOUND) {
      if (candidate !== order.at(-1)) {
        continue;
      }
      // Unknown in every environment asked. For a transaction StoreKit has only just handed the
      // app, that is Apple's API not having caught up yet — seen with a fresh sandbox purchase — so
      // it is "not yet", which the app retries, never "no".
      throw new ApiError('purchase_pending', 'The App Store has not confirmed that purchase yet. It is retried automatically.');
    }
    if (response.status === 404 || response.status === 400) {
      // An id that is not a transaction of this app, in either environment.
      throw new ApiError('not_found', 'The App Store does not know that purchase.');
    }
    console.error('app store api failed', candidate, path, response.status, JSON.stringify(body).slice(0, 300));
    throw new ApiError('server_error', 'The App Store could not be reached. The purchase is safe; try again.');
  }
  throw new ApiError('purchase_pending', 'The App Store has not confirmed that purchase yet. It is retried automatically.');
}

/** How long to wait between asks while Apple has not caught up with a new transaction. */
const PENDING_RETRY_DELAYS_MS = [1000, 2000, 4000];

/**
 * `appStoreGet`, asked again after 1, 2 and 4 seconds while Apple answers "not found" in every
 * environment. A transaction StoreKit has just completed can take a few seconds to reach the App
 * Store Server API; the waits cost no CPU time, and a request that still finds nothing answers
 * `purchase_pending` for the app to retry later.
 */
export async function appStoreGetWhenKnown(
  env: Env,
  path: string,
  now: Date,
): Promise<{ environment: AppStoreEnvironment; body: Record<string, unknown> }> {
  const sleep = env.APPLE_RETRY_SLEEP ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)));
  for (let attempt = 0; ; attempt += 1) {
    try {
      return await appStoreGet(env, path, now);
    } catch (error) {
      const delay = PENDING_RETRY_DELAYS_MS[attempt];
      if (!(error instanceof ApiError) || error.code !== 'purchase_pending' || delay === undefined) {
        throw error;
      }
      await sleep(delay);
    }
  }
}

function decodeSegment(segment: string): unknown {
  const bytes = fromBase64Url(segment);
  if (bytes === null) {
    throw new CertificateError('Malformed JWS.');
  }
  return JSON.parse(new TextDecoder().decode(bytes));
}

/**
 * Verifies an Apple JWS — a notification, a transaction, or a renewal info — and returns its
 * payload. The `x5c` chain must end at the pinned Apple root (src/x509.ts); its leaf key must sign
 * the JWS. `alg` is read only to insist on ES256, the one algorithm Apple uses.
 *
 * Throws `ApiError('unauthorized')` for anything that does not verify, so a forged notification is
 * refused rather than recorded.
 */
export async function verifyJws<T>(env: Env, jws: unknown, now: Date): Promise<T> {
  try {
    if (typeof jws !== 'string') {
      throw new CertificateError('Missing JWS.');
    }
    const parts = jws.split('.');
    if (parts.length !== 3) {
      throw new CertificateError('Malformed JWS.');
    }
    const header = decodeSegment(parts[0]!) as { alg?: unknown; x5c?: unknown };
    if (header.alg !== 'ES256' || !Array.isArray(header.x5c) || !header.x5c.every((c) => typeof c === 'string')) {
      throw new CertificateError('Unexpected JWS header.');
    }

    const seam = env.APPLE_ROOT_CERTIFICATES;
    const roots = Array.isArray(seam) && seam.length > 0 && seam.every((root) => root instanceof Uint8Array)
      ? seam
      : [decodeBase64(APPLE_ROOT_CA_G3)];
    const leaf = await verifyAppleChain((header.x5c as string[]).map(decodeBase64), roots, now);
    if (leaf.curve.name !== 'P-256') {
      throw new CertificateError('Unexpected signing key.');
    }
    const signature = fromBase64Url(parts[2]!);
    if (signature === null || !(await crypto.subtle.verify(
      { name: 'ECDSA', hash: 'SHA-256' },
      await importPublicKey(leaf),
      signature as BufferSource,
      new TextEncoder().encode(`${parts[0]}.${parts[1]}`) as BufferSource,
    ))) {
      throw new CertificateError('The JWS signature does not verify.');
    }
    return decodeSegment(parts[1]!) as T;
  } catch (error) {
    if (error instanceof CertificateError || error instanceof SyntaxError || error instanceof DOMException) {
      console.error('apple jws refused', error.message);
      throw new ApiError('unauthorized', 'The App Store payload is not signed by Apple.');
    }
    throw error;
  }
}
