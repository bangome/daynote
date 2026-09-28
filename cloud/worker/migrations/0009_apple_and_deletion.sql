-- Sign in with Apple, and account deletion (docs/CLOUD_SYNC.md §4.1c and §4.12).
--
-- The iPhone app has to offer Sign in with Apple next to Google (App Store guideline 4.8), so an
-- account's identity is now one of two provider subjects rather than always a Google one. Both are
-- UNIQUE and either may be NULL, but never both: an account nobody can sign in to is an account
-- its owner can never delete, and the CHECK below keeps that state unrepresentable.
--
-- `apple_refresh_token` is the refresh token Apple hands back on the code exchange, sealed under
-- DEK_WRAP_KEY (src/dek.ts). It is kept for exactly one call: Apple requires an app that deletes an
-- account to revoke it (`/auth/revoke`), and that needs a token. It is never used to sign in.
--
-- `users` is rebuilt for the same reason as 0005 — `google_sub` was NOT NULL and SQLite cannot
-- relax a constraint in place — but it cannot be rebuilt the way 0005 did it. D1 enforces foreign
-- keys, and with them on, `DROP TABLE users` runs an implicit DELETE of every account that fires the
-- ON DELETE CASCADE on notes, refresh_tokens, subscriptions, files and assets. `defer_foreign_keys`
-- does not stop that: it defers constraint *checks*, not cascade *actions*. The DROP would have
-- emptied every child table, silently, in the same transaction that looked like it succeeded.
-- (test/migration.test.ts caught this on the first run, against populated tables.)
--
-- Renaming `users` away first does not help either: with foreign keys on, a rename rewrites the
-- children's references to follow it, legacy_alter_table or not.
--
-- So the five child tables are rebuilt too, pointing at `users_new` from the start. By the time the
-- old `users` is dropped nothing references it, so its implicit DELETE has nowhere to cascade. The
-- renames at the end then rewrite the children's `users_new` references to `users`. Every column,
-- CHECK, index and ON DELETE clause is carried across verbatim from 0001–0008; only the reference
-- target is temporarily different. `change_log` and `billing_events` have no foreign key and are
-- left alone, apart from one new index on `billing_events` at the end.

PRAGMA defer_foreign_keys = true;

CREATE TABLE users_new (
    id                  TEXT PRIMARY KEY,
    -- Google's `sub`. NULL for an account created through Apple.
    google_sub          TEXT UNIQUE,
    -- Apple's `sub`: stable per developer team, never reused. NULL for a Google account. The
    -- address is never the identity, for either provider — Apple's may be a private relay.
    apple_sub           TEXT UNIQUE,
    -- Sealed (`t1.` envelope, src/dek.ts); only for the revoke Apple requires on deletion.
    apple_refresh_token TEXT,
    -- Empty only for an Apple account that has never shared an address (email is optional there).
    email               TEXT NOT NULL,
    protection          TEXT NOT NULL DEFAULT 'server'
                            CHECK (protection IN ('server', 'passphrase')),
    wrapped_dek         TEXT,
    wrapped_dek_pw      TEXT,
    wrapped_dek_rk      TEXT,
    kdf_params          TEXT,
    quota_bytes         INTEGER NOT NULL DEFAULT 2147483648,
    created_utc         TEXT NOT NULL,
    last_seen_utc       TEXT NOT NULL,
    trial_ends_utc      TEXT,

    CHECK (
        (protection = 'server'
            AND wrapped_dek IS NOT NULL
            AND wrapped_dek_pw IS NULL AND wrapped_dek_rk IS NULL)
        OR
        (protection = 'passphrase'
            AND wrapped_dek IS NULL
            AND wrapped_dek_pw IS NOT NULL AND wrapped_dek_rk IS NOT NULL)
    ),

    -- Somebody has to be able to sign in to every account.
    CHECK (google_sub IS NOT NULL OR apple_sub IS NOT NULL)
);

INSERT INTO users_new
    (id, google_sub, apple_sub, apple_refresh_token, email, protection, wrapped_dek,
     wrapped_dek_pw, wrapped_dek_rk, kdf_params, quota_bytes, created_utc, last_seen_utc,
     trial_ends_utc)
SELECT id, google_sub, NULL, NULL, email, protection, wrapped_dek,
       wrapped_dek_pw, wrapped_dek_rk, kdf_params, quota_bytes, created_utc, last_seen_utc,
       trial_ends_utc
  FROM users;

-- 0001
CREATE TABLE refresh_tokens_new (
    token_hash  TEXT PRIMARY KEY,
    user_id     TEXT NOT NULL REFERENCES users_new(id) ON DELETE CASCADE,
    family_id   TEXT NOT NULL,
    device_name TEXT NOT NULL,
    issued_utc  TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    revoked_utc TEXT
);
INSERT INTO refresh_tokens_new
    (token_hash, user_id, family_id, device_name, issued_utc, expires_utc, revoked_utc)
SELECT token_hash, user_id, family_id, device_name, issued_utc, expires_utc, revoked_utc
  FROM refresh_tokens;

-- 0002
CREATE TABLE notes_new (
    user_id     TEXT NOT NULL REFERENCES users_new(id) ON DELETE CASCADE,
    id          TEXT NOT NULL,
    payload     TEXT,
    updated_utc TEXT NOT NULL,
    deleted_utc TEXT,
    PRIMARY KEY (user_id, id)
);
INSERT INTO notes_new (user_id, id, payload, updated_utc, deleted_utc)
SELECT user_id, id, payload, updated_utc, deleted_utc FROM notes;

-- 0006
CREATE TABLE subscriptions_new (
    user_id            TEXT PRIMARY KEY REFERENCES users_new(id) ON DELETE CASCADE,
    provider           TEXT NOT NULL DEFAULT 'paddle',
    customer_id        TEXT,
    subscription_id    TEXT,
    status             TEXT NOT NULL,
    current_period_end_utc TEXT,
    grace_ends_utc     TEXT,
    updated_utc        TEXT NOT NULL
);
INSERT INTO subscriptions_new
    (user_id, provider, customer_id, subscription_id, status, current_period_end_utc,
     grace_ends_utc, updated_utc)
SELECT user_id, provider, customer_id, subscription_id, status, current_period_end_utc,
       grace_ends_utc, updated_utc
  FROM subscriptions;

-- 0007
CREATE TABLE files_new (
    user_id      TEXT NOT NULL REFERENCES users_new(id) ON DELETE CASCADE,
    id           TEXT NOT NULL,
    payload      TEXT,
    blinded_key  TEXT,
    stored_bytes INTEGER NOT NULL DEFAULT 0 CHECK (stored_bytes >= 0),
    updated_utc  TEXT NOT NULL,
    deleted_utc  TEXT,
    PRIMARY KEY (user_id, id)
);
INSERT INTO files_new (user_id, id, payload, blinded_key, stored_bytes, updated_utc, deleted_utc)
SELECT user_id, id, payload, blinded_key, stored_bytes, updated_utc, deleted_utc FROM files;

CREATE TABLE assets_new (
    user_id      TEXT NOT NULL REFERENCES users_new(id) ON DELETE CASCADE,
    blinded_key  TEXT NOT NULL,
    stored_bytes INTEGER NOT NULL DEFAULT 0 CHECK (stored_bytes >= 0),
    ref_count    INTEGER NOT NULL DEFAULT 0 CHECK (ref_count >= 0),
    uploaded_utc TEXT,
    PRIMARY KEY (user_id, blinded_key)
);
INSERT INTO assets_new (user_id, blinded_key, stored_bytes, ref_count, uploaded_utc)
SELECT user_id, blinded_key, stored_bytes, ref_count, uploaded_utc FROM assets;

-- Children first, so that when `users` goes nothing references it any more.
DROP TABLE refresh_tokens;
DROP TABLE notes;
DROP TABLE subscriptions;
DROP TABLE files;
DROP TABLE assets;
DROP TABLE users;

-- Renaming the parent rewrites the children's `users_new` references to `users`.
ALTER TABLE users_new RENAME TO users;
ALTER TABLE refresh_tokens_new RENAME TO refresh_tokens;
ALTER TABLE notes_new RENAME TO notes;
ALTER TABLE subscriptions_new RENAME TO subscriptions;
ALTER TABLE files_new RENAME TO files;
ALTER TABLE assets_new RENAME TO assets;

-- Every index 0001–0008 created on these tables, recreated under its original name.
CREATE INDEX users_email ON users(email);
CREATE INDEX refresh_tokens_user ON refresh_tokens(user_id);
CREATE INDEX refresh_tokens_family ON refresh_tokens(family_id);
CREATE INDEX subscriptions_subscription ON subscriptions(subscription_id);
CREATE INDEX subscriptions_customer ON subscriptions(customer_id);
CREATE INDEX assets_user_unreferenced ON assets(user_id, ref_count);

-- New: account deletion clears `billing_events.user_id` (src/account.ts), which without this is a
-- scan of every payment event ever received, once per deletion.
CREATE INDEX billing_events_user ON billing_events(user_id);
