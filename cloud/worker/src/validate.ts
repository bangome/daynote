import type { OAuthClient } from './google';
import { fromBase64Url } from './bytes';
import { ApiError } from './http';

/**
 * Request validation. Every check here turns a bad client into a 400 with a stable code, so a
 * malformed field never reaches D1 and never becomes a 500.
 */

const DEVICE_NAME_MAX = 64;
const CONTROL_CHARS = /[\p{Cc}\p{Cf}]/gu;

/** OAuth codes and PKCE verifiers are short; anything longer is not one of ours. */
const CODE_MAX = 2048;

/**
 * A wrapped DEK is AES-256-GCM over exactly 32 bytes: a 12-byte nonce (16 base64url chars) and a
 * 48-byte ciphertext+tag (64 chars). Fixed sizes, so validate them exactly — a wrong length here
 * means a client bug that would otherwise be discovered only at unlock time on another device.
 */
const WRAPPED_DEK = /^v1\.[A-Za-z0-9_-]{16}\.[A-Za-z0-9_-]{64}$/;

/** The raw data key handed back when the lock is turned off, base64url over TLS. */
const RAW_DEK = /^[A-Za-z0-9_-]{43}$/;

const KDF_PARAMS_MAX = 256;

export function requireString(body: Record<string, unknown>, field: string): string {
  const value = body[field];
  if (typeof value !== 'string' || value.length === 0 || value.length > CODE_MAX) {
    throw new ApiError('bad_request', `Field '${field}' is required.`);
  }
  return value;
}

/**
 * The loopback address the app listened on, echoed back to Google in the token exchange because the
 * two must match exactly.
 *
 * Restricted to loopback http, which is the only redirect a desktop OAuth client is allowed to use.
 * Accepting any URL here would let a caller point the exchange at a host of its choosing, and the
 * value is passed straight to Google.
 */
/**
 * Which app is signing in. Absent means the desktop one, which is the only client that existed
 * before the phones and the only one whose requests will not carry the field.
 */
export function requireClient(body: Record<string, unknown>): OAuthClient {
  const value = body['client'];
  if (value === undefined || value === null) {
    return 'desktop';
  }
  if (value === 'desktop' || value === 'ios' || value === 'android') {
    return value;
  }
  throw new ApiError('bad_request', "Field 'client' must be desktop, ios or android.");
}

/**
 * The address Google was told to send the code back to, checked against the shape that client can
 * actually own.
 *
 * The desktop app listens on a loopback port, so only an `http://127.0.0.1` style address makes
 * sense for it. A phone cannot hold a listening socket the system browser can reach, so it
 * registers a private scheme with the OS and Google redirects to that instead (RFC 8252 §7.1). The
 * check is per client because accepting either shape for either app would let a caller nominate a
 * redirect the app it claims to be could never have received.
 *
 * It is a shape check, not an allow-list: Google already refuses a redirect the client is not
 * registered with, so this only turns a confusing failure at Google into a clear one here.
 */
export function requireRedirectUri(body: Record<string, unknown>, client: OAuthClient): string {
  const value = requireString(body, 'redirect_uri');

  let parsed: URL;
  try {
    parsed = new URL(value);
  } catch {
    throw new ApiError('bad_request', "Field 'redirect_uri' is not a URL.");
  }

  if (client === 'desktop') {
    const loopback = parsed.hostname === '127.0.0.1' || parsed.hostname === '[::1]' ||
      parsed.hostname === 'localhost';
    if (parsed.protocol !== 'http:' || !loopback) {
      throw new ApiError('bad_request', "Field 'redirect_uri' must be an http loopback address.");
    }
    return value;
  }

  // A private-use scheme: at least one dot, as Google requires, and nothing that could be http(s)
  // or a scheme-relative URL pointing somewhere on the web.
  const scheme = parsed.protocol.slice(0, -1);
  if (!PRIVATE_SCHEME.test(scheme)) {
    throw new ApiError('bad_request', "Field 'redirect_uri' must use the app's own URI scheme.");
  }
  return value;
}

/** A reversed-domain scheme, which is the only kind Google issues to an installed app. */
const PRIVATE_SCHEME = /^[a-z][a-z0-9+.-]*\.[a-z0-9+.-]+$/;

export function requireWrappedDek(body: Record<string, unknown>, field: string): string {
  const value = requireString(body, field);
  if (!WRAPPED_DEK.test(value)) {
    throw new ApiError('bad_request', `Field '${field}' is not a v1 AES-GCM envelope.`);
  }
  return value;
}

/**
 * The unwrapped data key, sent only when an account turns the lock off and hands custody back.
 * Checked for shape and length so a truncated key cannot be sealed and stored as if it were whole.
 */
export function requireRawDek(body: Record<string, unknown>): Uint8Array {
  const value = requireString(body, 'data_key');
  const decoded = RAW_DEK.test(value) ? fromBase64Url(value) : null;
  if (decoded === null || decoded.length !== 32) {
    throw new ApiError('bad_request', "Field 'data_key' must be 32 base64url bytes.");
  }
  return decoded;
}

/**
 * The client's own KDF parameters, stored opaquely and echoed back at sign-in so a device can
 * re-derive with the parameters that were in force when the lock was turned on. The server has no
 * business interpreting them beyond a shape check.
 */
export function requireKdfParams(body: Record<string, unknown>): string {
  const value = body['kdf_params'];
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new ApiError('bad_request', "Field 'kdf_params' must be a JSON object.");
  }

  const kdf = (value as Record<string, unknown>)['kdf'];
  if (kdf !== 'argon2id' && kdf !== 'pbkdf2-sha256') {
    throw new ApiError('bad_request', "Field 'kdf_params.kdf' must be argon2id or pbkdf2-sha256.");
  }

  const serialized = JSON.stringify(value);
  if (serialized.length > KDF_PARAMS_MAX) {
    throw new ApiError('bad_request', "Field 'kdf_params' is too large.");
  }
  return serialized;
}

/** Free-text, shown back to the user in their device list, so strip control characters. */
export function deviceName(body: Record<string, unknown>): string {
  const raw = body['device_name'];
  if (typeof raw !== 'string') {
    return 'Unknown device';
  }

  const cleaned = raw.replace(CONTROL_CHARS, '').trim().slice(0, DEVICE_NAME_MAX);
  return cleaned.length === 0 ? 'Unknown device' : cleaned;
}
