-- Attachment sync (docs/CLOUD_SYNC.md §5, Phase 7). Two tables and no new cursor: `change_log`
-- has allowed `entity = 'file'` since 0002, so a file change pages through the same pull the notes
-- use and an existing client's cursor stays meaningful.
--
-- The split between the two tables is the whole design. `files` is per-attachment metadata, sealed
-- in `payload` exactly like a note, so the filename and the date it belongs to are not readable
-- here. `assets` is per-account bookkeeping for the bytes in R2: which blinded key, how large, and
-- how many file rows still point at it. Two attachments of the same content share one object, and
-- the object goes when the last of them does.

CREATE TABLE files (
    user_id      TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    id           TEXT NOT NULL,             -- the client's DayFileId; a random uuid
    payload      TEXT,                      -- v1.<nonce>.<ct> of {local_date, display_name,
                                            -- byte_length, asset_hash, created_utc}; NULL once deleted
    -- Kept in plaintext deliberately, and it leaks nothing: it is HMAC(HKDF(DEK), sha256(content)),
    -- so it is unforgeable without the account's key and uncorrelatable across accounts (§5.4).
    -- Stored on the row rather than derived, because a delete has to release the reference and by
    -- then the payload that named the asset is gone.
    blinded_key  TEXT,
    -- PLAINTEXT, and unavoidably so: the quota is counted in bytes, and a number that cannot be
    -- read cannot be summed. It is the size of the *ciphertext*, which is what R2 actually holds.
    stored_bytes INTEGER NOT NULL DEFAULT 0 CHECK (stored_bytes >= 0),
    updated_utc  TEXT NOT NULL,             -- PLAINTEXT: the last-write-wins clock (§7.3)
    deleted_utc  TEXT,
    PRIMARY KEY (user_id, id)
);

-- Per-account refcount over the R2 objects.
--
-- Per-account, not global: the blinded key is derived from the account's own data key, so the same
-- file uploaded by two people has two different keys and two objects. That is the point of blinding
-- (§5.4) and it costs duplicate storage on purpose.
--
-- `uploaded_utc` stays NULL between the metadata push and the bytes landing. A file row may
-- therefore reference an asset that is not there yet; the client asks for the bytes and gets a 404,
-- which is a retry, not a corruption.
CREATE TABLE assets (
    user_id      TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    blinded_key  TEXT NOT NULL,
    stored_bytes INTEGER NOT NULL DEFAULT 0 CHECK (stored_bytes >= 0),
    ref_count    INTEGER NOT NULL DEFAULT 0 CHECK (ref_count >= 0),
    uploaded_utc TEXT,
    PRIMARY KEY (user_id, blinded_key)
);

-- Reclaim scans for zero-refcount rows; without this it is a full table scan per delete.
CREATE INDEX assets_user_unreferenced ON assets(user_id, ref_count);
