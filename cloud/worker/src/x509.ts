/**
 * Just enough X.509 to check the certificate chain Apple puts in a JWS `x5c` header
 * (src/appStore.ts). There is no certificate API in Workers and no reason to pull in a library for
 * one job: read three DER certificates, check that each is signed by the next, that each is within
 * its validity, that the last is byte for byte the Apple root pinned here, and that the two below
 * it carry Apple's App Store marker extensions.
 *
 * What this deliberately does not do is general path building or revocation checking. The chain
 * is always exactly leaf → Apple WWDR intermediate → Apple Root CA - G3, and Apple's own server
 * library checks revocation only when asked to go online; the pinned root and the marker OIDs are
 * what make a certificate Apple's.
 */

/**
 * Apple Root CA - G3, from https://www.apple.com/certificateauthority/AppleRootCA-G3.cer.
 * SHA-256 63343ABFB89A6A03EBB57E9B3F5FA7BE7C4F5C756F3017B3A8C488C3653E9179, valid to 2039-04-30.
 */
export const APPLE_ROOT_CA_G3 =
  'MIICQzCCAcmgAwIBAgIILcX8iNLFS5UwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYD' +
  'VQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcN' +
  'MTQwNDMwMTgxOTA2WhcNMzkwNDMwMTgxOTA2WjBnMRswGQYDVQQDDBJBcHBsZSBSb290IENBIC0gRzMxJjAkBgNVBAsMHUFw' +
  'cGxlIENlcnRpZmljYXRpb24gQXV0aG9yaXR5MRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzB2MBAGByqGSM49' +
  'AgEGBSuBBAAiA2IABJjpLz1AcqTtkyJygRMc3RCV8cWjTnHcFBbZDuWmBSp3ZHtfTjjTuxxEtX/1H7YyYl3J6YRbTzBPEVoA' +
  '/VhYDKX1DyxNB0cTddqXl5dvMVztK517IDvYuVTZXpmkOlEKMaNCMEAwHQYDVR0OBBYEFLuw3qFYM4iapIqZ3r6966/ayySr' +
  'MA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMAoGCCqGSM49BAMDA2gAMGUCMQCD6cHEFl4aXTQY2e3v9GwOAEZL' +
  'uN+yRhHFD/3meoyhpmvOwgPUnPWTxnS4at+qIxUCMG1mihDK1A3UT82NQz60imOlM27jbdoXt2QfyFMm+YhidDkLF1vLUagM' +
  '6BgD56KyKA==';

/** Present on the certificate that signs App Store receipts and server payloads. */
export const OID_APPLE_RECEIPT_SIGNING = '1.2.840.113635.100.6.11.1';

/** Present on the Apple Worldwide Developer Relations intermediate. */
export const OID_APPLE_WWDR_INTERMEDIATE = '1.2.840.113635.100.6.2.1';

const OID_EC_PUBLIC_KEY = '1.2.840.10045.2.1';
const CURVES: Record<string, { name: string; size: number }> = {
  '1.2.840.10045.3.1.7': { name: 'P-256', size: 32 },
  '1.3.132.0.34': { name: 'P-384', size: 48 },
};
const SIGNATURE_HASHES: Record<string, string> = {
  '1.2.840.10045.4.3.2': 'SHA-256',
  '1.2.840.10045.4.3.3': 'SHA-384',
};

export class CertificateError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'CertificateError';
  }
}

interface Element {
  readonly tag: number;
  /** The whole element, header included: what a signature covers and what a Name compares as. */
  readonly raw: Uint8Array;
  readonly content: Uint8Array;
}

/** Reads one DER element at `offset`. Long-form lengths only up to four bytes: certificates are small. */
function readElement(bytes: Uint8Array, offset: number): { element: Element; next: number } {
  if (offset + 2 > bytes.length) {
    throw new CertificateError('Truncated DER.');
  }
  const tag = bytes[offset]!;
  let length = bytes[offset + 1]!;
  let header = 2;
  if (length & 0x80) {
    const count = length & 0x7f;
    if (count === 0 || count > 4 || offset + 2 + count > bytes.length) {
      throw new CertificateError('Unsupported DER length.');
    }
    length = 0;
    for (let i = 0; i < count; i += 1) {
      length = length * 256 + bytes[offset + 2 + i]!;
    }
    header += count;
  }
  const end = offset + header + length;
  if (end > bytes.length) {
    throw new CertificateError('Truncated DER.');
  }
  return {
    element: { tag, raw: bytes.subarray(offset, end), content: bytes.subarray(offset + header, end) },
    next: end,
  };
}

function children(element: Element): Element[] {
  const result: Element[] = [];
  let offset = 0;
  while (offset < element.content.length) {
    const { element: child, next } = readElement(element.content, offset);
    result.push(child);
    offset = next;
  }
  return result;
}

function expectTag(element: Element | undefined, tag: number, what: string): Element {
  if (element === undefined || element.tag !== tag) {
    throw new CertificateError(`Malformed certificate: ${what}.`);
  }
  return element;
}

function decodeOid(content: Uint8Array): string {
  const parts: number[] = [];
  let value = 0;
  for (const byte of content) {
    value = value * 128 + (byte & 0x7f);
    if ((byte & 0x80) === 0) {
      if (parts.length === 0) {
        const first = Math.min(2, Math.floor(value / 40));
        parts.push(first, value - first * 40);
      } else {
        parts.push(value);
      }
      value = 0;
    }
  }
  return parts.join('.');
}

function decodeTime(element: Element | undefined): number {
  if (element === undefined) {
    throw new CertificateError('Malformed certificate validity.');
  }
  const text = new TextDecoder().decode(element.content);
  let match: RegExpExecArray | null;
  if (element.tag === 0x17) {
    match = /^(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})Z$/.exec(text);
    if (match !== null) {
      // RFC 5280 §4.1.2.5.1: two-digit years 50–99 are 19xx.
      const year = Number(match[1]);
      match[1] = String(year >= 50 ? 1900 + year : 2000 + year);
    }
  } else if (element.tag === 0x18) {
    match = /^(\d{4})(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})Z$/.exec(text);
  } else {
    match = null;
  }
  if (match === null) {
    throw new CertificateError('Malformed certificate validity.');
  }
  const [, y, mo, d, h, mi, s] = match.map(Number) as number[];
  return Date.UTC(y!, mo! - 1, d!, h!, mi!, s!);
}

export interface Certificate {
  readonly der: Uint8Array;
  readonly tbs: Uint8Array;
  readonly signatureHash: string;
  /** DER `SEQUENCE { r, s }`, as X.509 stores it. */
  readonly signature: Uint8Array;
  readonly issuer: Uint8Array;
  readonly subject: Uint8Array;
  readonly notBefore: number;
  readonly notAfter: number;
  readonly spki: Uint8Array;
  readonly curve: { name: string; size: number };
  readonly extensions: ReadonlySet<string>;
}

export function parseCertificate(der: Uint8Array): Certificate {
  const { element: certificate, next } = readElement(der, 0);
  if (next !== der.length) {
    throw new CertificateError('Trailing bytes after the certificate.');
  }
  const [tbs, algorithm, signatureValue] = children(expectTag(certificate, 0x30, 'certificate'));
  const tbsFields = children(expectTag(tbs, 0x30, 'tbsCertificate'));

  // The version is an explicit [0]; every certificate here is v3 and has one.
  let index = tbsFields[0]?.tag === 0xa0 ? 1 : 0;
  index += 1; // serialNumber
  index += 1; // signature algorithm, repeated below and checked there
  const issuer = expectTag(tbsFields[index++], 0x30, 'issuer');
  const validity = children(expectTag(tbsFields[index++], 0x30, 'validity'));
  const subject = expectTag(tbsFields[index++], 0x30, 'subject');
  const spki = expectTag(tbsFields[index++], 0x30, 'subjectPublicKeyInfo');

  const extensions = new Set<string>();
  for (const field of tbsFields.slice(index)) {
    if (field.tag !== 0xa3) {
      continue;
    }
    for (const extension of children(expectTag(children(field)[0], 0x30, 'extensions'))) {
      const id = expectTag(children(extension)[0], 0x06, 'extension id');
      extensions.add(decodeOid(id.content));
    }
  }

  const algorithmId = decodeOid(expectTag(children(expectTag(algorithm, 0x30, 'algorithm'))[0], 0x06, 'algorithm').content);
  const signatureHash = SIGNATURE_HASHES[algorithmId];
  if (signatureHash === undefined) {
    throw new CertificateError(`Unsupported signature algorithm ${algorithmId}.`);
  }

  const keyAlgorithm = children(expectTag(children(spki)[0], 0x30, 'key algorithm'));
  const keyType = decodeOid(expectTag(keyAlgorithm[0], 0x06, 'key type').content);
  const curve = CURVES[decodeOid(expectTag(keyAlgorithm[1], 0x06, 'curve').content)];
  if (keyType !== OID_EC_PUBLIC_KEY || curve === undefined) {
    throw new CertificateError('Unsupported public key.');
  }

  // A BIT STRING's first content byte counts unused bits; a signature has none.
  const bits = expectTag(signatureValue, 0x03, 'signature');
  return {
    der,
    tbs: tbs!.raw,
    signatureHash,
    signature: bits.content.subarray(1),
    issuer: issuer.raw,
    subject: subject.raw,
    notBefore: decodeTime(validity[0]),
    notAfter: decodeTime(validity[1]),
    spki: spki.raw,
    curve,
    extensions,
  };
}

/** `SEQUENCE { INTEGER r, INTEGER s }` to the fixed-width `r || s` WebCrypto verifies. */
export function derToRawSignature(der: Uint8Array, size: number): Uint8Array {
  const { element } = readElement(der, 0);
  const [r, s] = children(expectTag(element, 0x30, 'signature'));
  const raw = new Uint8Array(size * 2);
  for (const [i, integer] of [r, s].entries()) {
    let value = expectTag(integer, 0x02, 'signature integer').content;
    while (value.length > size && value[0] === 0) {
      value = value.subarray(1);
    }
    if (value.length > size) {
      throw new CertificateError('Signature integer too long.');
    }
    raw.set(value, i * size + (size - value.length));
  }
  return raw;
}

export function importPublicKey(certificate: Certificate): Promise<CryptoKey> {
  return crypto.subtle.importKey(
    'spki',
    certificate.spki as BufferSource,
    { name: 'ECDSA', namedCurve: certificate.curve.name },
    false,
    ['verify'],
  );
}

function sameBytes(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) {
    return false;
  }
  for (let i = 0; i < a.length; i += 1) {
    if (a[i] !== b[i]) {
      return false;
    }
  }
  return true;
}

async function signedBy(child: Certificate, parent: Certificate): Promise<boolean> {
  if (!sameBytes(child.issuer, parent.subject)) {
    return false;
  }
  return crypto.subtle.verify(
    { name: 'ECDSA', hash: child.signatureHash },
    await importPublicKey(parent),
    derToRawSignature(child.signature, parent.curve.size) as BufferSource,
    child.tbs as BufferSource,
  );
}

export function decodeBase64(value: string): Uint8Array {
  const binary = atob(value);
  return Uint8Array.from(binary, (char) => char.charCodeAt(0));
}

/**
 * Checks `[leaf, intermediate, root]` and returns the leaf, whose key then verifies the JWS.
 * `trustedRoots` is the pinned Apple root in production; tests pass a root of their own.
 */
export async function verifyAppleChain(
  chain: readonly Uint8Array[],
  trustedRoots: readonly Uint8Array[],
  now: Date,
): Promise<Certificate> {
  if (chain.length !== 3) {
    throw new CertificateError('Expected a chain of three certificates.');
  }
  if (!trustedRoots.some((root) => sameBytes(root, chain[2]!))) {
    throw new CertificateError('The chain does not end at the Apple root.');
  }

  const [leaf, intermediate, root] = chain.map(parseCertificate) as [Certificate, Certificate, Certificate];
  const at = now.getTime();
  for (const certificate of [leaf, intermediate, root]) {
    if (at < certificate.notBefore || at > certificate.notAfter) {
      throw new CertificateError('A certificate in the chain is not valid now.');
    }
  }
  if (!leaf.extensions.has(OID_APPLE_RECEIPT_SIGNING)) {
    throw new CertificateError('The signing certificate is not an App Store one.');
  }
  if (!intermediate.extensions.has(OID_APPLE_WWDR_INTERMEDIATE)) {
    throw new CertificateError('The intermediate is not Apple WWDR.');
  }
  if (!(await signedBy(leaf, intermediate)) || !(await signedBy(intermediate, root))) {
    throw new CertificateError('A certificate in the chain is not signed by the next.');
  }
  return leaf;
}
