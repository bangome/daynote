-- Note sync storage. The server holds one opaque blob per note plus the last-write-wins clock, and
-- nothing else: no title, no body, no tags, not even the date (docs/CLOUD_SYNC.md §5.1).
--
-- Files and assets arrive with the R2 work in a later phase; `change_log.entity` already allows
-- 'file' so the cursor does not need reshaping then.

CREATE TABLE notes (
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    id          TEXT NOT NULL,             -- the client's NoteId; a random uuid, so it leaks nothing
    payload     TEXT,                      -- v1.<nonce>.<ciphertext>; NULL once deleted
    updated_utc TEXT NOT NULL,             -- PLAINTEXT, and unavoidably so: this is the LWW clock
    deleted_utc TEXT,                      -- tombstone marker; updated_utc holds the same instant
    PRIMARY KEY (user_id, id)
);

-- The pull cursor. AUTOINCREMENT is strictly increasing for the lifetime of the database, which is
-- what lets a client say "everything after seq N" and get a stable answer.
--
-- A client's own writes come back to it on the next pull. That is deliberate: filtering by device
-- would mean a client that lost its local database and re-synced with the same device id would skip
-- its own history. An echo costs one wasted row and is ignored by last-write-wins, since its
-- timestamp equals the local one.
CREATE TABLE change_log (
    seq         INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id     TEXT NOT NULL,
    entity      TEXT NOT NULL CHECK (entity IN ('note', 'file')),
    entity_id   TEXT NOT NULL,
    written_utc TEXT NOT NULL
);

CREATE INDEX change_log_user_seq ON change_log(user_id, seq);
