import { authenticate } from './auth';
import { resolve as resolveEntitlement, type Entitlement } from './entitlement';
import { ApiError, SYNC_BODY_LIMIT, json, readJsonObject } from './http';
import { canonicalUtc, isCanonicalUtc } from './time';
import type { Env } from './env';

/**
 * Push and pull for notes, to-dos, events and their lists. Every payload arriving here is already
 * ciphertext the Worker cannot open; the only plaintext it handles is the timestamp it needs for
 * last-write-wins, the random id, and which kind of thing the id names.
 *
 * All of it is free. The paywall is on attachment *bytes* (assets.ts) and nothing else, so a lapsed
 * account keeps syncing everything that is text — which to-dos and events are.
 */

/** A v1 AES-GCM envelope: 12-byte nonce, then ciphertext and tag. Length is content-dependent. */
const ENVELOPE = /^v1\.[A-Za-z0-9_-]{16}\.[A-Za-z0-9_-]{22,}$/;

/** D1 rows are capped well below this; a note larger than it is a bug, not a long note. */
const MAX_PAYLOAD_CHARS = 512 * 1024;
const MAX_ITEMS_PER_PUSH = 500;
const MAX_PULL_LIMIT = 500;
const DEFAULT_PULL_LIMIT = 200;

/**
 * What /v1/sync/push accepts. Attachments are not here: their metadata carries two plaintext
 * columns of its own and goes to /v1/files/push (files.ts), though it pulls down this same cursor.
 */
type PushEntity = 'note' | 'agenda_item' | 'agenda_list';

/** Which table holds a kind. Both agenda kinds share one (migration 0013). */
const AGENDA: ReadonlySet<string> = new Set<PushEntity>(['agenda_item', 'agenda_list']);

interface StoredRow {
  id: string;
  updated_utc: string;
}

interface IncomingRow {
  entity: PushEntity;
  id: string;
  payload: string;
  updated_utc: string;
}

interface IncomingTombstone {
  entity: PushEntity;
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

function parseRows(
  body: Record<string, unknown>,
  field: string,
  entity: PushEntity,
): IncomingRow[] {
  return readArray(body, field).map((raw) => {
    if (raw === null || typeof raw !== 'object') {
      throw new ApiError('bad_request', `Each entry in '${field}' must be an object.`);
    }
    const item = raw as Record<string, unknown>;
    return {
      entity,
      id: requireId(item['id'], `${field}[].id`),
      payload: requireEnvelope(item['payload']),
      updated_utc: requireTimestamp(item['updated_utc'], `${field}[].updated_utc`),
    };
  });
}

function parseTombstones(body: Record<string, unknown>): IncomingTombstone[] {
  return readArray(body, 'tombstones').map((raw) => {
    if (raw === null || typeof raw !== 'object') {
      throw new ApiError('bad_request', "Each entry in 'tombstones' must be an object.");
    }
    const item = raw as Record<string, unknown>;
    // Absent means 'note': clients that predate to-dos send no entity at all, and a delete they
    // cannot express is a note that comes back from the dead on every other device.
    //
    // A 'file' tombstone is still refused here. It has somewhere to land now, but not in this
    // module: deleting an attachment also releases its R2 reference, which /v1/files/push does and
    // this does not. Accepting it would report success for a delete that leaked the bytes.
    const entity = item['entity'] ?? 'note';
    if (entity !== 'note' && entity !== 'agenda_item' && entity !== 'agenda_list') {
      throw new ApiError(
        'bad_request',
        "Field 'tombstones[].entity' must be 'note', 'agenda_item' or 'agenda_list'.",
      );
    }
    return {
      entity,
      id: requireId(item['id'], 'tombstones[].id'),
      deleted_utc: requireTimestamp(item['deleted_utc'], 'tombstones[].deleted_utc'),
    };
  });
}

/** The key the LWW lookup is done under. An id alone would collide across kinds. */
function refKey(entity: PushEntity, id: string): string {
  return `${entity}/${id}`;
}

async function readExisting(
  env: Env,
  userId: string,
  refs: readonly { entity: PushEntity; id: string }[],
): Promise<Map<string, string>> {
  const known = new Map<string, string>();
  if (refs.length === 0) {
    return known;
  }

  // One statement per id would be a round trip per row; batch() is a single transaction. The same
  // id can appear as both an edit and a delete in one push, so the lookups are deduplicated —
  // otherwise the batch grows to twice the size for no extra answer.
  const unique = new Map<string, { entity: PushEntity; id: string }>();
  for (const ref of refs) {
    unique.set(refKey(ref.entity, ref.id), ref);
  }
  const ordered = [...unique.values()];

  const rows = await env.DB.batch<StoredRow>(
    ordered.map((ref) =>
      AGENDA.has(ref.entity)
        ? env.DB.prepare(
            'SELECT id, updated_utc FROM agenda WHERE user_id = ?1 AND entity = ?2 AND id = ?3',
          ).bind(userId, ref.entity, ref.id)
        : env.DB.prepare('SELECT id, updated_utc FROM notes WHERE user_id = ?1 AND id = ?2').bind(
            userId,
            ref.id,
          ),
    ),
  );

  // Matched back by position, not by the row's own id: `agenda` is keyed on (entity, id) and the
  // returned row carries only the id, so an item and a list sharing one would be indistinguishable.
  rows.forEach((result, index) => {
    const row = result.results?.[0];
    const ref = ordered[index];
    if (row !== undefined && ref !== undefined) {
      known.set(refKey(ref.entity, ref.id), row.updated_utc);
    }
  });

  return known;
}


/**
 * Refuses the request when the account is not on the paid tier.
 *
 * Text sync (notes, to-dos, tags, favorites) is free for every signed-in account and never calls
 * this. It exists for the image and file sync endpoints (docs/CLOUD_SYNC.md §14): a lapse stops
 * those and nothing else. Every row and object this Worker already holds stays exactly where
 * it is; resubscribing resumes from the same cursor, and the user's own PC is unaffected either way.
 *
 * Called by the asset routes (assets.ts) in both directions, and by the file-metadata upsert in
 * files.ts. Deliberately NOT called by tombstones or by the pull: a delete that cannot be sent
 * corrupts the other devices, and metadata shares the notes' cursor. See the header of files.ts.
 */
export async function requireFileEntitlement(env: Env, userId: string, now: Date): Promise<Entitlement> {
  const entitlement = await resolveEntitlement(env, userId, now);
  if (!entitlement.canSyncFiles) {
    throw new ApiError(
      'subscription_required',
      'Syncing images and files needs an active subscription. Your notes keep syncing, nothing on '
        + 'this PC is affected, and the files already synced are kept.',
    );
  }
  return entitlement;
}

export async function push(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  // No entitlement check: text sync is free, and a to-do is text.
  const body = await readJsonObject(request, SYNC_BODY_LIMIT);
  const notes = parseRows(body, 'notes', 'note');
  // Lists ahead of items, and that order is kept all the way into the batch below, so a list gets
  // the lower `seq`. A device pulling from zero then meets the container before the things in it.
  const lists = parseRows(body, 'agenda_lists', 'agenda_list');
  const items = parseRows(body, 'agenda_items', 'agenda_item');
  const tombstones = parseTombstones(body);

  const rows = [...lists, ...items, ...notes];
  const existing = await readExisting(env, user.id, [...rows, ...tombstones]);

  const nowUtc = canonicalUtc(now);
  const statements: D1PreparedStatement[] = [];
  const accepted: Record<PushEntity, string[]> = { note: [], agenda_item: [], agenda_list: [] };
  const rejected: Record<PushEntity, string[]> = { note: [], agenda_item: [], agenda_list: [] };
  const acceptedTombstones: string[] = [];
  const rejectedTombstones: string[] = [];

  for (const row of rows) {
    const stored = existing.get(refKey(row.entity, row.id));
    // Canonical timestamps compare correctly as strings, which is the whole point of the format.
    // Equal is a reject, not an accept: re-storing an identical version would append a change_log
    // row and echo back to every device for no reason.
    if (stored !== undefined && stored >= row.updated_utc) {
      rejected[row.entity].push(row.id);
      continue;
    }

    statements.push(
      AGENDA.has(row.entity)
        ? env.DB.prepare(
            `INSERT INTO agenda(user_id, entity, id, payload, updated_utc, deleted_utc)
             VALUES (?1, ?2, ?3, ?4, ?5, NULL)
             ON CONFLICT(user_id, entity, id) DO UPDATE SET
                 payload = excluded.payload,
                 updated_utc = excluded.updated_utc,
                 deleted_utc = NULL`,
          ).bind(user.id, row.entity, row.id, row.payload, row.updated_utc)
        : env.DB.prepare(
            `INSERT INTO notes(user_id, id, payload, updated_utc, deleted_utc)
             VALUES (?1, ?2, ?3, ?4, NULL)
             ON CONFLICT(user_id, id) DO UPDATE SET
                 payload = excluded.payload,
                 updated_utc = excluded.updated_utc,
                 deleted_utc = NULL`,
          ).bind(user.id, row.id, row.payload, row.updated_utc),
    );
    statements.push(
      env.DB.prepare(
        `INSERT INTO change_log(user_id, entity, entity_id, written_utc)
         VALUES (?1, ?2, ?3, ?4)`,
      ).bind(user.id, row.entity, row.id, nowUtc),
    );
    accepted[row.entity].push(row.id);
  }

  for (const tombstone of tombstones) {
    const stored = existing.get(refKey(tombstone.entity, tombstone.id));
    if (stored !== undefined && stored >= tombstone.deleted_utc) {
      rejectedTombstones.push(tombstone.id);
      continue;
    }

    // A delete is stored as the row with its payload dropped, and updated_utc set to the deletion
    // instant, so one comparison orders deletes and edits against each other.
    statements.push(
      AGENDA.has(tombstone.entity)
        ? env.DB.prepare(
            `INSERT INTO agenda(user_id, entity, id, payload, updated_utc, deleted_utc)
             VALUES (?1, ?2, ?3, NULL, ?4, ?4)
             ON CONFLICT(user_id, entity, id) DO UPDATE SET
                 payload = NULL,
                 updated_utc = excluded.updated_utc,
                 deleted_utc = excluded.deleted_utc`,
          ).bind(user.id, tombstone.entity, tombstone.id, tombstone.deleted_utc)
        : env.DB.prepare(
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
         VALUES (?1, ?2, ?3, ?4)`,
      ).bind(user.id, tombstone.entity, tombstone.id, nowUtc),
    );
    // Returned as bare ids, as they have been since the first release: a uuid names one row across
    // every kind, and the client matches them against the batch it just sent.
    acceptedTombstones.push(tombstone.id);
  }

  if (statements.length > 0) {
    await env.DB.batch(statements);
  }

  return json({
    accepted_notes: accepted.note,
    rejected_notes: rejected.note,
    accepted_agenda_lists: accepted.agenda_list,
    rejected_agenda_lists: rejected.agenda_list,
    accepted_agenda_items: accepted.agenda_item,
    rejected_agenda_items: rejected.agenda_item,
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
  entity: 'note' | 'file' | 'agenda_item' | 'agenda_list';
  entity_id: string;
  payload: string | null;
  updated_utc: string;
  deleted_utc: string | null;
}

export async function pull(request: Request, env: Env, now: Date): Promise<Response> {
  const user = await authenticate(request, env, now);
  // No entitlement check: text sync is free, and file metadata rides the same cursor (below).
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
  //
  // Notes, files and agenda rows come down the same page, ordered by the same sequence, because
  // there is one cursor. A second pull for files would have to advance a cursor of its own or share
  // this one, and sharing it means whichever pull ran first steps the other past its rows. Files
  // are not gated here either: metadata and deletions must keep flowing to a lapsed account, or
  // deleting an attachment on one device would never reach the next. The paywall is on the bytes
  // (assets.ts).
  //
  // The page is not sorted into dependency order and does not need to be. A list is created before
  // anything is put in it, so its first `seq` is the lower one, and the client sorts what it has
  // been handed anyway — lists before items, a series before its overrides (SqliteSyncStore.Agenda).
  const { results } = await env.DB.prepare(
    `SELECT MAX(cl.seq) AS seq, cl.entity, cl.entity_id,
            COALESCE(n.payload, f.payload, a.payload)             AS payload,
            COALESCE(n.updated_utc, f.updated_utc, a.updated_utc) AS updated_utc,
            COALESCE(n.deleted_utc, f.deleted_utc, a.deleted_utc) AS deleted_utc
       FROM change_log cl
       LEFT JOIN notes n
              ON cl.entity = 'note' AND n.user_id = cl.user_id AND n.id = cl.entity_id
       LEFT JOIN files f
              ON cl.entity = 'file' AND f.user_id = cl.user_id AND f.id = cl.entity_id
       LEFT JOIN agenda a
              ON cl.entity IN ('agenda_item', 'agenda_list')
             AND a.user_id = cl.user_id AND a.entity = cl.entity AND a.id = cl.entity_id
      WHERE cl.user_id = ?1 AND cl.seq > ?2
        AND (n.id IS NOT NULL OR f.id IS NOT NULL OR a.id IS NOT NULL)
      GROUP BY cl.entity, cl.entity_id
      ORDER BY seq
      LIMIT ?3`,
  )
    .bind(user.id, since, limit)
    .all<ChangeRow>();

  const changes = results.map((row) => ({
    seq: row.seq,
    entity: row.entity,
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
