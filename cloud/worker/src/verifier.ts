import { fromBase64Url, randomBytes, timingSafeEqual, toBase64Url } from './bytes';

/**
 * Server-side password verifier.
 *
 * The client already spent Argon2id on the password and sends only `auth_key`, a 32-byte uniformly
 * random value (docs/CLOUD_SYNC.md §4.2). Because the input is already high-entropy, this second
 * stage does not need to be expensive — it exists so that a leaked `users` table does not hand out
 * usable `auth_key`s, not to slow down a password guess. Keeping it cheap is what keeps the Worker
 * inside a small CPU budget.
 *
 * NEVER accept a raw password here. The password must not reach the server.
 */

const ALGORITHM = 'pbkdf2';
const HASH = 'sha256';
const ITERATIONS = 100_000;
const SALT_BYTES = 16;
const KEY_BITS = 256;

export const AUTH_KEY_BYTES = 32;

/**
 * Stand-in verifier used when an email is unknown, so that "no such user" costs the same as "wrong
 * password" and cannot be distinguished by timing. Derived from a throwaway key at module load.
 */
const DECOY_VERIFIER = `${ALGORITHM}$${HASH}$${ITERATIONS}$${toBase64Url(
  new Uint8Array(SALT_BYTES),
)}$${toBase64Url(new Uint8Array(KEY_BITS / 8))}`;

async function derive(authKey: Uint8Array, salt: Uint8Array, iterations: number): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey('raw', authKey as BufferSource, 'PBKDF2', false, [
    'deriveBits',
  ]);
  const bits = await crypto.subtle.deriveBits(
    { name: 'PBKDF2', hash: 'SHA-256', salt: salt as BufferSource, iterations },
    key,
    KEY_BITS,
  );
  return new Uint8Array(bits);
}

export async function createVerifier(authKey: Uint8Array): Promise<string> {
  const salt = randomBytes(SALT_BYTES);
  const hash = await derive(authKey, salt, ITERATIONS);
  return `${ALGORITHM}$${HASH}$${ITERATIONS}$${toBase64Url(salt)}$${toBase64Url(hash)}`;
}

/**
 * Verifies `authKey` against a stored verifier. A malformed verifier returns false rather than
 * throwing, so a corrupt row cannot become a 500 that distinguishes accounts.
 */
export async function verifyAuthKey(verifier: string, authKey: Uint8Array): Promise<boolean> {
  const parts = verifier.split('$');
  if (parts.length !== 5 || parts[0] !== ALGORITHM || parts[1] !== HASH) {
    return false;
  }

  const iterations = Number(parts[2]);
  const salt = fromBase64Url(parts[3]!);
  const expected = fromBase64Url(parts[4]!);
  if (!Number.isSafeInteger(iterations) || iterations < 1 || iterations > 1_000_000) {
    return false;
  }
  if (salt === null || expected === null) {
    return false;
  }

  const actual = await derive(authKey, salt, iterations);
  return timingSafeEqual(actual, expected);
}

/** Burns the same CPU as a real verification, for the unknown-email path. */
export async function verifyDecoy(authKey: Uint8Array): Promise<void> {
  await verifyAuthKey(DECOY_VERIFIER, authKey);
}

/** True when the stored verifier no longer matches this build's parameters and should be upgraded. */
export function needsUpgrade(verifier: string): boolean {
  const parts = verifier.split('$');
  return parts.length !== 5 || parts[0] !== ALGORITHM || parts[1] !== HASH || Number(parts[2]) !== ITERATIONS;
}
