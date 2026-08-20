# Privacy

Daynote is local-first. This document states plainly what it stores, what can leave your PC, and
exactly how your data is kept.

## No telemetry, no analytics, and nothing sent unless you ask for it

Daynote has **no analytics and no telemetry**, ever. Nothing about how you use the app is reported
anywhere.

Daynote makes **no network calls at all** unless you turn on cloud sync and sign in. Cloud sync is
off by default; with it off, the app behaves exactly as it always has — everything stays on this PC.

## Cloud sync (optional, off by default)

If you sign in under Settings → **클라우드 동기화 / Cloud sync**, your notes are synced to your other
devices through a service Daynote operates. What that means precisely:

**Your notes are encrypted on this PC before they are uploaded.** The key comes from your password
and never leaves your device. The service stores ciphertext it cannot open — not the operators of the
service, and not anyone who obtains a copy of its database.

**What the service stores and can read:**

- your email address, and when the account was created
- how many notes you have, and their random identifiers
- **when** each note was last edited or deleted
- the size of each encrypted note
- your IP address and device names when you sign in

**What the service cannot read:** note titles, note bodies, tags, favorites, the dates your notes are
filed under, or their order. All of that is inside the encrypted payload. The service cannot even tell
which days you write on.

**If you forget your password**, you can reset it by email. Resetting gets you back into the account,
but it does not by itself open the cloud copy: the key comes from your password, and the service does
not hold it. What happens next depends on where you are:

- **On a PC you already used**, the notes open automatically — the key is still on that machine.
- **On a new PC**, you are asked for the recovery key shown once when you created the account.
- **With neither**, the cloud copy cannot be opened by anyone, including us. You can discard it and
  start again from the notes on your PC, which are never affected either way.

Reset emails come from `no-reply@daynote.arachat.cc` and carry a code that expires in 30 minutes.

**Cloud sync is not a backup.** It copies changes, including deletions: a note you delete on one
device is deleted on the others. Use Settings → **백업 및 복원 / Backup and restore** for a real
backup.

Attachments are **not** synced yet. Files you attach to a day stay on the PC you added them to.

To stop syncing, sign out. That removes the account and its keys from this PC and leaves your local
notes untouched.

## Daynote only stores what you create — no background capture

Daynote stores only the content you actively create or add inside the app:

- **Notes** — the text of your daily notes, plus their titles, tags, favorites, and the to-dos
  parsed from your note text.
- **Day files** — files or images that **you** attach to a day.
- **Settings** — your preferences (theme, layout, shortcuts, and similar).

Daynote does **not** read or monitor your clipboard, does **not** run any background capture, does
**not** record your keystrokes, and does **not** take screenshots. It has no always-on listener of
any kind — it only touches data in response to actions you take in the app.

## On-disk storage is plaintext — NOT encrypted

This is unchanged by cloud sync. Encryption protects the **cloud copy**; the database on your own
disk stays readable, exactly as before.

Your notes, attached files, and settings are stored **as plaintext** on your own disk under
`%LocalAppData%\Daynote`. Specifically:

- The SQLite database and its full-text search index hold your note **text in the clear**.
- Files you attach to a day are stored as ordinary files under `assets\` and `files\`.
- If you are signed in, `credentials.dat` holds your session and your content key, encrypted with
  Windows DPAPI for your Windows account. It is deliberately kept out of the database, and out of
  every backup archive Daynote writes, so a backup you copy elsewhere never carries the key.
- Note versions replaced by a newer version from another device are kept as plain text under
  `conflicts\`, so a sync never destroys something you typed without leaving a copy.

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
