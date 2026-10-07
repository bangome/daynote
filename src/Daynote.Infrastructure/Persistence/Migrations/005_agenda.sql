-- To-dos and events as entities (docs/TODOS.md).
--
-- Nothing reads these yet. The panels still parse `-[ ]` out of note bodies, the `@` command does
-- not exist, and the one-time migration of §8 has not been written. This lands the shape alone so
-- the readers, the sync and the phone can follow in one release, which §12 requires them to do.
--
-- "Agenda" is the umbrella for both kinds here because `Tasks` as a namespace collides with
-- System.Threading.Tasks in every file that touches one. The product words stay 할 일 and 일정.
--
-- Deliberately additive: sync_outbox and sync_tombstones still CHECK entity IN ('note','file'),
-- and widening a CHECK means rebuilding the table in SQLite. Phase 2 has to rebuild them anyway to
-- add triggers for these tables, so the rebuild happens once, there, rather than twice.

-- A container for to-dos: a Reminders list, a Todoist project, a CalDAV collection. Notes have
-- tags, but a tag is per note and a container is per to-do, so they cannot share a table.
CREATE TABLE agenda_lists (
    id          TEXT PRIMARY KEY,
    -- Empty means "the built-in default", which the UI renders in the current language. A rename
    -- writes a real name and it stops being translated, which is what a rename should mean.
    name        TEXT NOT NULL,
    sort_order  INTEGER NOT NULL CHECK (sort_order >= 0),
    is_default  INTEGER NOT NULL DEFAULT 0 CHECK (is_default IN (0, 1)),
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
) WITHOUT ROWID;

-- Exactly one default, stated in the schema rather than in whichever writer remembers. The MCP
-- server shares this database and will not remember.
CREATE UNIQUE INDEX ux_agenda_lists_default ON agenda_lists(is_default) WHERE is_default = 1;

-- The id is fixed rather than generated so that two devices creating their default list offline
-- create the same row, and sync merges them instead of leaving the user with two defaults.
INSERT INTO agenda_lists(id, name, sort_order, is_default, created_utc, updated_utc)
VALUES (
    '00000000-0000-0000-0000-00000000da7e',
    '',
    0,
    1,
    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
    strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

-- One table for both kinds: a to-do and an event differ by whether a time range is set, and
-- splitting them would double every query the day panel and the Timeline make.
CREATE TABLE agenda_items (
    id          TEXT PRIMARY KEY,
    list_id     TEXT NOT NULL REFERENCES agenda_lists(id),
    kind        TEXT NOT NULL CHECK (kind IN ('task', 'event')),
    title       TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',

    -- Wall clock plus a zone, never a UTC instant (docs/TODOS.md §6). "Every Monday 07:00" is a
    -- repetition of a wall-clock time: across a DST boundary the absolute time has to move for the
    -- alarm to stay at 07:00, and an instant cannot be turned back into the rule that produced it.
    -- Stored as local 'YYYY-MM-DDTHH:MM' against an IANA zone id, the way iCal writes
    -- DTSTART;TZID=Asia/Seoul:20261008T070000.
    tz           TEXT NOT NULL,
    starts_at    TEXT,
    ends_at      TEXT,
    due_at       TEXT,
    -- Whether DUE carried a time, so a date-only to-do is not shown as due at midnight.
    has_due_time INTEGER NOT NULL DEFAULT 0 CHECK (has_due_time IN (0, 1)),

    -- The recurrence rule, as RRULE, even where the UI offers three choices. Inventing a format
    -- means maintaining a converter on the way out and losing rules on the way in.
    rrule         TEXT,
    -- An override of one occurrence: RECURRENCE-ID. `series_id` points at the row carrying the
    -- rule, `recurrence_id` is the original occurrence start it replaces. This is how a single
    -- occurrence is moved, and how per-occurrence completion is recorded.
    series_id     TEXT REFERENCES agenda_items(id) ON DELETE CASCADE,
    recurrence_id TEXT,

    status        TEXT NOT NULL DEFAULT 'needs_action'
                  CHECK (status IN ('needs_action', 'completed', 'cancelled')),
    completed_utc TEXT,
    -- The iCal scale: 1 highest, 9 lowest, 0 unset. Todoist and Graph both disagree with it and
    -- with each other, so the mapping lives at the edge and the store keeps the standard.
    priority      INTEGER NOT NULL DEFAULT 0 CHECK (priority BETWEEN 0 AND 9),
    -- Whether this shows in the Timeline. 'auto' means events and one-off tasks yes, recurring
    -- tasks no — a daily rule would otherwise be on every day forever. No iCal counterpart and
    -- never exported: it is a view preference and the receiving app has its own.
    timeline_visibility TEXT NOT NULL DEFAULT 'auto'
                  CHECK (timeline_visibility IN ('auto', 'always', 'never')),

    -- Where it was captured, for a one-way jump back. No foreign key on purpose: it is allowed to
    -- dangle, and a deleted note must not take tasks with it.
    source_note_id TEXT,

    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,

    -- A row is either a plain item, a series, or an override of one. An override has both halves
    -- of its key and carries no rule of its own.
    CHECK ((series_id IS NULL) = (recurrence_id IS NULL)),
    CHECK (series_id IS NULL OR rrule IS NULL),
    -- NULLs compare distinct in SQLite, so this constrains overrides without touching anything
    -- else: one override per occurrence of a series.
    UNIQUE (series_id, recurrence_id),

    -- An event is the kind with a span; a to-do is the kind with a deadline.
    CHECK (kind <> 'event' OR starts_at IS NOT NULL),
    CHECK (ends_at IS NULL OR starts_at IS NOT NULL),
    CHECK (has_due_time = 0 OR due_at IS NOT NULL)
) WITHOUT ROWID;

CREATE INDEX ix_agenda_items_due ON agenda_items(due_at) WHERE due_at IS NOT NULL;
CREATE INDEX ix_agenda_items_starts ON agenda_items(starts_at) WHERE starts_at IS NOT NULL;
CREATE INDEX ix_agenda_items_list ON agenda_items(list_id);
CREATE INDEX ix_agenda_items_series ON agenda_items(series_id) WHERE series_id IS NOT NULL;

-- EXDATE: an occurrence the rule produces and the user removed. Skipping is not completing, and
-- the two must not be confused when the series is exported.
CREATE TABLE agenda_exdates (
    item_id    TEXT NOT NULL REFERENCES agenda_items(id) ON DELETE CASCADE,
    occurrence TEXT NOT NULL,
    PRIMARY KEY (item_id, occurrence)
) WITHOUT ROWID;

-- VALARM, reduced to the one form the product offers: minutes before the due or start time. Zero
-- means "at the time". A row per alarm, because more than one is normal and a column is not.
CREATE TABLE agenda_alarms (
    item_id      TEXT NOT NULL REFERENCES agenda_items(id) ON DELETE CASCADE,
    lead_minutes INTEGER NOT NULL CHECK (lead_minutes >= 0),
    PRIMARY KEY (item_id, lead_minutes)
) WITHOUT ROWID;

-- What this row is called in somebody else's service. A list, not a column: a single external_id
-- works until the second integration and then forces a migration.
CREATE TABLE agenda_external_refs (
    entity      TEXT NOT NULL CHECK (entity IN ('item', 'list')),
    entity_id   TEXT NOT NULL,
    service     TEXT NOT NULL,
    -- Which connected account, since the same service can be connected twice.
    connection  TEXT NOT NULL,
    external_id TEXT NOT NULL,
    etag        TEXT,
    synced_utc  TEXT,
    PRIMARY KEY (entity, entity_id, service, connection)
) WITHOUT ROWID;

-- Two lists must not point at one CalDAV collection (docs/TODOS.md §10.1): their items would merge
-- on the way out and be indistinguishable on the way back. The same holds for two items.
CREATE UNIQUE INDEX ux_agenda_external_refs_target
    ON agenda_external_refs(service, connection, external_id);
