-- Why a refresh token was revoked, and which token replaced it (docs/CLOUD_SYNC.md §4.10).
--
-- The reuse grace in sessions.ts answers a client that lost a refresh response with a fresh pair.
-- It used to pair a rotated token with its successor by timestamp equality and could not tell a
-- rotation from the retirement the grace itself performs, so a retired successor became
-- grace-eligible in turn and two parties could alternate forever. These columns make both explicit.
--
-- revoke_reason: 'rotated' | 'grace_retired' | 'logout' | 'family'. Only 'rotated' is
--                grace-eligible. Rows revoked before this migration keep NULL, which never is.
-- replaced_by:   the token_hash a rotation issued in this token's place. The insert of the new
--                token is conditional on this link, which is what makes a rotation atomic.
ALTER TABLE refresh_tokens ADD COLUMN revoke_reason TEXT;
ALTER TABLE refresh_tokens ADD COLUMN replaced_by TEXT;
