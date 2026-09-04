-- Opt-in end-to-end encryption (docs/CLOUD_SYNC.md §4.1b).
--
-- By default the Worker holds an account's data key and can read its notes. An account that turns
-- the lock on replaces that arrangement: the key is wrapped on the device under a passphrase and a
-- recovery key, and the Worker's own copy is destroyed. Both states live in one table because they
-- are the same account with a different key custody, and `protection` says which one is in force.
--
-- The table is rebuilt rather than extended because `wrapped_dek` was NOT NULL in 0004, and SQLite
-- cannot relax a column constraint in place. Existing rows are carried across as 'server', which is
-- what they are: nothing changes for an account that never turns the lock on.
--
-- The sync tables reference `users(id)` by name, so the rename keeps their foreign keys intact.

CREATE TABLE users_new (
    id             TEXT PRIMARY KEY,
    google_sub     TEXT NOT NULL UNIQUE,
    email          TEXT NOT NULL,
    -- Which custody this account's data key is in.
    protection     TEXT NOT NULL DEFAULT 'server'
                       CHECK (protection IN ('server', 'passphrase')),
    -- The data key sealed under DEK_WRAP_KEY. NULL exactly when the lock is on: there is no state
    -- in which the server keeps a spare.
    wrapped_dek    TEXT,
    -- AES-GCM under a key derived from the lock passphrase. Opaque to the server.
    wrapped_dek_pw TEXT,
    -- Under a key derived from the one-time recovery key. Also opaque, and returned to the client:
    -- withholding it would make the recovery key useless exactly when it is needed.
    wrapped_dek_rk TEXT,
    -- The client's KDF parameters, stored opaquely and echoed back so a new device derives with the
    -- parameters that were in force when the lock was turned on.
    kdf_params     TEXT,
    quota_bytes    INTEGER NOT NULL DEFAULT 2147483648,
    created_utc    TEXT NOT NULL,
    last_seen_utc  TEXT NOT NULL,

    -- The invariant, enforced here rather than trusted to the handler.
    CHECK (
        (protection = 'server'
            AND wrapped_dek IS NOT NULL
            AND wrapped_dek_pw IS NULL AND wrapped_dek_rk IS NULL)
        OR
        (protection = 'passphrase'
            AND wrapped_dek IS NULL
            AND wrapped_dek_pw IS NOT NULL AND wrapped_dek_rk IS NOT NULL)
    )
);

INSERT INTO users_new
    (id, google_sub, email, protection, wrapped_dek, quota_bytes, created_utc, last_seen_utc)
SELECT id, google_sub, email, 'server', wrapped_dek, quota_bytes, created_utc, last_seen_utc
  FROM users;

DROP TABLE users;

ALTER TABLE users_new RENAME TO users;

CREATE INDEX users_email ON users(email);
