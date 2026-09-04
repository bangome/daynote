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

**This version of Daynote makes no network calls at all.** Cloud sync is not included in it — see
below.

There is exactly one way note content can leave this PC, and you have to switch it on: the **AI
integration (MCP)**, which hands your notes to an AI client that may forward them to that client's own
service. It is described below. With it off, everything stays on this PC.

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

## Cloud sync — not in this version

Daynote has no accounts and no cloud sync. There is no sign-in, nothing is uploaded, and the app
opens no network connection. If you have read about cloud sync elsewhere, it is built but not
released.

When it does ship it will be optional and off until you sign in with Google, and it will **not** be
end-to-end encrypted. Your notes are encrypted in transit and encrypted at rest on the service, but
the service also holds the key that opens them, so whoever runs it can read what is stored. That is
the direct consequence of signing in with an identity provider instead of a password: Google proves
who you are, but it gives the app no secret to build an encryption key from.

You will be able to turn that off. **Locking your notes** is an opt-in switch in the same settings
panel: it re-encrypts the data key with a passphrase only you know and asks the service to destroy
its own copy, after which nobody running the service can read your notes. The cost is that you enter
that passphrase once on each new PC, and that a forgotten passphrase needs the recovery key shown
when you turn the lock on — with the service's copy gone, there is nothing else that can open the
cloud copy.

Cloud sync is a paid subscription. **Daynote never sees your card details**: checkout happens on a
page hosted by Paddle, our payment provider and merchant of record, and what reaches our service is
a subscription status and a renewal date. If a subscription ends, syncing stops and **nothing is
deleted** — the notes on your PC are untouched and the copy already uploaded is kept.

Either way the service holds your Google account id and email address, and the times each note
changed. This document will be replaced with the full specifics in the same release that turns cloud
sync on. Until then, treat any description of it as a plan rather than a description of the app you
are running.

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
