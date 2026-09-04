import { authenticate } from './auth';
import { resolve as resolveEntitlement } from './entitlement';
import { ApiError, SYNC_BODY_LIMIT, json, readJsonObject } from './http';
import { canonicalUtc, isCanonicalUtc } from './time';
import type { Env } from './env';

/**
 * Push and pull for notes. Every payload arriving here is already ciphertext the Worker cannot open;
 * the only plaintext it handles is the timestamp it needs for last-write-wins and the random id.
 */

/** A v1 AES-GCM envelope: 12-byte nonce, then ciphertext and tag. Length is content-dependent. */
const ENVELOPE = /^v1\.[A-Za-z0-9_-]{16}\.[A-Za-z0-9_-]{22,}$/;

/** D1 rows are capped well below this; a note larger than it is a bug, not a long note. */
const MAX_PAYLOAD_CHARS = 512 * 1024;
const MAX_ITEMS_PER_PUSH = 500;
const MAX_PULL_LIMIT = 500;
const DEFAULT_PULL_LIMIT = 200;

interface NoteRow {
  id: string;
  updated_utc: string;
}

interface IncomingNote {
  id: string;
  payload: string;
  updated_utc: string;
}

interface IncomingTombstone {
  id: string;
  deleted_utc: string;
}

function requireId(value: unknown, field: string): string {
  // Client ids are uuids. Pinning the shape keeps junk out of the primary key and out of the AAD the
  // client will later authenticate against.
  if (typeof value !== 'string' || !/^[0-9a-f-]{36}$/i.test(value)) {
    throw new ApiError('bad_request', `Field '${field}' must be a uuid.`);
  }
  return value.toLowerCase();
}

function requireEnvelope(value: unknown): string {
  if (typeof value !== 'string' || value.length > MAX_PAYLOAD_CHARS || !ENVELOPE.test(value)) {
    throw new ApiError('bad_request', "Field 'payload' must be a v1 envelope of reasonable size.");
  }
  return value;
}

function requireTimestamp(value: unknown, field: string): string {
  if (!isCanonicalUtc(value)) {
    throw new ApiError('bad_request', `Field '${field}' must be yyyy-MM-ddTHH:mm:ss.fffffffZ.`);
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

function parseNotes(body: Record<string, unknown>): IncomingNote[] {
  return readArray(body, 'notes').map((raw) => {
    if (raw === null || typeof raw !== 'object') {
      throw new ApiError('bad_request', "Each entry in 'notes' must be an object.");
    }
    const item = raw as Record<string, unknown>;
    return {
      id: requireId(item['id'], 'notes[].id'),
      payload: requireEnvelope(item['payload']),
      updated_utc: requireTimestamp(item['updated_utc'], 'notes[].updated_utc'),
    };
  });
}

function parseTombstones(body: Record<string, unknown>): IncomingTombstone[] {
  return readArray(body, 'tombstones').map((raw) => {
    if (raw === null || typeof raw !== 'object') {
      throw new ApiError('bad_request', "Each entry in 'tombstones' must be an object.");
    }
    const item = raw as Record<string, unknown>;
    // Only notes exist server-side so far; a 'file' tombstone would have nowhere to land, and
    // silently accepting it would report success for a delete that never happened.
    const entity = item['entity'] ?? 'note';
    if (entity !== 'note') {
      throw new ApiError('bad_request', "Only note tombstones are supported yet.");
    }
    return {
      id: requireId(item['id'], 'tombstones[].id'),
      deleted_utc: requireTimestamp(item['deleted_utc'], 'tombstones[].deleted_utc'),
    };
  });
}

async function readExisting(
  env: Env,
  userId: string,
  ids: readonly string[],
): Promise<Map<string, string>> {
  const known = new Map<string, string>();
  if (ids.length === 0) {
    return known;
  }

  // One statement per id would be a round trip per note; batch() is a single transaction.
  const rows = await env.DB.batch<NoteRow>(
    ids.map((id) =>
      env.DB.prepare('SELECT id, updated_utc FROM notes WHERE user_id = ?1 AND id = ?2').bind(
        userId,
        id,
      ),
    ),
  );

  for (const result of rows) {
    const row = result.results?.[0];
    if (row !== undefined) {
      known.set(row.id, row.updated_utc);
    }
  }

  return known;
}


/**
 * Refuses the request when the account is not entitled to sync.
 *
 * A lapse stops sync and nothing else: every row this Worker already holds stays exactly where it
 * is (docs/CLOUD_SYNC.md §14). Resubscribing resumes from the same cursor, and in the meantime the
 * user's own PC is unaffected — the local database is the source of truth and needs no account.
 */
async function requireEntitlement(env: Env, userId: string, now: Date): Promise<void> {
  const entitlement = await resolveEntitlement(env, userId, now);
  if (!entitlement.canSync) {
    throw new ApiError(
      'subscription_required',
      'Cloud sync needs an active subscription. Your notes on this PC are unaffected, and the copy '
        + 'already synced is kept.',
    );
  }
}

export async function push(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  await requireEntitlement(env, user.id, now);
  const body = await readJsonObject(request, SYNC_BODY_LIMIT);
  const notes = parseNotes(body);
  const tombstones = parseTombstones(body);

  const existing = await readExisting(env, user.id, [
    ...notes.map((note) => note.id),
    ...tombstones.map((tombstone) => tombstone.id),
  ]);

  const nowUtc = canonicalUtc(now);
  const statements: D1PreparedStatement[] = [];
  const acceptedNotes: string[] = [];
  const rejectedNotes: string[] = [];
  const acceptedTombstones: string[] = [];
  const rejectedTombstones: string[] = [];

  for (const note of notes) {
    const stored = existing.get(note.id);
    // Canonical timestamps compare correctly as strings, which is the whole point of the format.
    // Equal is a reject, not an accept: re-storing an identical version would append a change_log
    // row and echo back to every device for no reason.
    if (stored !== undefined && stored >= note.updated_utc) {
      rejectedNotes.push(note.id);
      continue;
    }

    statements.push(
      env.DB.prepare(
        `INSERT INTO notes(user_id, id, payload, updated_utc, deleted_utc)
         VALUES (?1, ?2, ?3, ?4, NULL)
         ON CONFLICT(user_id, id) DO UPDATE SET
             payload = excluded.payload,
             updated_utc = excluded.updated_utc,
             deleted_utc = NULL`,
      ).bind(user.id, note.id, note.payload, note.updated_utc),
    );
    statements.push(
      env.DB.prepare(
        `INSERT INTO change_log(user_id, entity, entity_id, written_utc)
         VALUES (?1, 'note', ?2, ?3)`,
      ).bind(user.id, note.id, nowUtc),
    );
    acceptedNotes.push(note.id);
  }

  for (const tombstone of tombstones) {
    const stored = existing.get(tombstone.id);
    if (stored !== undefined && stored >= tombstone.deleted_utc) {
      rejectedTombstones.push(tombstone.id);
      continue;
    }

    // A delete is stored as the row with its payload dropped, and updated_utc set to the deletion
    // instant, so one comparison orders deletes and edits against each other.
    statements.push(
      env.DB.prepare(
        `INSERT INTO notes(user_id, id, payload, updated_utc, deleted_utc)
         VALUES (?1, ?2, NULL, ?3, ?3)
         ON CONFLICT(user_id, id) DO UPDATE SET
             payload = NULL,
             updated_utc = excluded.updated_utc,
             deleted_utc = excluded.deleted_utc`,
      ).bind(user.id, tombstone.id, tombstone.deleted_utc),
    );
    statements.push(
      env.DB.prepare(
        `INSERT INTO change_log(user_id, entity, entity_id, written_utc)
         VALUES (?1, 'note', ?2, ?3)`,
      ).bind(user.id, tombstone.id, nowUtc),
    );
    acceptedTombstones.push(tombstone.id);
  }

  if (statements.length > 0) {
    await env.DB.batch(statements);
  }

  return json({
    accepted_notes: acceptedNotes,
    rejected_notes: rejectedNotes,
    accepted_tombstones: acceptedTombstones,
    rejected_tombstones: rejectedTombstones,
    cursor: await readCursor(env, user.id),
    server_utc: nowUtc,
  });
}

async function readCursor(env: Env, userId: string): Promise<number> {
  const row = await env.DB.prepare(
    'SELECT COALESCE(MAX(seq), 0) AS cursor FROM change_log WHERE user_id = ?1',
  )
    .bind(userId)
    .first<{ cursor: number }>();
  return row?.cursor ?? 0;
}

interface ChangeRow {
  seq: number;
  entity_id: string;
  payload: string | null;
  updated_utc: string;
  deleted_utc: string | null;
}

export async function pull(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  await requireEntitlement(env, user.id, now);
  const url = new URL(request.url);

  const since = Number(url.searchParams.get('since') ?? '0');
  if (!Number.isSafeInteger(since) || since < 0) {
    throw new ApiError('bad_request', "Query 'since' must be a non-negative integer.");
  }

  const requested = Number(url.searchParams.get('limit') ?? String(DEFAULT_PULL_LIMIT));
  const limit = Number.isSafeInteger(requested) && requested > 0
    ? Math.min(requested, MAX_PULL_LIMIT)
    : DEFAULT_PULL_LIMIT;

  // Group by entity so a note edited twenty times since the cursor costs one row in the page, and
  // order by the highest seq per entity so the page boundary stays a clean cursor: every group with
  // a max seq at or below the returned cursor has been delivered.
  const { results } = await env.DB.prepare(
    `SELECT MAX(cl.seq) AS seq, cl.entity_id, n.payload, n.updated_utc, n.deleted_utc
       FROM change_log cl
       JOIN notes n ON n.user_id = cl.user_id AND n.id = cl.entity_id
      WHERE cl.user_id = ?1 AND cl.seq > ?2 AND cl.entity = 'note'
      GROUP BY cl.entity_id
      ORDER BY seq
      LIMIT ?3`,
  )
    .bind(user.id, since, limit)
    .all<ChangeRow>();

  const changes = results.map((row) => ({
    seq: row.seq,
    entity: 'note' as const,
    id: row.entity_id,
    payload: row.payload,
    updated_utc: row.updated_utc,
    deleted_utc: row.deleted_utc,
  }));

  return json({
    changes,
    // Staying put on an empty page matters: advancing to the global max would skip changes a
    // concurrent push is still writing.
    cursor: changes.length > 0 ? changes[changes.length - 1]!.seq : since,
    has_more: changes.length === limit,
    server_utc: canonicalUtc(now),
  });
}
