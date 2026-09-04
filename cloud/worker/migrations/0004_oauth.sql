-- Phase 9: identity moves to Google OAuth and the data key becomes server-held.
--
-- This replaces the password/E2EE model of 0001 and 0003. The decision and its consequence are
-- recorded in docs/CLOUD_SYNC.md §1: the server can now read note content, because it holds the
-- key that decrypts it. Everything below follows from that.
--
-- `users` is recreated rather than altered: five of its columns existed only to hold key material
-- the server was not allowed to open, and dropping them one at a time would leave a table whose
-- shape had to be read historically. The sync tables reference `users(id)` by name, so recreating
-- the table keeps their foreign keys intact, and D1 held no accounts when this ran.

DROP TABLE IF EXISTS reset_tokens;
DROP TABLE IF EXISTS users;

CREATE TABLE users (
    id             TEXT PRIMARY KEY,
    -- The Google account identifier. Stable for the life of the account and never reissued, which
    -- is why it, and not the address, is the identity: an email can be changed or reassigned.
    google_sub     TEXT NOT NULL UNIQUE,
    email          TEXT NOT NULL,             -- normalised; display and support only
    -- The data key, sealed under the Worker's DEK_WRAP_KEY (src/dek.ts). Sealed rather than stored
    -- raw so that a D1 dump on its own yields nothing: opening it needs the Worker secret too.
    -- This is defence in depth, NOT end-to-end encryption — the Worker can open every one of these.
    wrapped_dek    TEXT NOT NULL,
    quota_bytes    INTEGER NOT NULL DEFAULT 2147483648,
    created_utc    TEXT NOT NULL,
    last_seen_utc  TEXT NOT NULL
);

CREATE INDEX users_email ON users(email);
