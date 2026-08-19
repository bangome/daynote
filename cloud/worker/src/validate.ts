import { AUTH_KEY_BYTES } from './verifier';
import { fromBase64Url } from './bytes';
import { ApiError } from './http';

/**
 * Request validation. Every check here turns a bad client into a 400 with a stable code, so a
 * malformed field never reaches D1 and never becomes a 500.
 */

/**
 * Deliberately permissive: this is a sanity check, not an RFC 5322 parser. Real address validity is
 * established by the verification email, not by a regex.
 */
const EMAIL = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;
const EMAIL_MAX = 254;

/**
 * A wrapped DEK is AES-256-GCM over exactly 32 bytes: a 12-byte nonce (16 base64url chars) and a
 * 48-byte ciphertext+tag (64 chars). Fixed sizes, so validate them exactly — a wrong length here
 * means a client bug that would otherwise be discovered only at decrypt time on another device.
 */
const WRAPPED_DEK = /^v1\.[A-Za-z0-9_-]{16}\.[A-Za-z0-9_-]{64}$/;

const KDF_PARAMS_MAX = 256;
const DEVICE_NAME_MAX = 64;
const CONTROL_CHARS = /[\p{Cc}\p{Cf}]/gu;

export function requireString(body: Record<string, unknown>, field: string): string {
  const value = body[field];
  if (typeof value !== 'string' || value.length === 0) {
    throw new ApiError('bad_request', `Field '${field}' is required.`);
  }
  return value;
}

export function normalizeEmail(body: Record<string, unknown>): string {
  const raw = requireString(body, 'email').trim().toLowerCase();
  if (raw.length > EMAIL_MAX || !EMAIL.test(raw)) {
    throw new ApiError('bad_request', "Field 'email' is not a valid address.");
  }
  return raw;
}

/** Decodes and length-checks a base64url auth key. The password itself must never arrive here. */
export function requireAuthKey(body: Record<string, unknown>, field = 'auth_key'): Uint8Array {
  if ('password' in body) {
    // A client sending a password has misunderstood the protocol; failing loudly beats silently
    // accepting a plaintext password the server should never see. See docs/CLOUD_SYNC.md §10.
    throw new ApiError('bad_request', 'Send auth_key, never a password. See docs/CLOUD_SYNC.md §4.2.');
  }

  const decoded = fromBase64Url(requireString(body, field));
  if (decoded === null || decoded.length !== AUTH_KEY_BYTES) {
    throw new ApiError('bad_request', `Field '${field}' must be ${AUTH_KEY_BYTES} base64url bytes.`);
  }
  return decoded;
}

export function requireWrappedDek(body: Record<string, unknown>, field: string): string {
  const value = requireString(body, field);
  if (!WRAPPED_DEK.test(value)) {
    throw new ApiError('bad_request', `Field '${field}' is not a v1 AES-GCM envelope.`);
  }
  return value;
}

export function optionalWrappedDek(body: Record<string, unknown>, field: string): string | null {
  const value = body[field];
  if (value === undefined || value === null) {
    return null;
  }
  return requireWrappedDek(body, field);
}

/**
 * The client's own KDF parameters, stored opaquely and echoed back at login so the client can
 * re-derive with the parameters that were in force when the account was created. The server has no
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
