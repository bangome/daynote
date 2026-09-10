# Privacy

Daynote is local-first. This document states plainly what it stores, what can leave your PC, and
exactly how your data is kept.

*Applies to Daynote 1.3.0.0. Last updated 2026-08-28 — this revision covers the AI integration (MCP),
which is new in 1.3.*

*This page is published at `https://daynote.arachat.cc/privacy`, rendered from this file by the sync
Worker — the page and this document cannot say different things.*

## No telemetry, no analytics, and nothing sent unless you ask for it

Daynote has **no analytics and no telemetry**, ever. Nothing about how you use the app is reported
anywhere.

**Daynote makes no network call until you ask it to.** With cloud sync switched off — which is how
it starts — the app opens no connection at all.

There are exactly two ways note content can leave this PC, and you have to switch each one on:

- **Cloud sync**, which uploads your notes so other PCs can read them. Off until you sign in with
  Google. Described below, including who can read what is stored.
- **AI integration (MCP)**, which hands your notes to an AI client that may forward them to that
  client's own service. Also described below.

With both off, everything stays on this PC.

## AI integration (MCP, optional, off by default)

Daynote ships an MCP server that lets an AI client — Claude Desktop, Claude Code, or any other MCP
host — read and write your notes. **Nothing is connected until you register it** from Settings →
**AI 연동 / AI integration**; before that the server is never started.

Registering is the one time Daynote writes outside its own storage: it adds a `daynote` entry to your
AI client's configuration file — for Claude Desktop, `%AppData%\Claude\claude_desktop_config.json`. Only
that entry is added; other servers and settings in the file are left as they are, and a file Daynote
cannot parse is reported back to you rather than overwritten. For Claude Code, Settings hands you the
command to run instead and changes nothing itself.

Once registered, understand what the AI client can see:

- It has the **same access to your notes as the app does**: search, read any day, create, edit, and
  delete. It reads the same local database; there is no separate copy.
- **What the client then does with that content is the client's business, not Daynote's.** A
  cloud-based assistant will send the notes it reads to its own service to answer you. Daynote cannot
  see or control that, and this page cannot describe it — check your AI client's own privacy terms.
- The server itself makes **no network calls**. It speaks to the client over stdin/stdout on this PC.

To disconnect it, remove the `daynote` entry from your MCP client's configuration. Uninstalling
Daynote removes the server itself, but not that entry — it is your client's file, so Daynote does not
touch it on the way out; the entry simply stops resolving.

See [MCP.md](MCP.md) for the tool list and setup details.

## Cloud sync — optional, and off until you sign in

Cloud sync is in the app and switched off until you sign in with Google. Signed out there is no
account, nothing is uploaded, and the app opens no network connection.

Once you sign in, it is **not** end-to-end encrypted. Your notes are encrypted in transit and
encrypted at rest on the service, but the service also holds the key that opens them, so whoever
runs it can read what is stored. That is the direct consequence of signing in with an identity
provider instead of a password: Google proves who you are, but it gives the app no secret to build
an encryption key from.

You can turn that off. **Locking your notes** is an opt-in switch in the same settings panel: it
re-encrypts the data key with a passphrase only you know and asks the service to destroy
its own copy, after which nobody running the service can read your notes. The cost is that you enter
that passphrase once on each new PC, and that a forgotten passphrase needs the recovery key shown
when you turn the lock on — with the service's copy gone, there is nothing else that can open the
cloud copy.

Syncing your notes, to-dos, tags and favorites is free once you sign in. Syncing the images and
files you attach is a paid subscription. **Daynote never sees your card details**: checkout happens
on a page hosted by Paddle, our payment provider and merchant of record, and what reaches our
service is a subscription status and a renewal date. If a subscription ends, your notes keep syncing,
file syncing stops, and **nothing is deleted** — everything on your PC is untouched and the copies
already uploaded are kept.

Either way the service holds your Google account id and email address, and the times each note
changed.

Attached files are stored the same way your notes are — sealed, with the filename and the day they
belong to inside the sealed part, so the service does not learn what any of them is called. What it
does hold for each one is its size, because storage has to be counted, and a per-account key derived
from the file's contents, because the stored object needs a name. That key is derived with your
account's own key, so the same file in two accounts is stored under two unrelated names and cannot
be matched up between them — and it cannot be worked backwards to test whether you hold a file
someone already has.

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

- Location: `%LocalAppData%\Daynote` (database, `assets\`, and settings), for the Microsoft Store
  build and other builds alike.
- **Back up before uninstalling.** Whether uninstalling the Store build also removes that folder has
  not been established, so treat your notes as at risk and export a backup first.
- Daynote never deletes this folder for you. To remove your data, delete the folder yourself after
  uninstalling — that is the only way to be certain it is gone.

See [DATA_AND_RECOVERY.md](DATA_AND_RECOVERY.md) for backup, recovery, and removal details.
