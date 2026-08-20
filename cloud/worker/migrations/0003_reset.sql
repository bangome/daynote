-- Password reset (docs/CLOUD_SYNC.md §4.8).
--
-- Resetting a password restores account access. It cannot restore data access: the server has no way
-- to re-wrap a key it cannot read, so `/v1/auth/reset/confirm` rotates the verifier and sets
-- users.rewrap_pending, and the client re-wraps the data key from either the recovery key or a
-- device that still has it cached.

CREATE TABLE reset_tokens (
    token_hash  TEXT PRIMARY KEY,          -- sha256 of the code; the code itself is never stored
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    -- A typeable code is short enough to guess if guessing were free, so each token carries its own
    -- attempt counter on top of the per-IP rate limit.
    attempts    INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    expires_utc TEXT NOT NULL,
    used_utc    TEXT
);

CREATE INDEX reset_tokens_user ON reset_tokens(user_id);
CREATE INDEX reset_tokens_expiry ON reset_tokens(expires_utc);
