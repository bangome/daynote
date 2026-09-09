import { authenticate } from './auth';
import { requireFileEntitlement } from './sync';
import { MAX_ASSET_BYTES } from './files';
import { ApiError, json } from './http';
import { canonicalUtc } from './time';
import type { Env } from './env';

/**
 * The attachment bytes themselves (docs/CLOUD_SYNC.md §5.4).
 *
 * Opaque both ways: the body is `nonce || AES-256-GCM(k_asset, plaintext)`, sealed on the device
 * under a key derived from the account's data key, and the object name is a blinded key the server
 * cannot invert. So the Worker can say how many bytes an account is storing — it has to, to enforce
 * a quota — and nothing about what they are.
 *
 * Both directions need a subscription. This is the paid feature; the free tier is text.
 */

const BLINDED_KEY = /^[0-9a-f]{64}$/;

/** How long an uploaded-but-unreferenced object is left alone before it counts as abandoned. */
const ORPHAN_GRACE_MS = 24 * 60 * 60 * 1000;

/** Bounds the opportunistic sweep so one request cannot turn into a long scan. */
const ORPHAN_SWEEP_LIMIT = 20;

function objectKey(userId: string, blindedKey: string): string {
  return `${userId}/${blindedKey}`;
}

/** Reads the blinded key out of `/v1/assets/<key>`, or null when the path is not one of ours. */
export function blindedKeyFrom(pathname: string): string | null {
  const match = /^\/v1\/assets\/([^/]+)$/.exec(pathname);
  const key = match?.[1];
  return key !== undefined && BLINDED_KEY.test(key) ? key : null;
}

async function usedBytes(env: Env, userId: string): Promise<number> {
  const row = await env.DB.prepare(
    'SELECT COALESCE(SUM(stored_bytes), 0) AS used FROM assets WHERE user_id = ?1',
  )
    .bind(userId)
    .first<{ used: number }>();
  return row?.used ?? 0;
}

async function quotaBytes(env: Env, userId: string): Promise<number> {
  const row = await env.DB.prepare('SELECT quota_bytes FROM users WHERE id = ?1')
    .bind(userId)
    .first<{ quota_bytes: number }>();
  return row?.quota_bytes ?? 0;
}

function bucket(env: Env): R2Bucket {
  if (env.ASSET_BUCKET === undefined) {
    throw new ApiError('server_error', 'Attachment storage is not configured.');
  }
  return env.ASSET_BUCKET;
}

/**
 * Stores one attachment.
 *
 * Idempotent by construction: the name is a hash of the content, so a repeat upload writes the same
 * bytes to the same key. That is what makes a retry after a dropped connection safe, and it is why
 * the quota check excludes a key the account already has.
 */
export async function put(request: Request, env: Env, now: Date, key: string): Promise<Response> {
  const user = await authenticate(request, env, now);
  await requireFileEntitlement(env, user.id, now);

  const declared = Number(request.headers.get('content-length') ?? 'NaN');
  if (!Number.isSafeInteger(declared) || declared <= 0 || declared > MAX_ASSET_BYTES) {
    throw new ApiError('payload_too_large', `An attachment must be 1..${MAX_ASSET_BYTES} bytes.`);
  }

  const existing = await env.DB.prepare(
    'SELECT stored_bytes, uploaded_utc FROM assets WHERE user_id = ?1 AND blinded_key = ?2',
  )
    .bind(user.id, key)
    .first<{ stored_bytes: number; uploaded_utc: string | null }>();

  // Re-uploading a key the account already holds replaces it, so only the difference is new.
  const additional = declared - (existing?.stored_bytes ?? 0);
  if (additional > 0 && (await usedBytes(env, user.id)) + additional > (await quotaBytes(env, user.id))) {
    throw new ApiError(
      'payload_too_large',
      'This attachment would exceed the storage included with your account.',
    );
  }

  const body = await request.arrayBuffer();
  if (body.byteLength !== declared) {
    throw new ApiError('bad_request', 'The body length disagrees with Content-Length.');
  }

  await bucket(env).put(objectKey(user.id, key), body);

  // Written after the object lands, never before: a row claiming an upload that did not happen
  // would have the client believe the bytes are there and skip re-sending them.
  await env.DB.prepare(
    `INSERT INTO assets(user_id, blinded_key, stored_bytes, ref_count, uploaded_utc)
     VALUES (?1, ?2, ?3, 0, ?4)
     ON CONFLICT(user_id, blinded_key) DO UPDATE SET
         stored_bytes = excluded.stored_bytes,
         uploaded_utc = excluded.uploaded_utc`,
  )
    .bind(user.id, key, body.byteLength, canonicalUtc(now))
    .run();

  await sweepOrphans(env, user.id, now);

  return json({ blinded_key: key, stored_bytes: body.byteLength, server_utc: canonicalUtc(now) });
}

/** Hands the sealed bytes back. A 404 here is ordinary: the metadata can arrive before the object. */
export async function get(request: Request, env: Env, now: Date, key: string): Promise<Response> {
  const user = await authenticate(request, env, now);
  await requireFileEntitlement(env, user.id, now);

  const object = await bucket(env).get(objectKey(user.id, key));
  if (object === null) {
    throw new ApiError('not_found', 'No attachment is stored under that key.');
  }

  return new Response(object.body, {
    headers: {
      'content-type': 'application/octet-stream',
      'content-length': String(object.size),
      'cache-control': 'no-store',
      'x-content-type-options': 'nosniff',
    },
  });
}

/**
 * Drops objects that were uploaded and then never referenced by a file row.
 *
 * This happens for one reason: the client uploads the bytes and then fails, is closed, or goes
 * offline before pushing the metadata that would claim them. Without this they would sit in the
 * quota forever, and the user would have no way to see or free them.
 *
 * The grace period is what makes it safe. An upload is only abandoned if nothing has claimed it a
 * day later — well beyond the gap between the two calls in a working client, and beyond any retry.
 */
async function sweepOrphans(env: Env, userId: string, now: Date): Promise<void> {
  const before = canonicalUtc(new Date(now.getTime() - ORPHAN_GRACE_MS));
  const { results } = await env.DB.prepare(
    `SELECT blinded_key FROM assets
      WHERE user_id = ?1 AND ref_count = 0 AND uploaded_utc IS NOT NULL AND uploaded_utc < ?2
      LIMIT ?3`,
  )
    .bind(userId, before, ORPHAN_SWEEP_LIMIT)
    .all<{ blinded_key: string }>();

  if (results.length === 0) {
    return;
  }

  const keys = results.map((row) => row.blinded_key);
  await bucket(env).delete(keys.map((key) => objectKey(userId, key)));
  await env.DB.batch(
    keys.map((key) =>
      env.DB.prepare(
        'DELETE FROM assets WHERE user_id = ?1 AND blinded_key = ?2 AND ref_count = 0',
      ).bind(userId, key),
    ),
  );
}
