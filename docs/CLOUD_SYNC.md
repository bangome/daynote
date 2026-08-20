# Cloud sync design (Cloudflare Workers + D1 + R2, end-to-end encrypted)

> Status: **Phases 1–5 built. The app is wired up**, behind a configured sync endpoint. The auth Worker lives in
> [`cloud/worker/`](../cloud/worker/README.md). The WPF app still makes **no network calls** and has
> no sign-in UI, so [PRIVACY.md](PRIVACY.md) remains accurate until Phase 5 ships. Phases 2–8 below
> are still plan, not code.

## 1. Decisions taken

| Question | Decision |
| --- | --- |
| Login | Email + password, authenticated by our own Cloudflare Worker (no third-party IdP) |
| Sync scope | Everything: notes, note tags, per-note title flag, day files, and the attachment bytes |
| Conflict policy | Last-write-wins on `updated_utc`, with delete tombstones |
| **Encryption** | **End-to-end. The server stores ciphertext only and cannot read note content.** |
| **Password reset** | **In scope. Takes on the transactional-email dependency.** |
| **Reset + E2EE recovery** | **Recovery key issued at signup, plus device-assisted re-wrap** |
| Rollout | Design first (this document), then implement in phases |

Sync is **opt-in**. A signed-out Daynote stays exactly as it is today: local-only, no network calls.

## 2. Why a Worker is mandatory

D1 cannot be reached safely from a desktop client. The D1 REST API authenticates with a Cloudflare
**account** API token, which grants far more than one user's rows — shipping such a token inside a
WPF binary is an immediate, unrecoverable credential leak (any user can extract it with a hex
editor). There is no per-user scoping and no row-level security in D1.

```
Daynote (WPF, Windows)          ← all encryption/decryption happens here
      │  HTTPS + Bearer access token; bodies are already ciphertext
      ▼
Cloudflare Worker  ──►  D1   (users, tokens, wrapped keys, ciphertext blobs, change log)
   (auth + sync API)  ──►  R2   (encrypted attachment bytes, blinded keys)
```

The Worker holds the auth secrets. It never holds a content-encryption key, so a full compromise of
the Worker, D1, and R2 yields ciphertext and timing metadata — not notes.

### Plan requirement

Because the expensive key-derivation moves to the **client** (§4.2), the Worker's per-login CPU cost
drops to a few milliseconds, so the Workers free-tier 10 ms CPU limit is no longer a hard blocker.
The Paid plan is still recommended for headroom on D1 operations and R2, but it is now a cost
decision rather than a technical prerequisite.

## 3. What actually needs to sync

Read from the live schema, not from the historical one. Current writers touch:

| Table | Synced | Notes |
| --- | --- | --- |
| `notes` | yes | `id`, `local_date`, `title`, `body`, `sort_order`, `is_favorite`, `created_utc`, `updated_utc` |
| `note_tags` | yes | chip tags; ordered set per note. Tag edits already bump `notes.updated_utc` (`SqliteNoteStatements.cs:270`) |
| `settings` key `note.custom-title.<id>` | yes | **per-note state hiding in the settings table** — it is what makes `HasCustomTitle` true. It must travel with the note or titles will silently regress to "Untitled N" on a second device |
| `day_files` | yes | metadata: `id`, `local_date`, `display_name`, `byte_length`, `asset_hash`, `created_utc` |
| `file_assets` + `files\<hh>\<hash><ext>` | yes | bytes are encrypted and stored in R2 under a blinded key (§5.4) |
| `search_documents` / `search_fts` | no | derived; rebuilt locally from decrypted rows |
| `clipboard_items`, `image_assets` | no | **legacy tables no live code writes.** Confirmed by grep: no repository references them except one calendar rollup `UNION ALL`. Excluded from sync; a separate cleanup migration should drop them |
| other `settings` keys | no | shortcuts, language, theme, onboarding, layout are device-local by design |

Inline `#hashtags` in the body need no sync of their own — they are parsed from `body`.

**The local database stays plaintext.** E2EE protects the *cloud copy*; on-disk local storage is
unchanged, so local FTS5 search, the timeline, and the `Daynote.Mcp` MCP server all keep working
untouched. (Local plaintext remains a documented property — see [PRIVACY.md](PRIVACY.md).)

## 4. Auth and key hierarchy

### 4.1 What the password must do

The password has two jobs that must be kept separate, or E2EE collapses:

1. prove to the server who you are, and
2. unlock the key that decrypts your content — **without the server ever learning it**.

So the client derives one master key from the password, then splits it: one half is shown to the
server, the other never leaves the device.

### 4.2 Derivation (all client-side)

```
master_key = Argon2id(
    password,
    salt   = SHA-256("daynote-v1:" + lowercase(trim(email))),   -- available before any server call
    m = 64 MiB, t = 3, p = 4, out = 32 bytes)

auth_key   = HKDF-SHA256(master_key, info = "daynote-v1-auth", 32)   -- sent to the server
kek        = HKDF-SHA256(master_key, info = "daynote-v1-kek",  32)   -- NEVER leaves the device
```

- An email-derived salt is used because the client must derive `master_key` at login *before* it can
  authenticate to fetch anything. This is the standard trade-off; it means the salt is not secret,
  which is fine — Argon2id's cost, not salt secrecy, is what defends the password.
- **Decided: `Konscious.Security.Cryptography.Argon2` 1.3.1.** It ships `lib/net8.0` only — pure
  managed, no `runtimes/` folder, ~36 KB — so it adds nothing to the MSIX per-architecture payload,
  which is what ruled against a native Argon2 build. `KdfParameters` also supports a
  `Rfc2898DeriveBytes.Pbkdf2(SHA-256, 600 000)` fallback for a build that cannot take the
  dependency; the wire format carries the choice, so accounts created either way keep working.
  Argon2id is preferred because this key guards ciphertext held by someone else, so GPU-hardness
  matters more than it would in a server-side-only design.
- The stored server verifier is prefixed with its parameters (`argon2id$m=65536,t=3,p=4$…`) so the
  cost can be raised later and re-derived on the next successful login.

### 4.2.1 Which profile does login derive with?

A client has to derive `auth_key` *before* it can authenticate, so it cannot ask the server which
KDF parameters an account uses — an endpoint that answered that question would also answer "does this
email exist?", undoing the account-enumeration protection in §4.3.

So the profile is **pinned to the protocol version**: `daynote-v1` is Argon2id m=64 MiB, t=3, p=4, and
every v1 client derives with it. The account's parameters are still stored and returned at login, as
forward-looking metadata: a build that meets parameters it does not recognise reports "this account
was created by a newer version of Daynote" rather than deriving a key that would silently fail to
unwrap. Introducing a v2 profile later means either trying both on sign-in or adding a prelogin
endpoint and accepting the enumeration trade-off then, deliberately, rather than now by accident.

### 4.3 Server-side verifier

The server stores `PBKDF2-SHA256(auth_key, server_salt, 100 000)`. A second KDF stage on an already
256-bit-uniform input is belt-and-braces against a leaked-D1 offline attack; it does not need to be
expensive, which is what keeps Worker CPU low.

Verify with a constant-time comparison. `/auth/login` and `/auth/register` must be
rate-limited per-IP and per-email, and must not distinguish "wrong password" from "no such user" in
message or timing.

### 4.4 Content keys — envelope encryption

```
DEK           = 32 random bytes, generated once at registration. The only key that decrypts content.
wrapped_dek_pw  = AES-256-GCM(key = kek,  plaintext = DEK)
recovery_key  = 16 random bytes, shown to the user once (§4.6)
rkek          = HKDF-SHA256(recovery_key, info = "daynote-v1-rkek", 32)
wrapped_dek_rk  = AES-256-GCM(key = rkek, plaintext = DEK)
```

The server stores both wrapped blobs and can open neither. Per-entity keys are derived so no two
records share a nonce space:

```
k_note  = HKDF-SHA256(DEK, info = "note:"  + note_id)
k_file  = HKDF-SHA256(DEK, info = "file:"  + file_id)
k_asset = HKDF-SHA256(DEK, info = "asset:" + content_sha256)
```

### 4.5 Ciphertext envelope

`v1.<nonce_b64url>.<ciphertext_and_tag_b64url>` — AES-256-GCM, 96-bit random nonce, 128-bit tag.

**AAD = `user_id | entity_kind | entity_id`.** This binds a blob to its slot, so a malicious or
buggy server cannot move note A's ciphertext into note B's row and have the client accept it. A
failed tag check is a hard error surfaced to the user, never a silent skip.

### 4.6 Recovery key

- 128 random bits rendered as 26 Crockford-base32 characters in groups of four — seven groups, the
  last holding two (`K7QM-3XPV-9ZTR-4BHN-6WYD-2FGH-J0`). 128 bits of true randomness needs no
  stretching, and base32 avoids shipping a BIP-39 wordlist. Because 128 is not a multiple of 5, the
  final character carries two padding bits that the parser requires to be zero — otherwise 4
  different strings decode to the same key and a mistyped last character is accepted silently. (A wordlist rendering is a later UX upgrade; the stored format
  is the raw key, so switching the presentation is non-breaking.)
- Shown **once**, at registration, on a screen that requires an explicit "I have saved this"
  confirmation and offers copy-to-clipboard and save-to-file.
- Regenerable from Settings while signed in and unlocked: generate a new key, re-wrap the DEK,
  replace `wrapped_dek_rk`. The old key stops working immediately.
- The recovery key is not a password: it does not authenticate. It only unwraps the DEK.

### 4.7 Endpoints

| Method | Path | Body → Result |
| --- | --- | --- |
| POST | `/v1/auth/register` | `{email, auth_key, wrapped_dek_pw, wrapped_dek_rk, kdf_params}` → 201, or 409 |
| POST | `/v1/auth/login` | `{email, auth_key, device_name}` → `{access_token, refresh_token, user_id, wrapped_dek_pw, dek_generation, rewrap_pending}` |
| POST | `/v1/auth/refresh` | `{refresh_token}` → new pair (rotating) |
| POST | `/v1/auth/logout` | revokes the presented refresh token |
| POST | `/v1/auth/password` | authenticated change: `{current_auth_key, new_auth_key, new_wrapped_dek_pw, kdf_params}` |
| POST | `/v1/auth/reset/request` | `{email}` → always 204 (no account enumeration); emails a single-use token |
| POST | `/v1/auth/reset/confirm` | `{reset_token, new_auth_key}` → revokes all refresh tokens, sets `rewrap_pending = 1` |
| POST | `/v1/auth/rewrap` | authenticated: `{new_wrapped_dek_pw, dek_generation}` → clears `rewrap_pending` |
| GET | `/v1/auth/me` | → `{user_id, email, devices[], recovery_key_set, rewrap_pending}` |
| POST | `/v1/account/purge` | deletes all D1 rows and R2 objects for the user |

`/v1/auth/reset/confirm` deliberately does **not** touch `wrapped_dek_pw`: the server cannot
re-wrap a key it cannot read. It only rotates the verifier and flags that the password-wrapped DEK
is now stale.

`/v1/auth/password` requires `current_auth_key` in addition to the Bearer token. Without it, a
stolen 15-minute access token would be enough to change the password and lock the owner out of their
own account. Changing the password revokes every other device's refresh token and returns a fresh
session for the calling device, so the user is not bounced out of the app they just used.

### 4.8 Password reset flow

Reset restores **account access**; restoring **data access** needs the DEK from one of three places.

```
1. user requests reset → email token → sets a new password
   → server rotates the verifier, revokes all refresh tokens, sets rewrap_pending = 1
2. user signs in with the new password. wrapped_dek_pw no longer opens with the new kek,
   so the client enters the LOCKED state: it can sync metadata but decrypt nothing new.
3. unlock, by whichever is available:
   a. RECOVERY KEY — user enters it → unwrap via rkek → re-wrap under the new kek
      → POST /v1/auth/rewrap. Lossless.
   b. THIS DEVICE ALREADY HAD THE DEK — credentials.dat holds the DPAPI-protected DEK cache from
      before the reset. The client detects rewrap_pending, re-wraps the cached DEK under the new
      kek, and posts it. Lossless, and needs nothing from the user.
   c. NEITHER — the cloud ciphertext is unrecoverable. Offer an explicit, typed-confirmation
      "discard the cloud copy and re-upload from this PC". Local data is untouched either way.
```

Path (b) is why the reset flow revokes refresh tokens but the client does **not** wipe its cached
DEK on a 401: the DEK cache is what makes an ordinary forgotten-password reset lossless for anyone
still using their own PC. It is cleared only on explicit sign-out or on path (c).

While LOCKED the UI must be unambiguous — a persistent banner, no silent partial sync, and no
new local content pushed as plaintext-shaped-blank.

### 4.9 Transactional email

`/v1/auth/reset/request` and email verification need an email sender with a verified domain.
**Resend** (simple API, generous free tier) or **MailChannels** (Workers-native) are the candidates.
This adds an external dependency and a DNS/SPF/DKIM setup step to the deployment checklist; it was
accepted deliberately so that a forgotten password does not mean a lost account. Reset tokens are
single-use, 30-minute TTL, stored hashed.

### 4.10 Tokens

- **Access token**: JWT, HS256, Worker secret, `exp` 15 minutes, claims `{sub, jti, iat, exp}`,
  sent as `Authorization: Bearer …`.
- **Refresh token**: 32 random bytes, base64url. Only its SHA-256 is stored. TTL 60 days,
  **rotated on every refresh**; presenting an already-rotated token revokes the whole family.

### 4.11 Client-side secret storage

`%LocalAppData%\Daynote\credentials.dat`, encrypted with **Windows DPAPI**
(`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`, plus app-specific entropy). Holds the
refresh token, the access token, and the cached **DEK**. Requires the
`System.Security.Cryptography.ProtectedData` package in `Daynote.Infrastructure`.

Never put any of this in the `settings` table — that table lands verbatim in the plaintext backup
`.zip`. `BackupService` must explicitly **exclude `credentials.dat`**, and the backup must not
become a way to exfiltrate a DEK.

## 5. D1 schema

```sql
CREATE TABLE users (
    id             TEXT PRIMARY KEY,          -- uuid
    email          TEXT NOT NULL UNIQUE,      -- normalised lowercase
    verifier       TEXT NOT NULL,             -- pbkdf2$sha256$100000$…  over auth_key
    kdf_params     TEXT NOT NULL,             -- client Argon2id params, echoed back at login
    wrapped_dek_pw TEXT NOT NULL,             -- AES-GCM envelope; server cannot open
    wrapped_dek_rk TEXT,                      -- recovery-key envelope; NULL only if user declined
    dek_generation INTEGER NOT NULL DEFAULT 1,
    rewrap_pending INTEGER NOT NULL DEFAULT 0 CHECK (rewrap_pending IN (0,1)),
    quota_bytes    INTEGER NOT NULL DEFAULT 2147483648,
    created_utc    TEXT NOT NULL
);

CREATE TABLE refresh_tokens (
    token_hash  TEXT PRIMARY KEY,             -- sha256 of the opaque token
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    family_id   TEXT NOT NULL,                -- rotation chain; revoke by family
    device_name TEXT NOT NULL,
    issued_utc  TEXT NOT NULL,
    expires_utc TEXT NOT NULL,
    revoked_utc TEXT
);
CREATE INDEX refresh_tokens_user ON refresh_tokens(user_id);

CREATE TABLE reset_tokens (
    token_hash  TEXT PRIMARY KEY,
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    expires_utc TEXT NOT NULL,
    used_utc    TEXT
);

-- One row per note. The server sees an opaque blob plus the LWW clock. Nothing else.
CREATE TABLE notes (
    user_id     TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    id          TEXT NOT NULL,                -- the client NoteId (random uuid)
    payload     TEXT,                         -- v1.<nonce>.<ct> of the note JSON; NULL when deleted
    updated_utc TEXT NOT NULL,                -- PLAINTEXT — required for LWW (§7.3)
    deleted_utc TEXT,                         -- tombstone
    PRIMARY KEY (user_id, id)
);

CREATE TABLE files (
    user_id      TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    id           TEXT NOT NULL,
    payload      TEXT,                        -- encrypted {local_date, display_name, byte_length,
                                              --            asset_hash, created_utc}
    blinded_key  TEXT,                        -- R2 object key (§5.4); needed for reclaim
    stored_bytes INTEGER NOT NULL DEFAULT 0,  -- PLAINTEXT — required for quota accounting
    updated_utc  TEXT NOT NULL,
    deleted_utc  TEXT,
    PRIMARY KEY (user_id, id)
);

-- Per-user refcount so an R2 object is reclaimed when its last file row goes.
CREATE TABLE assets (
    user_id      TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    blinded_key  TEXT NOT NULL,
    stored_bytes INTEGER NOT NULL,
    ref_count    INTEGER NOT NULL DEFAULT 0,
    uploaded_utc TEXT,                        -- NULL until the bytes land in R2
    PRIMARY KEY (user_id, blinded_key)
);

-- Monotonic pull cursor. AUTOINCREMENT is strictly increasing per D1 database.
CREATE TABLE change_log (
    seq         INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id     TEXT NOT NULL,
    entity      TEXT NOT NULL CHECK (entity IN ('note','file')),
    entity_id   TEXT NOT NULL,
    written_utc TEXT NOT NULL
);
CREATE INDEX change_log_user_seq ON change_log(user_id, seq);
```

### 5.1 What the note payload contains

One JSON object, encrypted as a single blob — not per-field encryption:

```json
{ "local_date": "2026-08-19", "title": "…", "body": "…", "sort_order": 3,
  "is_favorite": true, "has_custom_title": true, "tags": ["work","q3"],
  "created_utc": "2026-08-01T…Z" }
```

A single blob is both simpler and tighter: it leaves the server with no per-field structure to
mine. Notably `local_date` is *inside* the envelope, so the server cannot even tell which days you
write on.

**Tags live in this payload rather than in a child table.** They are an ordered set the user edits
as a unit, and every tag edit already bumps `notes.updated_utc`. A separate table would need its own
per-tag clock to be mergeable, which LWW does not provide — so it would add rows without adding
correctness, and would leak tag cardinality.

### 5.2 The metadata the server unavoidably learns

Stating this plainly, because "E2EE" is often oversold:

- your email address, and when you registered
- how many notes and files you have, and their random ids
- **when** each was last edited (`updated_utc`) and deleted
- roughly how large each encrypted attachment is (`stored_bytes`)
- your IP and device names at sign-in

It does **not** learn: note titles, bodies, tags, filenames, dates, favorites, or attachment
content. Padding `updated_utc` or blob lengths to hide the rest is out of scope.

### 5.3 No `revision` column

The local `notes.revision` is Daynote's optimistic-concurrency guard *between windows of one
install*. It is meaningless across devices and must never be pushed or treated as a clock.

### 5.4 Blinded R2 keys

An R2 key of `sha256(plaintext)` would let the operator run a confirmation attack: hash a known
file, check whether the object exists. So:

```
key_id      = HKDF-SHA256(DEK, info = "daynote-v1-asset-keyid", 32)
blinded_key = hex(HMAC-SHA256(key_id, content_sha256))
```

Per-user dedup still works (identical content → identical blinded key for that user), while the
server learns nothing about content and cannot correlate the same file across two accounts. The
object body is `nonce || AES-256-GCM(k_asset, plaintext_bytes)`.

## 6. Local schema migration `004_cloud_sync.sql`

```sql
-- What still needs pushing. Replaced on each further edit, so a note edited fifty times between
-- syncs is pushed once.
CREATE TABLE sync_outbox (
    entity     TEXT NOT NULL CHECK (entity IN ('note', 'file')),
    entity_id  TEXT NOT NULL,
    queued_utc TEXT NOT NULL,
    PRIMARY KEY (entity, entity_id)
) WITHOUT ROWID;

-- Deletes must survive as tombstones or a delete on device A resurrects from device B.
CREATE TABLE sync_tombstones (
    entity      TEXT NOT NULL CHECK (entity IN ('note', 'file')),
    entity_id   TEXT NOT NULL,
    deleted_utc TEXT NOT NULL,
    PRIMARY KEY (entity, entity_id)
) WITHOUT ROWID;

-- Single row. No key material: keys live in credentials.dat under DPAPI (§4.11), because this
-- database is copied verbatim into the plaintext backup .zip.
CREATE TABLE sync_state (
    id             INTEGER PRIMARY KEY CHECK (id = 1),
    user_id        TEXT,
    server_cursor  INTEGER NOT NULL DEFAULT 0 CHECK (server_cursor >= 0),
    dek_generation INTEGER NOT NULL DEFAULT 0 CHECK (dek_generation >= 0),
    locked         INTEGER NOT NULL DEFAULT 0 CHECK (locked IN (0, 1)),
    last_sync_utc  TEXT
);
INSERT INTO sync_state(id) VALUES (1);

CREATE TABLE sync_asset_queue (
    asset_hash TEXT PRIMARY KEY,
    direction  TEXT NOT NULL CHECK (direction IN ('up', 'down')),
    attempts   INTEGER NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error TEXT
) WITHOUT ROWID;
```

**An outbox table, not `sync_dirty` columns.** A column has to be set by every writer and is silently
forgotten by the next writer someone adds; and a trigger maintaining it would have to `UPDATE` the
table it fires on. Writing to a separate table keeps the triggers non-recursive and makes the
bookkeeping impossible for a writer to skip — including the MCP server, which shares this database.
Triggers on `notes` and `day_files` enqueue on insert/update and clear on delete, taking `queued_utc`
from the row's own app-clock timestamp so enqueueing needs no clock of its own.

No trigger on `note_tags` is needed: a tag edit already bumps `notes.updated_utc`, so the note
triggers cover it. A test pins that, so if a future writer stops bumping the note, it fails there
rather than tag edits silently never syncing.

**Tombstone timestamps come from the app clock, not from SQL.** The `AFTER DELETE` triggers can stamp
a tombstone with `strftime('now')`, and they do — but only as a safety net for a writer that bypasses
the repository. The delete path records its own tombstone first, using the injected `IClock`, and the
trigger's `INSERT OR IGNORE` leaves it alone. This matters because last-write-wins has to order a
delete against an edit: with only the SQL clock, any test using a fake clock becomes flaky against
real wall-clock time, which is exactly how this was found.

**Enrolment is explicit.** Because the outbox is trigger-maintained, content written before migration
004 has no queue entry. `SqliteSyncStore.EnrollExistingContentAsync` queues it at first sign-in
rather than a column default doing it implicitly, so a user who never signs in gets no bookkeeping
churn at all. It is idempotent.

### 6.1 The `UNIQUE (local_date, sort_order)` hazard

`notes` carries `UNIQUE (local_date, sort_order)` (`001_initial.sql`). Two devices that each add a
note to the same date will both claim the same `sort_order`, so a naive merge insert **fails with a
constraint violation**. SQLite cannot defer a table `UNIQUE`, so the merge:

1. shifts every row on the affected date by a large uniform offset, which preserves uniqueness within
   the date and cannot collide with an unshifted row (sort orders are dense `0..n-1` at rest),
2. writes the incoming rows into the freed high slots,
3. re-orders the whole date to a dense `0..n-1` range,
4. all inside one transaction.

Two corrections to the first draft of this section, both found while implementing it:

**Order by claimed slot first, not by creation time.** Sorting the date by `(created_utc, id)` would
be deterministic but would silently undo every manual reordering the user ever made, on every merge.
The comparison is `(claimed sort_order, created_utc, id)`: each side keeps the slot it claims, and the
tie-breakers exist only so two devices resolve a *contested* slot identically.

**A row that moves must get a newer `updated_utc`.** The first draft said resequencing must not bump
the timestamp, to avoid merge ping-pong. That is backwards: the server rejects a push whose
`updated_utc` is not newer than its copy, so a sort-order change carrying its old timestamp can never
propagate — the note would sit in the queue forever, re-pushed on every sync and never accepted.
Rows that move are stamped with the merge instant; rows that end up where they started keep their
timestamp, so a merge never manufactures an edit. Convergence comes from the ordering being
deterministic, not from withholding the bump.

**Cleaning up after the shift must not eat a real pending edit.** Re-ordering rewrites every row on
the date, which fires the outbox trigger for notes the merge did not actually change. Those spurious
entries are removed afterwards — but only the spurious ones: the queue is snapshotted before the
shift, so a sibling note that was genuinely waiting to be pushed keeps its entry. Without that
snapshot, merging one note discards another note's unpushed local edit and nothing ever notices.

This remains the most bug-prone part of the design and has dedicated tests for the collision, the
contested slot, manual ordering, cross-date moves, the timestamp rules, and the queue snapshot.

## 7. Sync protocol

### 7.1 Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/v1/sync/pull?since=<seq>&limit=500` | → `{changes: [...], cursor, has_more, server_utc}` |
| POST | `/v1/sync/push` | `{notes, files, tombstones}` → per-item accept/reject + new cursor |
| POST | `/v1/assets/register` | `{blinded_keys: [...]}` → per key: `already_present`, or an upload URL |
| GET | `/v1/assets/<blinded_key>` | streams the encrypted object (Worker-proxied, so auth is enforced) |

### 7.2 Client cycle

```
sync():
  0. if sync_state.locked → metadata-only; never push, never overwrite. Stop.
  1. push tombstones      (deletes first, so a delete never loses to its own stale upsert)
  2. encrypt + upload dirty assets  (bytes before the metadata that references them)
  3. encrypt + push dirty notes and files
  4. pull ?since=server_cursor → decrypt → LWW merge → resequence dates → rebuild FTS rows
  5. download + decrypt assets referenced by pulled files
  6. store new cursor + last_sync_utc
```

Ordering matters: a `day_files` row whose R2 object is absent shows as a broken attachment on the
other device, so assets are pushed **before** their metadata and pulled **after** it.

### 7.2.1 The cursor comes only from a pull

The push response reports the change log's head. Adopting it as the pull cursor **skips every change
already in the log below it that this device has not read** — a second device that pushes its own note
gets a cursor past the first device's note and never sees it again. Only a pull advances the cursor,
and only forwards.

This was caught by two devices each adding a note offline: both pushes succeeded, both devices
reported a clean sync, and each was permanently missing the other's note. Nothing errored.

### 7.2.2 Skew is checked before the first write

The clock check has to happen before anything is stored, not on the push response — by then the data
is already on the server under a bad timestamp, poisoning every later comparison for that note. The
engine spends one read-only pull establishing the server's clock before it writes anything.

Server-side enforcement is the stronger form of this and is still worth adding: a client cannot be
trusted to police its own clock, and one with a wrong clock can currently still poison its own
account's ordering if it lies. Tracked as a follow-up.

### 7.3 LWW rule

For each incoming entity, compare `updated_utc` (ISO-8601 UTC, the format already stored):

- incoming newer → apply (including tombstone → delete locally)
- incoming older → ignore, keep local dirty so the next push wins
- exact tie → break on `id` string comparison, so all devices reach the same answer

Because `updated_utc` is plaintext, the *server* can also reject a stale push, which stops an
offline device from clobbering newer content on reconnect.

Clock skew is LWW's known weakness: a device with a wrong system clock wins or loses unfairly.
Mitigation: every response carries `server_utc`, and the client refuses to sync — with a clear
message — when local time differs by more than 5 minutes.

**Canonical timestamp format — `yyyy-MM-ddTHH:mm:ss.fffffffZ`.** This is not cosmetic. The local
database writes `DateTimeOffset.ToString("O")`, which renders as `2026-08-19T12:34:56.1234567+00:00`,
while JavaScript's `toISOString()` renders `2026-08-19T12:34:56.123Z`. Compared as strings those two
disagree with the instants they represent: after the shared `.123` prefix, `'4'` (0x34) sorts before
`'Z'` (0x5A), so a .NET timestamp reads as *older* than a JavaScript one taken at the same moment.
Every timestamp on the wire therefore uses the single sortable form above (7 fractional digits, `Z`
suffix), which lets the server's stale-push check be a plain string comparison. The local database
keeps its existing `"O"` format — `SqliteSyncStore` converts at the boundary — and the **client** must
compare parsed `DateTimeOffset` values, never raw strings. Implemented in `cloud/worker/src/time.ts`.

### 7.4 Safety net for overwritten edits

Before LWW discards a local body, write the losing version to
`%LocalAppData%\Daynote\conflicts\<note-id>-<utc>.txt` and surface a one-line notice
("2 notes were replaced by a newer version from another device — view backups"). LWW is only
acceptable with this escape hatch; silent data loss is not.

### 7.5 Trigger points

- On sign-in, on app resume from tray, and on a 5-minute idle timer.
- Debounced ~10 s after the autosave coordinator reports a clean save (never mid-edit).
- Manual **Sync now** in Settings.
- Never on a blocked or failed autosave — `AutosaveCoordinator` already blocks navigation there, and
  sync must not race an unsaved buffer.

## 8. Client architecture

New code, following the existing Core/Infrastructure split:

```
src/Daynote.Core/Sync/
    ISyncApiClient.cs        -- transport contract (no HTTP types in Core)
    ISyncSessionStore.cs     -- token + DEK persistence contract
    ISyncCrypto.cs           -- wrap/unwrap, encrypt/decrypt envelope, key derivation
    SyncEngine.cs            -- push/pull/merge orchestration, testable against fakes
    SyncModels.cs            -- DTOs, payload JSON, LWW comparison, tombstones
    SyncStatus.cs            -- Idle / Syncing / Offline / AuthRequired / Locked / Error

src/Daynote.Infrastructure/Sync/
    HttpSyncApiClient.cs     -- HttpClient + retry/backoff + 401→refresh→retry once
    DpapiSyncSessionStore.cs -- credentials.dat via ProtectedData (tokens + cached DEK)
    AesGcmSyncCrypto.cs      -- Argon2id/HKDF/AES-GCM; System.Security.Cryptography
    SqliteSyncStore.cs       -- dirty-row reads, merge writes, cursor, resequencing

src/Daynote.App/Account/
    SignInViewModel.cs / SignInView.xaml
    RecoveryKeyViewModel.cs / RecoveryKeyView.xaml   -- show-once, confirm-saved, copy/save
    UnlockViewModel.cs / UnlockView.xaml             -- post-reset LOCKED state (§4.8)
    AccountSettingsViewModel.cs  -- email, devices, last sync, Sync now, regenerate recovery key,
                                    Sign out, Delete cloud data

cloud/worker/                -- the Cloudflare Worker (TypeScript, wrangler)
    src/index.ts, auth.ts, reset.ts, sync.ts, assets.ts, schema.sql
    wrangler.toml, test/
```

`SyncEngine` takes `ISyncApiClient` + `ISyncCrypto` + `SqliteSyncStore` + `IClock`, so the merge
algorithm and the crypto are unit-testable with no network and no WPF — matching how the rest of the
codebase is tested.

All new user-visible strings go through `AppStrings`/`KoreanStrings`/`EnglishStrings`; the app is
bilingual ko/en and an untranslated string is a defect, not a TODO.

## 9. Implementation phases

| Phase | Deliverable | Done when |
| --- | --- | --- |
| 1 ✅ | Worker skeleton + D1 schema + register/login/refresh/logout, with Vitest coverage | **Done.** `cloud/worker/`: 40 tests green in workerd against a real local D1; `wrangler deploy --dry-run` validates config and build |
| 2 ✅ | `AesGcmSyncCrypto`: Argon2id, HKDF split, DEK wrap/unwrap, envelope with AAD | **Done.** `Daynote.Core/Sync` + `Daynote.Infrastructure/Sync`; 49 crypto cases and 14 recovery-key cases green. Tampered ciphertext, tampered tag, spliced nonce, swapped note, swapped user, swapped entity kind, and swapped DEK purpose all fail as `CiphertextAuthenticationFailed` |
| 3 ✅ | `004_cloud_sync.sql`, `SqliteSyncStore`, tombstone capture on delete | **Done.** Trigger-maintained outbox, tombstones, merge with dense re-ordering, cursor/account state. 51 tests green, including a seeded v3 → v4 upgrade and the §6.1 collision cases |
| 4 ✅ | `SyncEngine` + `HttpSyncApiClient` for notes/tags/title-flag | **Done.** Worker push/pull with a grouped change-log cursor; engine encrypts, pushes, pulls, merges. 22 convergence cases across two real databases and one shared server, plus 26 Worker cases. A test asserts the server's stored blob contains no title, body, tag, or date |
| 5a ✅ | Account layer: `AccountService`, `DpapiSyncSessionStore`, `HttpAuthApiClient`, `SyncTokenProvider`, `FileSystemConflictSink` | **Done.** Register → sync → sign out → sign in on an empty data root restores notes and tags, with the same data key. 18 tests, including token renewal, the indistinguishable wrong-password/unknown-email pair, an unreadable credentials file reading as signed out, conflict files landing as plain text, and the backup zip not containing `credentials.dat` |
| 5b ✅ | Sign-in view, recovery-key screen, account settings panel, status chip, ko/en strings, DI wiring | **Done.** Account section inside the settings panel, chip in the command row, 42 localized keys in both catalogs, 13 view-model tests. Gated on `DAYNOTE_SYNC_ENDPOINT`: with no endpoint nothing is registered, no `HttpClient` exists, and the section is absent. PRIVACY.md, DATA_AND_RECOVERY.md, and STORE.md rewritten |
| 6 | Email sender + `/auth/reset/*` + `/auth/rewrap` + LOCKED/Unlock UI | All three §4.8 unlock paths pass end-to-end, including the "discard cloud copy" path |
| 7 | R2 attachments: blinded keys, encrypted upload/download, refcount + reclaim, quota | A file added on A opens on B; deleting on both releases the R2 object; quota rejects cleanly |
| 8 | Docs + store metadata: rewrite PRIVACY.md, DATA_AND_RECOVERY.md, STORE.md; backup excludes `credentials.dat` | Docs describe the account, the E2EE boundary, the §5.2 metadata, and server-side deletion |

Phases 1–6 are the minimum shippable unit — password reset is now in scope, so it cannot be deferred
past launch. Phase 7 may follow as a second release, in which case attachments are explicitly
labelled "not yet synced" in the UI rather than failing quietly.

## 10. Non-negotiables

- **Signed-out behaviour is unchanged.** No network calls, no background threads, no added startup
  latency for users who never sign in.
- **The server never receives a key.** No endpoint accepts `kek`, `rkek`, `DEK`, the password, or
  the recovery key. A code review that finds one of these in a request body is a blocker.
- **Never block an edit on the network.** Sync is strictly background; a failure shows a status
  chip, never a modal, and never prevents saving locally.
- **A failed authentication tag is an error, not a skip.** Silently dropping an undecryptable record
  would look like data loss with no explanation.
- **Server-side deletion must exist and must actually run.** `/v1/account/purge` (D1 rows + R2
  objects), reachable from Settings. Store and GDPR requirement, not a nicety.
- **Do not promise more than E2EE delivers.** §5.2 goes into PRIVACY.md verbatim in substance.

## 11. Documentation impact

These files currently assert the opposite of this feature and must be rewritten in the same change
that ships it:

- `docs/PRIVACY.md:6-9` — "makes **no network calls at runtime** … no account, no sign-in, no cloud
  sync" — becomes: the account is opt-in; content is encrypted on this PC before upload; the server
  holds ciphertext plus the metadata listed in §5.2; here is the retention and deletion path.
- `docs/DATA_AND_RECOVERY.md:3-5` — "stores everything locally and never syncs or backs up to any
  cloud" — becomes: local is still the source of truth; the cloud copy is a **sync, not a backup**
  (a LWW sync propagates a mistaken delete; a backup does not). Add: losing both the password and
  the recovery key makes the cloud copy unrecoverable by design, and the in-app backup `.zip` stays
  the real safety net.
- `docs/STORE.md` — privacy-policy answers and the feature list.

Note the local database remains plaintext; E2EE changes the cloud posture only, and PRIVACY.md must
not blur the two.

## 12. Cost and limits

At Cloudflare's current published tiers — verify against the live pricing page before committing:

- **D1**: 25 GB per database; reads/writes billed beyond the included allowance. Daynote writes a
  handful of rows per sync per device — negligible. Watch `change_log`: it grows per write and needs
  a scheduled compaction job (delete rows below the minimum known device cursor) or reads creep up.
- **R2**: no egress fee; storage and class-A/B operations billed. Per-file size is already capped by
  `FileCapturePolicy.MaxFileBytes`; the `users.quota_bytes` column enforces an account-level cap
  (default 2 GB) so one account cannot run up an unbounded bill.
- **Email**: Resend/MailChannels free tiers cover reset volume comfortably at this scale.
- The client-side Argon2id at 64 MiB is a per-login cost on the user's PC, not ours.

## 13. Open questions

1. **Email verification at registration.** The sender dependency now exists anyway, so this is
   nearly free — include it in Phase 6 or skip?
2. **Device limit** per account, and whether remote device revoke ships in v1.
3. **Recovery key opt-out.** If a user declines to save one, `wrapped_dek_rk` is NULL and only
   §4.8(b) can save them. Allow the opt-out with a blunt warning, or make it mandatory?
4. **Legacy table cleanup.** `clipboard_items` and `image_assets` are dead; drop them in a separate
   migration before `004` so the sync code never has to reason about them.
