import { authenticate } from './auth';
import { requireFileEntitlement } from './sync';
import { ApiError, SYNC_BODY_LIMIT, json, readJsonObject } from './http';
import { canonicalUtc, isCanonicalUtc } from './time';
import type { Env } from './env';

/**
 * Attachment sync (docs/CLOUD_SYNC.md §5, Phase 7).
 *
 * Two things travel, and they are deliberately separate. Metadata — the filename, the day it
 * belongs to, the content hash — is one more sealed payload and rides this module's push together
 * with the notes' pull. The bytes go straight to R2 under a blinded key (§5.4), because a 20MB
 * image has no business inside a D1 row.
 *
 * Where the paywall sits, and why it is not simply "every route here":
 *
 * - **Uploading and downloading bytes needs a subscription.** That is the paid feature.
 * - **Deleting does not.** A delete that cannot be sent is a delete that never reaches the user's
 *   other devices, and the attachment reappears there forever. Refusing a tombstone would turn a
 *   lapsed subscription into data corruption, so tombstones are always accepted.
 * - **Pulling metadata does not**, and it is `/v1/sync/pull` that carries it. There is one cursor
 *   over one `change_log`; a second, separately-gated pull would let the note cursor step past file
 *   rows and silently lose them. See sync.ts.
 *
 * The pull returns no blinded key, though the push sends one and the row stores it. The client
 * derives the key from the content hash inside the payload it has just decrypted, so echoing it
 * back would add a plaintext field that buys nothing — and deriving it means a server that
 * substituted one file's payload for another's cannot also point the client at matching bytes.
 *
 * So a lapsed account keeps syncing text, keeps propagating its deletions, learns that a file
 * exists — and cannot move a byte of it. Nothing is deleted by lapsing, here or anywhere.
 */

/** Matches the client's `BlindAssetKey`: hex of HMAC-SHA256, so exactly 64 lowercase hex digits. */
const BLINDED_KEY = /^[0-9a-f]{64}$/;

/** A v1 AES-GCM envelope, as sync.ts defines it for notes. */
const ENVELOPE = /^v1\.[A-Za-z0-9_-]{16}\.[A-Za-z0-9_-]{22,}$/;

const MAX_PAYLOAD_CHARS = 64 * 1024;
const MAX_ITEMS_PER_PUSH = 500;

/**
 * The largest single attachment, as ciphertext. Matches the client's `FileCapturePolicy` cap with
 * room for the nonce and tag; R2 would take far more, but an app that accepts a 2GB upload from a
 * bug is an app that fills someone's quota with it.
 */
export const MAX_ASSET_BYTES = 64 * 1024 * 1024;

interface IncomingFile {
  id: string;
  payload: string;
  blinded_key: string;
  stored_bytes: number;
  updated_utc: string;
}

interface IncomingTombstone {
  id: string;
  deleted_utc: string;
}

/** How many new references a push adds to one asset, and how large that asset is. */
interface Gain {
  bytes: number;
  count: number;
}

interface FileRow {
  id: string;
  updated_utc: string;
  blinded_key: string | null;
}

function requireId(value: unknown, field: string): string {
  if (typeof value !== 'string' || !/^[0-9a-f-]{36}$/i.test(value)) {
    throw new ApiError('bad_request', `Field '${field}' must be a uuid.`);
  }
  return value.toLowerCase();
}

function requireTimestamp(value: unknown, field: string): string {
  if (!isCanonicalUtc(value)) {
    throw new ApiError('bad_request', `Field '${field}' must be yyyy-MM-ddTHH:mm:ss.fffffffZ.`);
  }
  return value;
}

function requireBlindedKey(value: unknown, field: string): string {
  if (typeof value !== 'string' || !BLINDED_KEY.test(value)) {
    throw new ApiError('bad_request', `Field '${field}' must be 64 lowercase hex digits.`);
  }
  return value;
}

function requireStoredBytes(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0 || value > MAX_ASSET_BYTES) {
    throw new ApiError('bad_request', `Field 'stored_bytes' must be 0..${MAX_ASSET_BYTES}.`);
  }
  return value;
}

function readArray(body: Record<string, unknown>, field: string): unknown[] {
  const value = body[field];
  if (value === undefined) {
    return [];
  }
  if (!Array.isArray(value)) {
    throw new ApiError('bad_request', `Field '${field}' must be an array.`);
  }
  if (value.length > MAX_ITEMS_PER_PUSH) {
    throw new ApiError('bad_request', `Field '${field}' holds more than ${MAX_ITEMS_PER_PUSH} items.`);
  }
  return value;
}

function asObject(item: unknown, field: string): Record<string, unknown> {
  if (typeof item !== 'object' || item === null || Array.isArray(item)) {
    throw new ApiError('bad_request', `Each entry of '${field}' must be an object.`);
  }
  return item as Record<string, unknown>;
}

function parseFiles(body: Record<string, unknown>): IncomingFile[] {
  return readArray(body, 'files').map((entry) => {
    const item = asObject(entry, 'files');
    const payload = item['payload'];
    if (typeof payload !== 'string' || payload.length > MAX_PAYLOAD_CHARS || !ENVELOPE.test(payload)) {
      throw new ApiError('bad_request', "Field 'files[].payload' must be a v1 envelope.");
    }
    return {
      id: requireId(item['id'], 'files[].id'),
      payload,
      blinded_key: requireBlindedKey(item['blinded_key'], 'files[].blinded_key'),
      stored_bytes: requireStoredBytes(item['stored_bytes']),
      updated_utc: requireTimestamp(item['updated_utc'], 'files[].updated_utc'),
    };
  });
}

function parseTombstones(body: Record<string, unknown>): IncomingTombstone[] {
  return readArray(body, 'tombstones').map((entry) => {
    const item = asObject(entry, 'tombstones');
    return {
      id: requireId(item['id'], 'tombstones[].id'),
      deleted_utc: requireTimestamp(item['deleted_utc'], 'tombstones[].deleted_utc'),
    };
  });
}

/** The rows we are about to overwrite, so a changed asset releases the one it used to point at. */
async function readExisting(
  env: Env,
  userId: string,
  ids: readonly string[],
): Promise<Map<string, FileRow>> {
  const known = new Map<string, FileRow>();
  if (ids.length === 0) {
    return known;
  }

  const rows = await env.DB.batch<FileRow>(
    ids.map((id) =>
      env.DB.prepare(
        'SELECT id, updated_utc, blinded_key FROM files WHERE user_id = ?1 AND id = ?2',
      ).bind(userId, id),
    ),
  );

  for (const result of rows) {
    const row = result.results?.[0];
    if (row !== undefined) {
      known.set(row.id, row);
    }
  }

  return known;
}

/**
 * Applies the reference changes a push implies, and returns the keys that reached zero.
 *
 * Increment and decrement are separate statements against `assets` rather than a recount over
 * `files`, because a recount would have to scan every row the account owns on every push. The
 * CHECK on `ref_count` is the safety net: a decrement that would go negative fails the batch rather
 * than quietly reclaiming an object that is still in use.
 */
function referenceStatements(
  env: Env,
  userId: string,
  gained: ReadonlyMap<string, Gain>,
  released: readonly string[],
): D1PreparedStatement[] {
  const statements: D1PreparedStatement[] = [];

  for (const [key, gain] of gained) {
    // `gain.count`, not 1: two attachments of identical content share one asset, and a single push
    // can carry both. Counting presence rather than references would leave the refcount one short
    // and reclaim the object while the other file still pointed at it.
    statements.push(
      env.DB.prepare(
        `INSERT INTO assets(user_id, blinded_key, stored_bytes, ref_count)
         VALUES (?1, ?2, ?3, ?4)
         ON CONFLICT(user_id, blinded_key) DO UPDATE SET ref_count = ref_count + ?4`,
      ).bind(userId, key, gain.bytes, gain.count),
    );
  }

  for (const key of released) {
    statements.push(
      env.DB.prepare(
        `UPDATE assets SET ref_count = MAX(0, ref_count - 1)
          WHERE user_id = ?1 AND blinded_key = ?2`,
      ).bind(userId, key),
    );
  }

  return statements;
}

/**
 * Deletes the R2 objects nothing points at any more, and forgets their rows.
 *
 * Done after the batch commits, never inside it: R2 has no part in a D1 transaction, so deleting
 * first would be a lost object if the batch then failed. This order can only leak an object, which
 * a later delete of the same key tidies up, and a leaked object is recoverable where a lost one is
 * not.
 */
async function reclaim(env: Env, userId: string, candidates: readonly string[]): Promise<void> {
  if (candidates.length === 0 || env.ASSET_BUCKET === undefined) {
    return;
  }

  const unique = [...new Set(candidates)];
  const rows = await env.DB.batch<{ blinded_key: string }>(
    unique.map((key) =>
      env.DB.prepare(
        'SELECT blinded_key FROM assets WHERE user_id = ?1 AND blinded_key = ?2 AND ref_count = 0',
      ).bind(userId, key),
    ),
  );

  const orphans = rows.flatMap((result) => result.results ?? []).map((row) => row.blinded_key);
  if (orphans.length === 0) {
    return;
  }

  await env.ASSET_BUCKET.delete(orphans.map((key) => objectKey(userId, key)));
  await env.DB.batch(
    orphans.map((key) =>
      env.DB.prepare(
        'DELETE FROM assets WHERE user_id = ?1 AND blinded_key = ?2 AND ref_count = 0',
      ).bind(userId, key),
    ),
  );
}

/** Namespaced by account so one user's blinded key can never address another's object. */
function objectKey(userId: string, blindedKey: string): string {
  return `${userId}/${blindedKey}`;
}

export async function push(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  const body = await readJsonObject(request, SYNC_BODY_LIMIT);
  const files = parseFiles(body);
  const tombstones = parseTombstones(body);

  // Upserts are the paid half; tombstones are not (see the header). Checked once, and only when
  // there is something to refuse, so a delete-only push costs no entitlement lookup.
  let blocked = false;
  if (files.length > 0) {
    blocked = !(await hasFileEntitlement(env, user.id, now));
  }
  const upserts = blocked ? [] : files;

  const existing = await readExisting(env, user.id, [
    ...upserts.map((file) => file.id),
    ...tombstones.map((tombstone) => tombstone.id),
  ]);

  const nowUtc = canonicalUtc(now);
  const statements: D1PreparedStatement[] = [];
  const gained = new Map<string, Gain>();
  const released: string[] = [];
  const acceptedFiles: string[] = [];
  const rejectedFiles: string[] = [];
  const acceptedTombstones: string[] = [];
  const rejectedTombstones: string[] = [];

  for (const file of upserts) {
    const stored = existing.get(file.id);
    if (stored !== undefined && stored.updated_utc >= file.updated_utc) {
      rejectedFiles.push(file.id);
      continue;
    }

    if (stored?.blinded_key != null && stored.blinded_key !== file.blinded_key) {
      released.push(stored.blinded_key);
    }
    if (stored?.blinded_key !== file.blinded_key) {
      const gain = gained.get(file.blinded_key);
      gained.set(
        file.blinded_key,
        gain === undefined ? { bytes: file.stored_bytes, count: 1 } : { ...gain, count: gain.count + 1 },
      );
    }

    statements.push(
      env.DB.prepare(
        `INSERT INTO files(user_id, id, payload, blinded_key, stored_bytes, updated_utc, deleted_utc)
         VALUES (?1, ?2, ?3, ?4, ?5, ?6, NULL)
         ON CONFLICT(user_id, id) DO UPDATE SET
             payload = excluded.payload,
             blinded_key = excluded.blinded_key,
             stored_bytes = excluded.stored_bytes,
             updated_utc = excluded.updated_utc,
             deleted_utc = NULL`,
      ).bind(user.id, file.id, file.payload, file.blinded_key, file.stored_bytes, file.updated_utc),
    );
    statements.push(logChange(env, user.id, file.id, nowUtc));
    acceptedFiles.push(file.id);
  }

  for (const tombstone of tombstones) {
    const stored = existing.get(tombstone.id);
    if (stored !== undefined && stored.updated_utc > tombstone.deleted_utc) {
      rejectedTombstones.push(tombstone.id);
      continue;
    }

    if (stored?.blinded_key != null) {
      released.push(stored.blinded_key);
    }

    // Recorded even when no row exists: the delete has to be pullable by the other devices, and
    // whichever device pushed the file may not have got there yet.
    statements.push(
      env.DB.prepare(
        `INSERT INTO files(user_id, id, payload, blinded_key, stored_bytes, updated_utc, deleted_utc)
         VALUES (?1, ?2, NULL, NULL, 0, ?3, ?3)
         ON CONFLICT(user_id, id) DO UPDATE SET
             payload = NULL,
             blinded_key = NULL,
             stored_bytes = 0,
             updated_utc = excluded.updated_utc,
             deleted_utc = excluded.deleted_utc`,
      ).bind(user.id, tombstone.id, tombstone.deleted_utc),
    );
    statements.push(logChange(env, user.id, tombstone.id, nowUtc));
    acceptedTombstones.push(tombstone.id);
  }

  statements.push(...referenceStatements(env, user.id, gained, released));

  if (statements.length > 0) {
    await env.DB.batch(statements);
    await reclaim(env, user.id, released);
  }

  return json({
    accepted_files: acceptedFiles,
    rejected_files: rejectedFiles,
    accepted_tombstones: acceptedTombstones,
    rejected_tombstones: rejectedTombstones,
    // Distinct from `rejected_files`, and the client must treat it differently: a rejection is
    // settled and the queue entry goes, whereas this means "keep it, and try again once there is a
    // subscription". Conflating the two would drop the upload silently.
    files_blocked: blocked,
    server_utc: canonicalUtc(now),
  });
}

function logChange(env: Env, userId: string, id: string, nowUtc: string): D1PreparedStatement {
  return env.DB.prepare(
    `INSERT INTO change_log(user_id, entity, entity_id, written_utc) VALUES (?1, 'file', ?2, ?3)`,
  ).bind(userId, id, nowUtc);
}

/** The entitlement question as a boolean, for the one caller that answers it without a 402. */
async function hasFileEntitlement(env: Env, userId: string, now: Date): Promise<boolean> {
  try {
    await requireFileEntitlement(env, userId, now);
    return true;
  } catch (error) {
    if (error instanceof ApiError && error.code === 'subscription_required') {
      return false;
    }
    throw error;
  }
}
