# Desktop QA

Daynote's acceptance evidence comes from two automated lanes plus a small set of deferred,
machine-mutating scenarios that run only in a disposable VM.

- **Unit / integration tests** (`dotnet test Daynote.sln`) cover the domain, storage, search,
  normalization, and view-model logic.
- **Deterministic desktop QA** (`qa/Daynote.UiQa`) drives the **real product** through Windows UI
  Automation and observes real artifacts (the automation tree, the SQLite database, the filesystem,
  process counts).
- **Deferred VM scenarios** exercise the system clipboard, a 20-process launch fleet, and the MSIX
  install/upgrade/uninstall lifecycle. These mutate the host and are run only in a disposable VM.

## Why a separate harness (and not the showcase host)

The `--showcase` host in `Daynote.App` renders isolated primitive fixtures for visual and
interaction fidelity; it does not exercise the real SQLite database, clipboard listener, or tray
lifecycle end to end. The plan's product-behavior scenarios — empty-Note-1 persistence,
reorder/delete/restart, receipt-time date, clipboard contention, 20-launch single-instance, and MSIX
data preservation — can only be observed by launching the shipping `Daynote.App.exe` and inspecting
the real database and filesystem. `qa/Daynote.UiQa` therefore drives the real product via UI
Automation and is the product-behavior lane; the showcase host remains the primitive/visual-fidelity
lane. The two do not overlap.

## Isolation and safety

Scenarios run the real app against a **disposable data root** nested inside the real Daynote root:

```
%LocalAppData%\Daynote\.uiqa\<run-id>
```

The product is pointed there through the `DAYNOTE_DATA_ROOT` environment variable (a QA-only seam;
unset in normal use, where the app uses `%LocalAppData%\Daynote`). Nesting under the real root means
the MSIX preservation checks exercise the exact unvirtualized location the product ships with, while
the `.uiqa` namespace guarantees your own notes are never touched.

Cleanup is strictly namespaced. The harness and `Run-DesktopScenarios.ps1` only ever delete inside
`%LocalAppData%\Daynote\.uiqa`; they never delete `%LocalAppData%\Daynote` itself, your data, or any
arbitrary path, and never perform a recursive delete outside that namespace. Every evidence file is
payload-redacted: the harness writes row counts and structural metadata, never note bodies,
clipboard text, or image bytes.

## Building the harness

```powershell
dotnet build Daynote.sln -c Release -warnaserror
```

`qa/Daynote.UiQa` is part of `Daynote.sln`. A plain build never launches anything, and neither do
`--help`, `--list`, or `--inspect`.

## Launch-free commands (safe anywhere)

```powershell
# List the scenario registry as JSON (names, what each mutates, deferred flag):
dotnet qa/Daynote.UiQa/bin/Release/net10.0-windows10.0.19041.0/Daynote.UiQa.dll --list

# Payload-redacted, read-only inspection of a database:
pwsh -File qa/InspectDaynoteDatabase.ps1 `
    -DatabasePath "$env:LOCALAPPDATA\Daynote\daynote.db" `
    -EvidenceDir .omo\evidence\daynote-desktop-app\task-12\inspect
```

## Scenario registry

| Scenario | What it proves | Runs the real app? | Deferred to VM? |
| --- | --- | --- | --- |
| `calendar-notes` | Empty Note 1, add/reorder/delete, Markdown survives date switch + restart | yes | drives live app |
| `empty-note-1` | No `notes` row until first edit | yes | drives live app |
| `notes-reorder-delete-restart` | Stable ids + contiguous order across restart | yes | drives live app |
| `unified-search` | Literal queries incl. punctuation/SQL metachars, deep links | yes | drives live app |
| `korean-short-search` | 1-2 character Korean substring search | yes | drives live app |
| `orphan-missing-files` | Startup reconciliation of orphan/`.tmp`; missing image stays non-crashing | yes | drives live app |
| `hide-pause-quit` | Hide-to-tray residency, pause capture, explicit quit | yes | drives live app |
| `payload-redacted-diagnostics` | Sentinel note body never appears in any evidence file | yes | drives live app |
| `midnight-receipt-date` | Receipt date fixed before retry across midnight | yes | **writes system clipboard** |
| `clipboard-contention` | 20/40/80/160/320 ms retry yields one item, UI responsive | yes | **writes system clipboard** |
| `duplicate-sequence-payload` | Coalesce same-sequence; dedupe A,A; keep A,B,A | yes | **writes system clipboard** |
| `dib-alpha-image-sharing` | DIB/DIBV5 equivalence → one shared asset | yes | **writes system clipboard** |
| `twenty-launches` | 20 concurrent launches → one primary process | yes | **launches process fleet** |
| `startup-policy` | Startup task defaults disabled; reports OS policy states | yes | **requires packaged app** |
| `msix-update-uninstall-reinstall` | Data preserved across update/uninstall/reinstall | yes | **requires packaged app** |

`--list` reports the same information as machine-readable JSON, including the
`DeferredOnAuthoringMachine` flag.

## Running the full suite (disposable VM only)

`Run-DesktopScenarios.ps1` drives the installed packaged app through every scenario and returns 0
only when all binary observables pass and the QA namespace was cleaned:

```powershell
pwsh -File qa/Run-DesktopScenarios.ps1 `
    -PackagePath .\artifacts\package\Daynote.Dev_1.0.0.0_x64.msix `
    -EvidenceDir .omo\evidence\daynote-desktop-app\task-12
```

It writes `summary.json`, per-scenario evidence (action logs, screenshots, `database.json`), and a
`cleanup-receipt.json` recording that only the `.uiqa` namespace was removed.

## DEFERRED commands (run only in a disposable VM)

Per the 2026-07-20 user decision the following mutate the host and are **not** run on the authoring
machine. The harness and scripts are authored and compile-verified; run these later in a VM.

### 1. Clipboard-writing scenarios

```powershell
# Text contention (app listening, capture consented):
pwsh -File qa/PublishThenHoldClipboard.ps1 -Text '경합-test' -HoldMs 200

# Image DIB then DIBV5 of identical content → expect a single shared asset:
pwsh -File qa/PublishClipboardImage.ps1 -Format DIB   -Width 32 -Height 32 -EvidenceDir <dir>
pwsh -File qa/PublishClipboardImage.ps1 -Format DIBV5 -Width 32 -Height 32 -EvidenceDir <dir>

# Then drive/observe via the harness (writes the disposable .uiqa data root only):
dotnet qa/Daynote.UiQa/bin/Release/net10.0-windows10.0.19041.0/Daynote.UiQa.dll `
    --scenario clipboard-contention --evidence <dir>
```

### 2. 20-launch single-instance proof

```powershell
1..20 | ForEach-Object { Start-Process .\artifacts\package\Daynote.Dev_1.0.0.0_x64.msix }
(Get-Process Daynote -ErrorAction SilentlyContinue).Count   # expect: 1
```

### 3. MSIX install / upgrade / uninstall / reinstall data preservation

The exact `Add-AppxPackage` sequence (install → marker → upgrade → uninstall → reinstall) and the
certificate steps live in [PACKAGING.md](PACKAGING.md). Cleanup afterward removes the test package
and dev certificate and confirms the intentional data marker is preserved.

### 4. Startup policy states

Install the package, then inspect the `DaynoteStartupTask` state (Enabled / Disabled /
DisabledByUser / policy) via Windows Settings → Startup apps and the app's own settings surface. The
task defaults **disabled**; the app never auto-enables it.
