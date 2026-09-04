import { fromBase64Url, randomBytes, toBase64Url } from './bytes';
import type { Env } from './env';

/**
 * The server-held data key.
 *
 * Read this next to docs/CLOUD_SYNC.md §1: Daynote's cloud sync is **not** end-to-end encrypted.
 * The Worker generates each account's DEK, keeps it, and hands it back at sign-in, which means the
 * Worker can decrypt every note it stores. That is the cost of signing in with Google and nothing
 * else — an identity provider proves who you are but gives the client no secret to derive a key
 * from, and the alternative designs were rejected deliberately.
 *
 * What this module still buys: the DEK is sealed under `DEK_WRAP_KEY`, a Worker secret that does not
 * live in D1. A leaked database dump on its own is therefore ciphertext plus sealed keys, and the
 * secret has to leak too. Defence in depth against one failure, not a privacy guarantee.
 */

const DEK_BYTES = 32;
const NONCE_BYTES = 12;
const ENVELOPE = /^s1\.([A-Za-z0-9_-]{16})\.([A-Za-z0-9_-]{64})$/;

/** Derives the AES key from the configured secret, so the secret itself can be any length. */
async function wrappingKey(env: Env): Promise<CryptoKey> {
  const secret = env.DEK_WRAP_KEY;
  if (typeof secret !== 'string' || secret.length < 32) {
    // Refuse to start rather than silently sealing every account's key under a weak or absent
    // secret, which would be indistinguishable from working until the day it mattered.
    throw new Error(
      'DEK_WRAP_KEY must be set to at least 32 characters. ' +
        'Run: wrangler secret put DEK_WRAP_KEY',
    );
  }

  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(secret));
  return crypto.subtle.importKey('raw', digest, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']);
}

/** Generates a fresh data key and returns it both raw (to hand to the client) and sealed. */
export async function createDek(env: Env): Promise<{ raw: Uint8Array; wrapped: string }> {
  const raw = randomBytes(DEK_BYTES);
  return { raw, wrapped: await seal(env, raw) };
}

export async function seal(env: Env, dek: Uint8Array): Promise<string> {
  const nonce = randomBytes(NONCE_BYTES);
  const sealed = await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv: nonce as BufferSource },
    await wrappingKey(env),
    dek as BufferSource,
  );
  return `s1.${toBase64Url(nonce)}.${toBase64Url(new Uint8Array(sealed))}`;
}

/**
 * Opens a sealed DEK. A failure here is not a client error: the row was written by this Worker, so
 * it can only mean the secret was rotated or the row was tampered with, and both need a human.
 */
export async function open(env: Env, wrapped: string): Promise<Uint8Array> {
  const match = ENVELOPE.exec(wrapped);
  if (match === null) {
    throw new Error('The stored data key is not an s1 envelope.');
  }

  const nonce = fromBase64Url(match[1]!);
  const sealed = fromBase64Url(match[2]!);
  if (nonce === null || sealed === null) {
    throw new Error('The stored data key is not decodable.');
  }

  const plaintext = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: nonce as BufferSource },
    await wrappingKey(env),
    sealed as BufferSource,
  );
  return new Uint8Array(plaintext);
}

/** The wire form handed to the client: raw base64url, over TLS. */
export function toWire(dek: Uint8Array): string {
  return toBase64Url(dek);
}
