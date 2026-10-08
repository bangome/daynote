-- To-dos, events and their lists (docs/TODOS.md §9). The server half of migrations 005 and 006.
--
-- One table for both kinds, unlike notes and files. That split exists because `files` carries two
-- plaintext columns of its own — the blinded key and the byte count the quota is summed from. An
-- agenda row carries nothing a note row does not, and a list is not a different *kind* of storage
-- from an item, only a different name in the same feature. Two tables would be the same five
-- columns twice and a fourth LEFT JOIN in the pull.
--
-- What the server knows about a to-do is therefore: that it exists, when it last changed, and
-- whether it is a list or an item. Not its title, its date, its repeat rule or which list it is in
-- — all of that is inside `payload`, sealed by AgendaPayloadCodec before it leaves the device.
--
-- The entity is plaintext and has to be: the pull sorts nothing, so the client is what orders a
-- page (lists before items, series before overrides), and it cannot do that without knowing which
-- rows are which.

CREATE TABLE agenda (
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    entity      TEXT NOT NULL CHECK (entity IN ('agenda_item', 'agenda_list')),
    id          TEXT NOT NULL,             -- the client's uuid; random, so it leaks nothing
    payload     TEXT,                      -- v1.<nonce>.<ciphertext>; NULL once deleted
    updated_utc TEXT NOT NULL,             -- PLAINTEXT, and unavoidably so: the LWW clock
    deleted_utc TEXT,                      -- tombstone marker; updated_utc holds the same instant
    PRIMARY KEY (user_id, entity, id)
);

-- `change_log.entity` has allowed 'note' and 'file' since 0002, and a CHECK cannot be widened in
-- SQLite without rebuilding the table. 0002 reserved 'file' ahead of time precisely to avoid this;
-- two entities that did not exist as a design then are the price of not reserving more.
--
-- The rebuild copies `seq` explicitly, so every cursor any client holds keeps pointing at the same
-- row. AUTOINCREMENT's high-water mark comes back as MAX(seq) rather than its old value, which
-- differs only if the rows above it were deleted — and the only thing that deletes change_log rows
-- is deleting the account they belong to, which leaves no client holding a cursor into them.

CREATE TABLE change_log_new (
    seq         INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id     TEXT NOT NULL,
    entity      TEXT NOT NULL CHECK (entity IN ('note', 'file', 'agenda_item', 'agenda_list')),
    entity_id   TEXT NOT NULL,
    written_utc TEXT NOT NULL
);

INSERT INTO change_log_new(seq, user_id, entity, entity_id, written_utc)
SELECT seq, user_id, entity, entity_id, written_utc FROM change_log;

DROP TABLE change_log;
ALTER TABLE change_log_new RENAME TO change_log;

CREATE INDEX change_log_user_seq ON change_log(user_id, seq);
