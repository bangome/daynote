/**
 * A stand-in for Apple's signing: a test root, an intermediate and a leaf, shaped like Apple Root
 * CA - G3 → WWDR → the App Store signing certificate (P-384, P-384, P-256, with Apple's marker
 * extensions), and JWS signed with the leaf the way Apple signs transactions and notifications.
 *
 * The certificates are built by hand in DER because nothing in the Workers runtime makes one; the
 * Worker's own parser (src/x509.ts) then reads exactly what a real chain would give it. The root is
 * injected through `env.APPLE_ROOT_CERTIFICATES`, so the code under test runs its real checks
 * against a root the test controls.
 */

const OID = {
  ecdsaSha256: '1.2.840.10045.4.3.2',
  ecdsaSha384: '1.2.840.10045.4.3.3',
  commonName: '2.5.4.3',
  appleLeaf: '1.2.840.113635.100.6.11.1',
  appleIntermediate: '1.2.840.113635.100.6.2.1',
};

function concat(...parts: Uint8Array[]): Uint8Array {
  const out = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
}

function tlv(tag: number, ...content: Uint8Array[]): Uint8Array {
  const body = concat(...content);
  const length = body.length;
  const header = length < 0x80
    ? [length]
    : length < 0x100 ? [0x81, length] : [0x82, length >> 8, length & 0xff];
  return concat(new Uint8Array([tag, ...header]), body);
}

const seq = (...content: Uint8Array[]) => tlv(0x30, ...content);
const set = (...content: Uint8Array[]) => tlv(0x31, ...content);
const explicit = (n: number, ...content: Uint8Array[]) => tlv(0xa0 + n, ...content);

function integer(bytes: Uint8Array): Uint8Array {
  let value = bytes;
  while (value.length > 1 && value[0] === 0 && (value[1]! & 0x80) === 0) {
    value = value.subarray(1);
  }
  return tlv(0x02, value[0]! & 0x80 ? concat(new Uint8Array([0]), value) : value);
}

function oid(dotted: string): Uint8Array {
  const [first, second, ...rest] = dotted.split('.').map(Number) as number[];
  const bytes = [first! * 40 + second!];
  for (const part of rest) {
    const chunk: number[] = [part & 0x7f];
    let value = Math.floor(part / 128);
    while (value > 0) {
      chunk.unshift((value & 0x7f) | 0x80);
      value = Math.floor(value / 128);
    }
    bytes.push(...chunk);
  }
  return tlv(0x06, new Uint8Array(bytes));
}

function time(date: Date): Uint8Array {
  const text = date.toISOString().replace(/[-:T]/g, '').slice(0, 14) + 'Z';
  return tlv(0x18, new TextEncoder().encode(text));
}

function name(commonName: string): Uint8Array {
  return seq(set(seq(oid(OID.commonName), tlv(0x0c, new TextEncoder().encode(commonName)))));
}

/** WebCrypto's raw `r || s` as the DER `SEQUENCE { r, s }` X.509 stores. */
function rawToDer(raw: Uint8Array): Uint8Array {
  const half = raw.length / 2;
  return seq(integer(raw.subarray(0, half)), integer(raw.subarray(half)));
}

export function toBase64(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes));
}

function toBase64Url(bytes: Uint8Array): string {
  return toBase64(bytes).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');
}

export interface TestCertificate {
  readonly der: Uint8Array;
  readonly keys: CryptoKeyPair;
  readonly commonName: string;
}

interface CertificateOptions {
  readonly commonName: string;
  readonly curve: 'P-256' | 'P-384';
  readonly issuer?: TestCertificate;
  readonly extensions?: readonly string[];
  readonly notBefore?: Date;
  readonly notAfter?: Date;
}

export async function makeCertificate(options: CertificateOptions): Promise<TestCertificate> {
  const keys = (await crypto.subtle.generateKey(
    { name: 'ECDSA', namedCurve: options.curve },
    true,
    ['sign', 'verify'],
  )) as CryptoKeyPair;
  const spki = new Uint8Array((await crypto.subtle.exportKey('spki', keys.publicKey)) as ArrayBuffer);

  const signer = options.issuer?.keys ?? keys;
  const signerCurve = (signer.privateKey.algorithm as { namedCurve?: string }).namedCurve;
  const hash = signerCurve === 'P-384' ? 'SHA-384' : 'SHA-256';
  const algorithm = seq(oid(hash === 'SHA-384' ? OID.ecdsaSha384 : OID.ecdsaSha256));

  const extensions = (options.extensions ?? []).map((id) => seq(oid(id), tlv(0x04, tlv(0x05))));
  const tbs = seq(
    explicit(0, integer(new Uint8Array([2]))),
    integer(crypto.getRandomValues(new Uint8Array(8))),
    algorithm,
    name(options.issuer?.commonName ?? options.commonName),
    seq(
      time(options.notBefore ?? new Date(Date.now() - 24 * 60 * 60 * 1000)),
      time(options.notAfter ?? new Date(Date.now() + 365 * 24 * 60 * 60 * 1000)),
    ),
    name(options.commonName),
    spki,
    ...(extensions.length === 0 ? [] : [explicit(3, seq(...extensions))]),
  );

  const signature = new Uint8Array(await crypto.subtle.sign(
    { name: 'ECDSA', hash },
    signer.privateKey,
    tbs as BufferSource,
  ));
  const der = seq(tbs, algorithm, tlv(0x03, new Uint8Array([0]), rawToDer(signature)));
  return { der, keys, commonName: options.commonName };
}

export interface TestChain {
  readonly root: TestCertificate;
  readonly intermediate: TestCertificate;
  readonly leaf: TestCertificate;
}

/** A chain shaped like Apple's. `leafExtensions` lets a test drop the App Store marker. */
export async function makeAppleChain(
  options: { leafExtensions?: readonly string[]; leafNotAfter?: Date } = {},
): Promise<TestChain> {
  const root = await makeCertificate({ commonName: 'Test Root CA - G3', curve: 'P-384' });
  const intermediate = await makeCertificate({
    commonName: 'Test WWDR CA - G6',
    curve: 'P-384',
    issuer: root,
    extensions: [OID.appleIntermediate],
  });
  const leaf = await makeCertificate({
    commonName: 'Test StoreKit Signing',
    curve: 'P-256',
    issuer: intermediate,
    extensions: options.leafExtensions ?? [OID.appleLeaf],
    notAfter: options.leafNotAfter,
  });
  return { root, intermediate, leaf };
}

/**
 * Signs `payload` as Apple does: ES256 with the leaf, the chain in `x5c`. `x5c` replaces the
 * certificates sent, for a test that presents a chain other than the one that signed.
 */
export async function signJws(chain: TestChain, payload: unknown, x5c?: readonly Uint8Array[]): Promise<string> {
  const encode = (value: unknown) => toBase64Url(new TextEncoder().encode(JSON.stringify(value)));
  const header = encode({
    alg: 'ES256',
    x5c: (x5c ?? [chain.leaf.der, chain.intermediate.der, chain.root.der]).map(toBase64),
  });
  const body = encode(payload);
  const signature = new Uint8Array(await crypto.subtle.sign(
    { name: 'ECDSA', hash: 'SHA-256' },
    chain.leaf.keys.privateKey,
    new TextEncoder().encode(`${header}.${body}`) as BufferSource,
  ));
  return `${header}.${body}.${toBase64Url(signature)}`;
}

export const APPLE_OIDS = OID;
