# Profiles: one local store per account

Status: design, 2026-09-29. Implementation follows this document; change the document first when
the design changes.

## 1. The problem

Every Daynote keeps one SQLite database per device (`<DataRoot>/daynote.db`) and signs whichever
account the user picks into it. Signing in then enrolls **every note on the device** into that
account's outbox (`AccountService.SignInAsync` → `ISyncStore.EnrollExistingContentAsync`). That was
right when a device only ever met one account. It is wrong as soon as it meets two:

- Signing out and back in with another account uploads the first account's notes into the second.
  Observed 2026-09-29: an iPhone signed out of Google and into Apple pushed 62 copies of the Google
  account's notes into the new Apple account.
- A shared device has no way to sign out *and* leave the next person nothing to read.
- A deleted account's notes stay mixed with whatever the device writes next.

## 2. Goals

1. Notes of different accounts never share a database, so no query, sync run or bug can mix them.
2. Signing out, signing in as someone else and signing back in are ordinary, lossless operations.
3. Notes written before signing in (or with no account at all) go to an account only when the user
   says so.
4. The user can choose to leave nothing behind when signing out or deleting an account.
5. No existing user loses data or has to do anything when they update.

Not goals: two accounts open at once; syncing the local profile anywhere; merging two accounts.

## 3. Layout

```
<BaseRoot>/                    ← DaynoteDataRoot.Resolve(), unchanged
  daynote.db, files/, assets/  ← the LOCAL profile: exactly where data lives today
  conflicts/
  profile.json                 ← which profile is active
  accounts/
    <userId>/                  ← one ACCOUNT profile per account ever signed in on this device
      daynote.db, files/, assets/, conflicts/
      credentials.dat          ← that account's sealed session; nowhere else
```

- **The base root stays the local profile.** A user who never signed in keeps every file where it
  is; nothing is moved for them. Backup already includes only `daynote.db`, `files/` and `assets/`,
  so `accounts/` never ends up inside a local backup.
- An account folder is named by the server's `user_id` (a UUID). It holds that account's local copy,
  its outbox, cursor, attachments and conflict copies — everything `DaynoteAppOptions.DataRoot`
  addresses today. The local profile never has a `credentials.dat`.
- `profile.json`: `{"version":1,"active":"local"}` or `{"version":1,"active":"<userId>"}`, written
  atomically (temp file + rename). Missing or unreadable means `local`. An `active` naming a folder
  that does not exist also means `local`.

`DaynoteAppOptions.DataRoot` becomes **the active profile's folder**; `DaynoteAppOptions.BaseRoot` is
the base. Every service that takes `DataRoot` today (database, file store, backup, conflict sink,
session store) is therefore scoped to the active profile without being touched.

## 4. Invariants

- **One owner per account database.** An account profile's `sync_state.user_id`, its
  `credentials.dat` user and its folder name are the same id. On start-up and before every sync run,
  a mismatch refuses to sync and reports the account as needing sign-in; it never "fixes" the
  database by adopting the other id.
- **The local profile never syncs.** No credentials are ever written there, so `ResumeAsync` reports
  signed-out and the scheduler does nothing.
- **Sign-in never enrolls existing content on its own.** Enrollment happens only when the user
  chooses to bring local notes along (§5.2), or for content already inside that account's own folder.

## 5. Flows

### 5.1 Start-up
Resolve `BaseRoot`, run the one-time migration (§7), read `profile.json`, build the app over the
active profile's folder. The desktop tray, the phone head and the MCP server all resolve the same way.

### 5.2 Sign in (from the local profile)
1. The browser or Apple sheet runs and the Worker returns the session, exactly as today.
2. If the local profile holds notes the user wrote (the untouched first-run sample does not count),
   ask once: **"이 기기의 노트 N개를 이 계정으로 옮길까요?"** — *Move them* / *Keep them on this device only*.
   With no such notes there is no question.
3. Hand-off:
   - create `accounts/<userId>/` if needed, seed its device-local settings from the local profile
     (§6), and write the session to its `credentials.dat`;
   - if the user chose *Move*, record a pending import of the local profile's notes (tags, custom
     titles, attachments);
   - point `profile.json` at the account and switch the app to that profile (§8).
4. On the account profile's first start: mark the store signed in, run the pending import (it goes
   through the normal writers, so the outbox picks it up), then sync. *Move* then clears the moved
   notes from the local profile, so they are not in two places; *Keep* leaves the local profile as is.

An existing account folder (signing back in on a device that kept the data) is reused: its cursor,
outbox and notes carry on, and step 2's question still applies to whatever the local profile holds.

### 5.3 Signing in again inside an account profile
A session that expired or lost its key re-authenticates the account whose profile is open. If the
browser returns a **different** account, it is treated as §5.2 from that account's point of view: the
current profile is left as it is and the new account gets its own folder.

### 5.4 Sign out
1. Run a sync if anything is waiting. If the outbox still holds changes (offline, server error),
   say how many and ask to continue.
2. Choose: **Keep this account's notes on this device** (default; signing back in is instant) or
   **Remove them from this device** (shared devices). Remove deletes the account folder after the
   server logout; with unsynced changes it requires an explicit second confirmation.
3. Point `profile.json` at `local` and switch (§8).

### 5.5 Delete account
After the server confirms: **Keep the notes on this device (as local notes)** or **Remove them**.
Keep imports the account's notes into the local profile (same importer as §5.2, local direction);
either way the account folder is then deleted, `profile.json` points at `local`, and the app
switches. A session the server no longer knows (`AccountDeletion.SessionAlreadyGone`) offers the same
choice.

### 5.6 A device where several accounts signed in
Each keeps its folder until signed out with *Remove* or deleted. The settings screen shows only the
active account; signing in to another is §5.2. (A picker listing kept accounts is a later nicety.)

## 6. Device-local settings

The `settings` table lives inside each database, but language, theme, shortcuts, onboarding state and
layout describe the device, not the account. When a profile is created, those rows are copied from
the profile being left; nothing is copied back. The per-note `note.custom-title.<id>` rows are content
and travel only with an import.

## 7. Migration (first start of this version)

- `accounts/` absent and the base database's `sync_state` signed in as user U → move `daynote.db`
  (+`-wal`/`-shm`, after a checkpoint), `files/`, `assets/`, `conflicts/` and `credentials.dat` into
  `accounts/U/`, copy the device settings back into a fresh local database (§6), and set
  `profile.json` to U. The user stays signed in, with the same notes, cursor and outbox.
- Otherwise → write `profile.json` = `local`. Nothing moves.
- The migration is idempotent and runs before any database is opened. A failure part-way leaves the
  base untouched (move into a staging folder, then rename it into place).

Everything that was enrolled into U before this version stays U's: that was already true on the server.

## 8. Switching profiles at run time

Every service holding the database is a singleton, so a switch rebuilds the composition rather than
re-pointing live objects:

- **Desktop (Avalonia) and WPF:** relaunch the process, the same path backup-restore already takes
  (`RequestRestartForRestore`), after flushing the editor.
- **Phones:** flush, dispose the service provider, build a new one over the new root and give the
  main view a new shell. The OS gives no clean way for an app to relaunch itself.
- **MCP server:** resolves the active profile when it starts. A switch while a client holds it open
  takes effect on the client's next launch of the server; documented, not engineered around.

## 9. Code map

| Piece | Where |
| --- | --- |
| `ProfileStore` — base/active/account roots, `profile.json`, migration, import, removal | `Daynote.Infrastructure/Persistence/Profiles/` |
| `DaynoteAppOptions.ForCurrentUser` over the active profile; `BaseRoot` | `Daynote.Presentation/Composition` |
| `IProfileHost` port (Core): current profile id, hand-off to an account, return to local (keep/remove), import requests | `Daynote.Core/Sync` |
| `AccountService`: sign-in hands off instead of enrolling; owner check in `ResumeAsync` | `Daynote.Core/Sync` |
| `AccountViewModel`: the *Move/Keep* question, sign-out and deletion choices, "switch requested" | `Daynote.Presentation/Account` |
| Hosts: react to a switch (relaunch / rebuild) | Desktop `App.axaml.cs`, WPF `App.xaml.cs`, `Daynote.Mobile/App.axaml.cs` |
| MCP server over the active profile | `Daynote.Mcp/Program.cs` |

## 10. Tests

- Migration: signed-in legacy root moves into `accounts/U/` with notes, outbox, cursor and
  credentials intact; signed-out legacy root is left byte-for-byte alone; a second run is a no-op.
- Sign-in with *Keep*: the account uploads nothing from the local profile (the 62-copy bug).
- Sign-in with *Move*: the notes arrive on another device of the account and leave the local profile.
- Sign out (*Keep*) then sign in as another account: two folders, no note in both; sign back in as
  the first: its notes and cursor are there without a re-download.
- Sign out (*Remove*): the folder is gone; unsynced changes need the second confirmation.
- Delete (*Keep*/*Remove*), including `SessionAlreadyGone`.
- Owner mismatch refuses to sync.
- Device settings follow the user into a new profile.
- Headless UI: the *Move/Keep* question and the sign-out choice render and bind on every shell.
