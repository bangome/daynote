# Data and recovery

This document explains where Daynote keeps your data, what survives updates and reinstalls, and how
to back up, recover, or remove your data.

Your PC is the source of truth. Optional cloud sync keeps your other devices in step, but it is a
**sync, not a backup**: it propagates deletions as faithfully as it propagates edits, so a note you
delete by mistake is deleted everywhere. **You** still own the backup story — see
[PRIVACY.md](PRIVACY.md) for what cloud sync does and does not store.

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
- `credentials.dat` — your cloud-sync session and content key, encrypted with Windows DPAPI. Present
  only while signed in, excluded from backups on purpose, and unusable on another PC or under another
  Windows account.
- `conflicts\` — plain-text copies of note versions that a sync replaced with a newer version from
  another device. Nothing here is needed by the app; it exists so a sync never silently discards
  something you wrote.

The data is **plaintext and not encrypted by Daynote**. It is not copied or uploaded anywhere.

## What survives update, uninstall, and reinstall

The Store build uses **standard packaged storage**: the app writes under `%LocalAppData%\Daynote` and
Windows redirects that into the package's per-app store. As a result:

- **Update** to a newer version keeps all notes, images, settings, and pause state.
- **Uninstall removes the app's data.** Back up first (below) if you want to keep it.
- **Reinstall** starts fresh; restore a backup to bring your data back.

Because uninstalling clears the data, treat the in-app **Backup** as your safety net before
uninstalling, resetting, or moving to another PC. (Older development *sideload* builds kept the data
across uninstall by declaring the `unvirtualizedResources` capability; the Store build drops that —
see [PACKAGING.md](PACKAGING.md).)

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
