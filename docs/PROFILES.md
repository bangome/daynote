# Profiles: one local store per account

Status: phase 1 (layout, migration, importer) and phase 2 (hand-off, sign-out and deletion choices,
owner check, host switching) implemented, 2026-09-29. Implementation follows this document; change the
document first when the design changes. §11 records where the implementation had to decide something
this design left open.

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
  main view a new shell. The OS gives no clean way for an app to relaunch itself. The lifetime's
  `MainView` is a host control set once and the new shell goes inside it: on Android a second
  `MainView` assignment never reaches the screen, which left the old shell up mid-switch.
- **MCP server:** resolves the active profile when it starts. A switch while a client holds it open
  takes effect on the client's next launch of the server; documented, not engineered around.

## 9. Code map

| Piece | Where |
| --- | --- |
| `ProfileStore` — base/active/account roots, `profile.json`, migration, import, removal | `Daynote.Infrastructure/Persistence/Profiles/` |
| `ProfileHost` — `IProfileHost` over `ProfileStore` + `ProfileImporter`, with a per-host session-store factory | `Daynote.Infrastructure/Persistence/Profiles/` |
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

## 11. Decisions made while implementing

- **Removal waits for the database to close.** The running app holds the account's database open
  when the user picks *Remove* (§5.4) or deletes the account (§5.5), and Windows will not rename a
  folder with an open file. So the folder gets a `remove-pending` marker instead: from that moment
  `ReadActiveProfileId` and `ListAccounts` treat it as gone, and `ProfileStore.RemoveMarkedAccounts`
  deletes it at the next start (it runs inside `MigrateLegacyLayout`, before any database opens) —
  which on the desktop is the relaunch, and on a phone the rebuild after the old provider is disposed.
  A sign-in that finds its own folder still marked removes it first and starts over.
- **Sign-out (*Keep*) leaves the account's database alone.** Only `credentials.dat` is cleared; the
  database keeps its `sync_state` owner, cursor and outbox, so signing back in carries on without a
  re-download and still passes the owner check. The pre-profile `SqliteSyncStore.SignOutAsync` (which
  clears owner and cursor) now runs only for a single-root composition and after a server-side delete.
- **Delete → *Keep* imports right away.** The account's notes are imported into the local database by
  the running app (opening the local database briefly; nothing else has it open) before the switch,
  rather than through a pending import on the local side. Either way the folder is then marked for
  removal.
- **A *Move* removes exactly what it imported, and leaves no tombstones.** `ProfileImporter.MoveFromAsync`
  deletes from the local profile only the note versions it read (one edited in the meantime, e.g. by the
  MCP server, stays for the next import), their attachments, and then the tombstones and outbox rows
  those deletes create. A tombstone left in the local profile would make a later import back into it
  (§5.5 *Keep*) read those notes as deleted.
- **The first start runs before the notes are read.** Hosts call `AccountViewModel.PrepareProfileAsync`
  ahead of loading the day, so moved notes are on screen from the first frame; `AccountService.ResumeAsync`
  runs the same preparation (idempotent) before every sync run, together with the owner check. A
  pending import that fails (a bad row, the local database busy under the MCP server) is logged and
  swallowed: the marker stays for the next start, the notes stay in the local profile, and the account
  starts and syncs what it already has.
- **The editor is saved before a profile is left, not after.** Hosts give the account view model a
  `FlushEditor` delegate, and the Move/Keep answer, both sign-out choices and both deletion choices
  call it before touching any profile; a save that fails stops the step with a message and nothing
  moves. The host's own flush on switch still runs, but by then it should find nothing to save — a
  delete → *Keep* would otherwise copy the notes out first and save the open note into the folder
  being removed.
- **Sign-out *Remove* recounts at the moment it is pressed.** The editor stays usable beside the card,
  so the count shown when the choice opened is not trusted: the press saves the editor, counts the
  outbox again, and asks (again) if anything is waiting that the user has not agreed to lose.
- **Delete → *Remove* is always asked twice**, and the second question says it cannot be undone: after
  the server delete this device holds the only copy, and with `SessionAlreadyGone` the account may still
  exist without this device's unsynced changes.
- **A *Move* clears only what the account received.** Notes the importer read, and attachments the
  destination now holds (imported, or already there). An attachment skipped because its bytes were
  never on this device stays in the local profile.
- **A relaunch that is refused leaves a visible state.** If the host's quit is refused (its flush
  failed) after the pointer moved, the relaunch is disarmed and the panel shows "the switch did not
  finish; the next start opens the new profile" with a *Switch now* button that asks again. On a
  phone, a new profile that will not open after the old provider was disposed makes the host point
  `profile.json` back at the profile that was running and compose that again; if even that fails, the
  view shows a plain "close and reopen" message instead of a dead screen.
- **Failures in profile steps become messages.** `RunAsync` and the profile steps catch everything but
  cancellation and out-of-memory (a locked database, a keystore that refuses, a platform without a
  sealed store) and show a localized sentence, instead of faulting the command or, on a phone, the app.
- **Hand-off work is off the UI thread**: creating the account database (every migration) and
  initializing the local database for a kept import run on the thread pool.
- **The owner check in the local profile** compares only the database and the session (the local
  profile has no folder name). That keeps a base root a failed migration left signed in syncing as the
  previous version did; a local profile created by this version never holds a session at all.
- **A mismatch reports `ResumeState.SignInRequired`** (mapped to `SyncOutcome.SignInRequired`). The
  panel shows the account as signed out, says why, and offers *Go back to local notes*, which drops
  the session and switches to the local profile while keeping the folder (whose notes they are is not
  guessed). Signing in again as the folder's account is refused while the database names someone
  else, rather than re-labelling it; signing in as another account hands off as usual.
- **An account folder with no owner** and no readable session is one of two things. With a
  `credentials.dat` that will not open (a lost keystore key) the panel says "sign in again". With no
  session file at all it is an account deleted before the user chose *Keep*/*Remove* (the app closed
  in between), and the panel asks that question again.
- **The question counts attachments too.** It asks when the local profile holds notes *or* files the
  user added, and says how many of each.
- **Without a profile host** (`AccountService` built with `profiles: null`, as the single-root tests do)
  sign-in, sign-out and deletion behave exactly as before this design, enrolment included: that root is
  taken to be the account's own folder.
