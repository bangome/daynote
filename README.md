# Daynote

Daynote is a resident Windows desktop app for daily notes and a dated clipboard inbox. Pick a date
on the calendar and it shows that date's notes and, separately, the text and images you copied that
day. One search surface covers your note text and captured clipboard text. Everything stays on your
own PC.

## What it does

- **Dated notes.** Each local date holds an ordered set of titled Markdown notes with autosave. An
  empty date shows "Note 1" but nothing is written to storage until you start typing.
- **Clipboard inbox.** After you turn capture on, future copied text and bitmaps are saved to that
  day's inbox, newest first, with copy and delete actions. Capture is off until you consent and can
  be paused at any time.
- **Unified search.** Literal search across note titles/bodies and captured clipboard text,
  including Korean and short 1-2 character queries, with deep links to the exact note or item.
- **Resident and private.** It lives in the tray, captures only after consent, makes no network
  calls, and stores everything locally.

## What it does NOT do

- No accounts, no cloud sync, no cloud backup, no telemetry, and no network access at runtime.
- **No application-managed encryption.** Your notes and clipboard data are stored as plaintext files
  readable by your Windows user account. See [docs/PRIVACY.md](docs/PRIVACY.md).
- No OCR, no AI processing, no screenshots, no keyboard hooks, and no file/HTML clipboard history.
- No macOS/Linux/mobile/web build and no x86 or Arm64 artifact.

## Supported systems

- x64 **Windows 11**.
- x64 **Windows 10 21H2 LTSC / Enterprise**.

No other Windows 10 edition, no 32-bit, and no Arm are supported for the first release.

## Where your data lives

Everything is under `%LocalAppData%\Daynote` (the SQLite database, image assets, and settings). This
folder is **not** encrypted by the app and is **not** copied anywhere. Updating, uninstalling, and
reinstalling the app all preserve it. See [docs/DATA_AND_RECOVERY.md](docs/DATA_AND_RECOVERY.md).

## Documentation

- [docs/PRIVACY.md](docs/PRIVACY.md) — what is captured, consent and pause, and the plaintext-local
  storage model.
- [docs/DATA_AND_RECOVERY.md](docs/DATA_AND_RECOVERY.md) — data location, update/uninstall/reinstall
  preservation, and how to back up or remove your data yourself.
- [docs/QA.md](docs/QA.md) — the deterministic UI/OS QA harness and the exact commands operators run.
- [docs/PACKAGING.md](docs/PACKAGING.md) — how the development MSIX is built and why data survives
  packaging operations.
- [DESIGN.md](DESIGN.md) — the WPF design-system implementation contract.
