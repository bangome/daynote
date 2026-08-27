-- Cloud sync bookkeeping (docs/CLOUD_SYNC.md §6). Adds no content columns: everything here records
-- what still needs sending and how far we have received, never note text.
--
-- Nothing in this migration turns sync on. A database that reaches version 4 and never signs in
-- keeps a populated outbox that is simply never drained.

-- What still needs pushing. One row per entity, replaced on each further edit, so a note edited
-- fifty times between syncs is pushed once.
--
-- Deliberately NOT a `sync_dirty` column on `notes`: a column has to be set by every writer and is
-- silently forgotten by the next one added, and a trigger maintaining it would have to UPDATE the
-- table it fires on. Writing to a separate table keeps the triggers below non-recursive and makes
-- the bookkeeping impossible for a writer to skip — including the MCP server, which shares this
-- database.
CREATE TABLE sync_outbox (
    entity     TEXT NOT NULL CHECK (entity IN ('note', 'file')),
    entity_id  TEXT NOT NULL,
    queued_utc TEXT NOT NULL,
    PRIMARY KEY (entity, entity_id)
) WITHOUT ROWID;

-- A delete has to outlive the row, or the next pull from another device resurrects it.
CREATE TABLE sync_tombstones (
    entity      TEXT NOT NULL CHECK (entity IN ('note', 'file')),
    entity_id   TEXT NOT NULL,
    deleted_utc TEXT NOT NULL,
    PRIMARY KEY (entity, entity_id)
) WITHOUT ROWID;

-- Single row. Holds no key material: tokens and the data key live in credentials.dat under DPAPI,
-- because this database is copied verbatim into the plaintext backup .zip.
CREATE TABLE sync_state (
    id             INTEGER PRIMARY KEY CHECK (id = 1),
    user_id        TEXT,
    server_cursor  INTEGER NOT NULL DEFAULT 0 CHECK (server_cursor >= 0),
    dek_generation INTEGER NOT NULL DEFAULT 0 CHECK (dek_generation >= 0),
    locked         INTEGER NOT NULL DEFAULT 0 CHECK (locked IN (0, 1)),
    last_sync_utc  TEXT
);

INSERT INTO sync_state(id) VALUES (1);

-- Attachment bytes waiting to move. Keyed by the local plaintext content hash; the blinded R2 key is
-- computed at upload time and never stored here (docs/CLOUD_SYNC.md §5.4).
CREATE TABLE sync_asset_queue (
    asset_hash TEXT PRIMARY KEY,
    direction  TEXT NOT NULL CHECK (direction IN ('up', 'down')),
    attempts   INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error TEXT
) WITHOUT ROWID;

-- Triggers. `queued_utc` is taken from the row's own app-clock timestamp rather than from
-- strftime('now'), so enqueueing needs no clock of its own and stays consistent with the row.
--
-- Tombstone timestamps are the one place SQL reads the clock, because a delete has no row timestamp
-- of its own and must be able to beat an earlier edit under last-write-wins. The format matches
-- DateTimeOffset.ToString("O") exactly -- 7 fractional digits and a +00:00 offset -- so every _utc
-- column in this database stays in one format (see docs/CLOUD_SYNC.md §7.3 for why mixing two
-- formats is a correctness bug, not a cosmetic one).

CREATE TRIGGER sync_notes_ai AFTER INSERT ON notes BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc) VALUES ('note', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
    -- A note reappearing under an id we buried is a resurrection, so the tombstone must go.
    DELETE FROM sync_tombstones WHERE entity = 'note' AND entity_id = new.id;
END;

CREATE TRIGGER sync_notes_au AFTER UPDATE ON notes BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc) VALUES ('note', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
END;

CREATE TRIGGER sync_notes_ad AFTER DELETE ON notes BEGIN
    DELETE FROM sync_outbox WHERE entity = 'note' AND entity_id = old.id;
    -- OR IGNORE so a caller that recorded its own tombstone first keeps its timestamp.
    INSERT OR IGNORE INTO sync_tombstones(entity, entity_id, deleted_utc)
    VALUES ('note', old.id, strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00');
END;

-- Tag edits already bump notes.updated_utc, so the note triggers above cover them and no trigger on
-- note_tags is needed. SqliteSyncStoreTests pins that behaviour: if a future writer stops bumping
-- the note, the test fails here rather than the change silently never syncing.

CREATE TRIGGER sync_files_ai AFTER INSERT ON day_files BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc) VALUES ('file', new.id, new.created_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
    DELETE FROM sync_tombstones WHERE entity = 'file' AND entity_id = new.id;
END;

CREATE TRIGGER sync_files_ad AFTER DELETE ON day_files BEGIN
    DELETE FROM sync_outbox WHERE entity = 'file' AND entity_id = old.id;
    INSERT OR IGNORE INTO sync_tombstones(entity, entity_id, deleted_utc)
    VALUES ('file', old.id, strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00');
END;
