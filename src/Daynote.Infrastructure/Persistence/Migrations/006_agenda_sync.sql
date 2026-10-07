-- Sync bookkeeping for to-dos, events and their lists (docs/TODOS.md §9, §12 step 1).
--
-- Still nothing reads the agenda tables. This makes their edits *queue* — the readers, the `@`
-- command and the phone follow in one release, which §12 requires. A database that reaches this
-- version and never signs in keeps a populated outbox that is simply never drained.
--
-- Two new sync entities is the real cost of taking to-dos out of the note body, and it is worth
-- restating: a note rides as one opaque envelope, so anything *inside* it syncs for free. A sibling
-- entity needs its own queue rows, its own last-write-wins rule and its own tombstones.

-- Migration 005 and the first cut of SqliteAgendaStatements wrote `_utc` columns as
-- 'YYYY-MM-DDTHH:MM:SS.fffZ' while every other table in this database holds
-- DateTimeOffset.ToString("O") — 7 fractional digits and a +00:00 offset. That is a correctness
-- bug, not a cosmetic one: AcknowledgePushAsync matches `queued_utc` as an exact string, so rows
-- written in the other format would be queued and never acknowledged. Normalised here, before the
-- triggers below exist, so the rewrite does not queue anything of its own.
UPDATE agenda_lists SET created_utc = substr(created_utc, 1, 23) || '0000+00:00'
 WHERE created_utc LIKE '%Z';
UPDATE agenda_lists SET updated_utc = substr(updated_utc, 1, 23) || '0000+00:00'
 WHERE updated_utc LIKE '%Z';
UPDATE agenda_items SET created_utc = substr(created_utc, 1, 23) || '0000+00:00'
 WHERE created_utc LIKE '%Z';
UPDATE agenda_items SET updated_utc = substr(updated_utc, 1, 23) || '0000+00:00'
 WHERE updated_utc LIKE '%Z';
UPDATE agenda_items SET completed_utc = substr(completed_utc, 1, 23) || '0000+00:00'
 WHERE completed_utc LIKE '%Z';

-- Widening a CHECK means rebuilding the table in SQLite. Migration 005 deliberately left these
-- alone so the rebuild would happen once, here, together with the triggers that need it.
--
-- 004's triggers have to go first and come back afterwards. SQLite re-parses the whole schema
-- after a DROP TABLE and refuses it while any trigger body still names the table, so leaving them
-- in place fails the migration outright rather than quietly working.

DROP TRIGGER sync_notes_ai;
DROP TRIGGER sync_notes_au;
DROP TRIGGER sync_notes_ad;
DROP TRIGGER sync_files_ai;
DROP TRIGGER sync_files_ad;

CREATE TABLE sync_outbox_new (
    entity     TEXT NOT NULL CHECK (entity IN ('note', 'file', 'agenda_item', 'agenda_list')),
    entity_id  TEXT NOT NULL,
    queued_utc TEXT NOT NULL,
    PRIMARY KEY (entity, entity_id)
) WITHOUT ROWID;

INSERT INTO sync_outbox_new(entity, entity_id, queued_utc)
SELECT entity, entity_id, queued_utc FROM sync_outbox;

DROP TABLE sync_outbox;
ALTER TABLE sync_outbox_new RENAME TO sync_outbox;

CREATE TABLE sync_tombstones_new (
    entity      TEXT NOT NULL CHECK (entity IN ('note', 'file', 'agenda_item', 'agenda_list')),
    entity_id   TEXT NOT NULL,
    deleted_utc TEXT NOT NULL,
    PRIMARY KEY (entity, entity_id)
) WITHOUT ROWID;

INSERT INTO sync_tombstones_new(entity, entity_id, deleted_utc)
SELECT entity, entity_id, deleted_utc FROM sync_tombstones;

DROP TABLE sync_tombstones;
ALTER TABLE sync_tombstones_new RENAME TO sync_tombstones;

-- 004's triggers, back verbatim. Their bodies are unchanged: the tables they write to have the
-- same names and the same columns, only a wider CHECK.

CREATE TRIGGER sync_notes_ai AFTER INSERT ON notes BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc) VALUES ('note', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
    DELETE FROM sync_tombstones WHERE entity = 'note' AND entity_id = new.id;
END;

CREATE TRIGGER sync_notes_au AFTER UPDATE ON notes BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc) VALUES ('note', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
END;

CREATE TRIGGER sync_notes_ad AFTER DELETE ON notes BEGIN
    DELETE FROM sync_outbox WHERE entity = 'note' AND entity_id = old.id;
    INSERT OR IGNORE INTO sync_tombstones(entity, entity_id, deleted_utc)
    VALUES ('note', old.id, strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00');
END;

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

-- Items. Same shape as the note triggers: `queued_utc` comes from the row's own app-clock
-- timestamp rather than strftime('now'), so enqueueing needs no clock of its own, and a tombstone
-- reads the clock because a delete has no row timestamp to beat an earlier edit with.
--
-- Exdates and alarms get no triggers of their own. Every write to one goes through
-- SqliteAgendaStatements.Save, which rewrites the whole item and bumps its updated_utc, so the
-- triggers here already cover them. SqliteSyncStoreTests pins that: if a future writer stops
-- bumping the item, the test fails there rather than the change silently never syncing — the same
-- bargain note_tags makes in 004.

CREATE TRIGGER sync_agenda_items_ai AFTER INSERT ON agenda_items BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc)
    VALUES ('agenda_item', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
    DELETE FROM sync_tombstones WHERE entity = 'agenda_item' AND entity_id = new.id;
END;

CREATE TRIGGER sync_agenda_items_au AFTER UPDATE ON agenda_items BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc)
    VALUES ('agenda_item', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
END;

-- Deleting a series cascades to its overrides, and this fires once per row, so every override gets
-- a tombstone of its own. That is what the other device needs: it holds those override rows as
-- rows, and a tombstone for the parent alone would leave them orphaned.
CREATE TRIGGER sync_agenda_items_ad AFTER DELETE ON agenda_items BEGIN
    DELETE FROM sync_outbox WHERE entity = 'agenda_item' AND entity_id = old.id;
    INSERT OR IGNORE INTO sync_tombstones(entity, entity_id, deleted_utc)
    VALUES ('agenda_item', old.id, strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00');
END;

CREATE TRIGGER sync_agenda_lists_ai AFTER INSERT ON agenda_lists BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc)
    VALUES ('agenda_list', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
    DELETE FROM sync_tombstones WHERE entity = 'agenda_list' AND entity_id = new.id;
END;

CREATE TRIGGER sync_agenda_lists_au AFTER UPDATE ON agenda_lists BEGIN
    INSERT INTO sync_outbox(entity, entity_id, queued_utc)
    VALUES ('agenda_list', new.id, new.updated_utc)
        ON CONFLICT(entity, entity_id) DO UPDATE SET queued_utc = excluded.queued_utc;
END;

-- Deleting a list moves its items to the default rather than taking them along, so each moved item
-- fires the update trigger above and is pushed with its new list_id. The other device therefore
-- learns where the items went even if it applies the list tombstone first.
CREATE TRIGGER sync_agenda_lists_ad AFTER DELETE ON agenda_lists BEGIN
    DELETE FROM sync_outbox WHERE entity = 'agenda_list' AND entity_id = old.id;
    INSERT OR IGNORE INTO sync_tombstones(entity, entity_id, deleted_utc)
    VALUES ('agenda_list', old.id, strftime('%Y-%m-%dT%H:%M:%f', 'now') || '0000+00:00');
END;
