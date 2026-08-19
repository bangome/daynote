-- Phase 1: accounts, tokens, and rate limiting.
-- The sync tables (notes / files / assets / change_log) arrive in a later migration; nothing here
-- stores content, only credentials and the wrapped keys the client cannot keep on its own.

CREATE TABLE users (
    id             TEXT PRIMARY KEY,
    email          TEXT NOT NULL UNIQUE,      -- normalised: trimmed + lowercased
    verifier       TEXT NOT NULL,             -- pbkdf2$sha256$<iters>$<salt>$<hash> over auth_key
    kdf_params     TEXT NOT NULL,             -- the CLIENT's KDF params, echoed back at login
    wrapped_dek_pw TEXT NOT NULL,             -- AES-GCM envelope under the password KEK
    wrapped_dek_rk TEXT,                      -- AES-GCM envelope under the recovery KEK (NULL = opted out)
    dek_generation INTEGER NOT NULL DEFAULT 1,
    rewrap_pending INTEGER NOT NULL DEFAULT 0 CHECK (rewrap_pending IN (0, 1)),
    quota_bytes    INTEGER NOT NULL DEFAULT 2147483648,
    created_utc    TEXT NOT NULL
);

CREATE TABLE refresh_tokens (
    token_hash  TEXT PRIMARY KEY,             -- sha256(token); the token itself is never stored
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    family_id   TEXT NOT NULL,                -- rotation chain: reuse revokes the whole family
    device_name TEXT NOT NULL,
    issued_utc  TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    revoked_utc TEXT
);

CREATE INDEX refresh_tokens_user ON refresh_tokens(user_id);
CREATE INDEX refresh_tokens_family ON refresh_tokens(family_id);

-- Fixed-window counters for login/register abuse. Keyed by "<action>:<scope>:<value>:<window>".
CREATE TABLE rate_limits (
    bucket      TEXT PRIMARY KEY,
    hits        INTEGER NOT NULL,
    expires_utc TEXT NOT NULL
);

CREATE INDEX rate_limits_expiry ON rate_limits(expires_utc);
