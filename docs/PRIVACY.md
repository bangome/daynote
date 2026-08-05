# Privacy

Daynote is a local-only application. This document states plainly what it stores and exactly how
your data is kept.

## No network, no accounts, no telemetry

Daynote makes **no network calls at runtime**. There is no account, no sign-in, no cloud sync, no
cloud backup, no analytics, and no telemetry. Nothing you write or add leaves your PC through
Daynote.

## Daynote only stores what you create — no background capture

Daynote stores only the content you actively create or add inside the app:

- **Notes** — the text of your daily notes, plus their titles, tags, favorites, and the to-dos
  parsed from your note text.
- **Day files** — files or images that **you** attach to a day.
- **Settings** — your preferences (theme, layout, shortcuts, and similar).

Daynote does **not** read or monitor your clipboard, does **not** run any background capture, does
**not** record your keystrokes, and does **not** take screenshots. It has no always-on listener of
any kind — it only touches data in response to actions you take in the app.

## Storage is plaintext and local — NOT encrypted, NOT cloud

Your notes, attached files, and settings are stored **as plaintext** on your own disk under
`%LocalAppData%\Daynote`. Specifically:

- The SQLite database and its full-text search index hold your note **text in the clear**.
- Files you attach to a day are stored as ordinary files under `assets\`.

**Daynote does not encrypt this data itself.** Its confidentiality relies on standard Windows
protections: the per-user file permissions (ACLs) on your profile folder, and any full-disk
encryption you have enabled (for example BitLocker). Anyone who can read your Windows user profile —
or an unencrypted copy of the disk — can read your Daynote data. If you need the data encrypted,
enable device encryption / BitLocker; Daynote will not do it for you and does not claim to.

## Where the data is and how to remove it

- Location: `%LocalAppData%\Daynote` (database, `assets\`, and settings). For a Microsoft Store
  install the OS redirects these writes into the app's per-user package storage, so **uninstalling
  the Store build removes your data** — back up first if you want to keep it.
- Outside of uninstall, Daynote never deletes this folder for you. You remove your data by
  uninstalling the app (Store build) or by deleting the folder yourself (sideload build).

See [DATA_AND_RECOVERY.md](DATA_AND_RECOVERY.md) for backup, recovery, and removal details.
