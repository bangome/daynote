# Data and recovery

This document explains where Daynote keeps your data, what survives updates and reinstalls, and how
to back up, recover, or remove your data.

Your PC is the only copy. This version has no cloud sync, so there is nothing standing between a
mistake and lost notes except a backup you took — see [Backing up your data](#backing-up-your-data) below.

When cloud sync does ship it will not change that. It is a **sync, not a backup**: it propagates
deletions as faithfully as it propagates edits, so a note you delete by mistake is deleted
everywhere. **You** will still own the backup story.

## Data location

All Daynote data lives under:

```
%LocalAppData%\Daynote
```

- `daynote.db` — the SQLite database (notes and clipboard text; plaintext, see
  [PRIVACY.md](PRIVACY.md)).
- `daynote.db-wal`, `daynote.db-shm` — SQLite write-ahead-log side files.
- `assets\` — captured images as content-addressed PNG files (`assets\<hh>\<hash>.png`).
- Settings are stored inside the database.
Two more files appear only once cloud sync ships, and neither exists in this version:

- `credentials.dat` — the cloud-sync session and the account's content key, encrypted with Windows
  DPAPI. Present only while signed in, excluded from backups on purpose, and unusable on another PC
  or under another Windows account. Losing it costs nothing: the key is held by the service and is
  re-fetched, because sign-in is a Google account rather than a password only you know.
- `conflicts\` — plain-text copies of note versions that a sync replaced with a newer version from
  another device. Nothing there is needed by the app; it exists so a sync never silently discards
  something you wrote.

The data is **plaintext and not encrypted by Daynote**. It is not copied or uploaded anywhere.

## What survives update, uninstall, and reinstall

The app writes under `%LocalAppData%\Daynote`.

- **Update** to a newer version keeps all notes, images, settings, and pause state.
- **Uninstall** leaves `%LocalAppData%\Daynote` in place — measured, see below. Back up anyway.
- **Reinstall** finds the data still there and picks it up; a restore is only needed if the folder
  was removed by hand.

Treat the in-app **Backup** as your safety net before uninstalling, resetting, or moving to another
PC. That advice has not changed, but the reason behind it has been corrected twice: this page used
to say the Store package redirected its storage into the package's private store, so an uninstall
necessarily took the notes with it. Measured on 2026-09-07, the installed Store build wrote to the
real `%LocalAppData%\Daynote` and its `LocalCache` held no Daynote folder — there is no redirection.
Measured on 2026-09-08, uninstalling the package removed the package's own container and left
`%LocalAppData%\Daynote` untouched, file for file, database hash unchanged. So uninstall does not
destroy your notes; the backup is for the ordinary reasons — disk failure, a new PC, a mistake.

## Backing up your data

**In-app (recommended).** Settings → **백업 및 복원**:

- **백업** writes a single `.zip` containing all your data (notes, clipboard items, attachments, and
  settings) to a location you choose. The database is captured with a consistent online snapshot, so
  you can back up while the app is running.
- **복원** lets you pick a backup `.zip`. Daynote validates it, then **restarts** and applies it before
  the database opens. Your current data is moved aside into `%LocalAppData%\Daynote\pre-restore-backup`
  first, so a restore can be undone by copying that folder back.

**Manual alternative.** You can still copy the folder by hand:

1. Quit Daynote (tray → Quit) so the database is flushed and not mid-write.
2. Copy the entire `%LocalAppData%\Daynote` folder to your backup location.

To restore manually, quit Daynote and copy the folder back to `%LocalAppData%\Daynote`.

> Because the data is plaintext, treat any backup copy as sensitive: store it only on media you
> trust, ideally encrypted (for example a BitLocker-protected drive).

## Recovery behavior

- On startup Daynote reconciles image assets: unreferenced files and stale temporary files under the
  data root are cleaned up, and a note or clipboard item whose image file is missing is shown in a
  clear "missing image" state rather than crashing.
- Autosave retains your unsaved text if a save fails; navigation and Quit are blocked until the save
  succeeds or you resolve the problem, so you do not silently lose edits.

## Removing your data

To permanently delete your Daynote data:

1. Quit Daynote.
2. (Optional) Uninstall the app.
3. Delete `%LocalAppData%\Daynote`.

This is irreversible and is the only supported deletion path.
