/** Byte, base64url, and constant-time helpers. No secrets are logged from this module. */

const B64URL = /^[A-Za-z0-9_-]+$/;

export function randomBytes(length: number): Uint8Array {
  const buffer = new Uint8Array(length);
  crypto.getRandomValues(buffer);
  return buffer;
}

export function toBase64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}

/** Returns null rather than throwing: callers turn a bad value into a 400, never a 500. */
export function fromBase64Url(value: string): Uint8Array | null {
  if (value.length === 0 || !B64URL.test(value)) {
    return null;
  }

  const padded = value.replaceAll('-', '+').replaceAll('_', '/');
  try {
    const binary = atob(padded + '='.repeat((4 - (padded.length % 4)) % 4));
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
  } catch {
    return null;
  }
}

export function toHex(bytes: Uint8Array): string {
  let hex = '';
  for (const byte of bytes) {
    hex += byte.toString(16).padStart(2, '0');
  }
  return hex;
}

export async function sha256Hex(value: string | Uint8Array): Promise<string> {
  const input = typeof value === 'string' ? new TextEncoder().encode(value) : value;
  const digest = await crypto.subtle.digest('SHA-256', input as BufferSource);
  return toHex(new Uint8Array(digest));
}

/**
 * Length-independent comparison for equal-length secrets. Length is compared first and leaks only
 * the length, which is fixed for every secret this Worker handles.
 */
export function timingSafeEqual(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) {
    return false;
  }

  let difference = 0;
  for (let i = 0; i < a.length; i += 1) {
    difference |= a[i]! ^ b[i]!;
  }
  return difference === 0;
}

export function uuid(): string {
  return crypto.randomUUID();
}
