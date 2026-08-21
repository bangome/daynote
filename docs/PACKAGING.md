# Packaging (development MSIX)

This document covers how Daynote is packaged as an x64 MSIX for development and how
its user data survives update, uninstall, and reinstall. It is scoped to packaging
only; the full privacy, data-recovery, and QA docs are owned by Todo 12.

## What ships

- An **x64-only** development MSIX for `Daynote.App` (self-contained publish).
- Package identity `Daynote.Dev`, publisher `CN=Daynote.Dev`, version `1.0.0.0`.
- A full-trust desktop app (`runFullTrust`).
- A **Windows startup task** (`TaskId=DaynoteStartupTask`), **disabled by default and
  opt-in**: the app never auto-enables it (Store policy). The user turns it on from
  Settings, and user/policy-disabled states are never overridden.
- The **MCP stdio server** (`Daynote.Mcp`) as a second, hidden entry point
  (`AppListEntry="none"`), reachable through the app execution alias
  `daynote-mcp.exe`. It ships in the same package on purpose: only then does the
  server inherit the package identity, and with it the redirected data path below, so
  it opens the very same database as the app. Settings -> AI integration registers the
  alias with Claude Desktop / Claude Code. See [MCP.md](MCP.md).

No x86/Arm64 artifact and no auto-update feed are produced here. For **Store**
submission see [STORE.md](STORE.md) (`scripts/Build-Package.ps1 -Store`).

## Where user data lives (packaged storage)

Daynote's code writes under `%LocalAppData%\Daynote` (database, image/file assets,
settings). File-system virtualization is **left enabled**, so for a packaged install
the OS transparently redirects those writes into the package's per-app store. This is
the Store-standard model and needs no app code change.

Consequence: **uninstalling removes the app's data.** Use the in-app **Backup/Restore**
(Settings → 백업 및 복원) before uninstalling or moving machines — see
[DATA_AND_RECOVERY.md](DATA_AND_RECOVERY.md). (Update/reinstall keep the data.)

> History: earlier development sideload builds disabled virtualization and declared
> the `unvirtualizedResources` restricted capability to keep the real, un-redirected
> `%LocalAppData%\Daynote` path across uninstall. That capability requires special
> Microsoft approval for the Store, so it was removed in favor of the standard model
> above.

## Building the package

Authoring/CI (never signs, never installs):

```powershell
pwsh -File scripts/Build-Package.ps1 -Configuration Release -Architecture x64 `
    -EvidenceDir .omo\evidence\daynote-desktop-app\task-11 `
    -OutputDirectory artifacts\package
```

### Lock files and the win-x64 RID

Packaging publishes `Daynote.App` and everything it references for `win-x64`, so restore records a
win-x64 target in those projects' `packages.lock.json`. `Directory.Build.props` therefore declares
`<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` repo-wide: without it those lock files named a RID
their project did not, and the next locked-mode restore failed with NU1004 - so a packaging run left
the repo unable to restore. With it, the committed lock files satisfy both restores and a packaging run
leaves the working tree unchanged. Step 1's locked restore is the guard: if the lock files ever drift
again, packaging fails there rather than silently rewriting them.

The script always performs the locked restore, `-warnaserror` build, and the
self-contained `win-x64` publish. It builds the `.msix` only when a full MSBuild with
the DesktopBridge targets is present (Visual Studio "Windows application packaging"
component or the MSIX Packaging Tools); otherwise it completes the publish and prints
why packaging was skipped. The `.wapproj` is intentionally **not** part of
`Daynote.sln` because `dotnet build` cannot resolve those targets.

## Production inputs (external, NOT committed)

The production publisher identity, the code-signing certificate and its password,
and any App Installer update URI are supplied out-of-band at release time. They are
never committed to this repository. CI produces unsigned/development artifacts only.

---

## DEFERRED machine-mutating steps (run in a disposable VM only)

Per the 2026-07-20 user decision, the following steps mutate the machine (certificate
store, installed packages) and are **not** run during authoring. Run them in a
disposable Windows VM. The scripts support all of them; they are listed here as the
exact commands to run later.

1. **Create + trust a disposable dev certificate** (writes to `Cert:\CurrentUser\My`
   and a trust store):

   ```powershell
   # Dry-run first (no mutation):
   .\scripts\New-DevelopmentCertificate.ps1 -WhatIf
   # Real run (VM), then trust the public cert (elevated):
   .\scripts\New-DevelopmentCertificate.ps1 -OutputDirectory 'D:\daynote-cert'
   Import-Certificate -FilePath 'D:\daynote-cert\Daynote.Dev.cer' `
       -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

2. **Build + sign the MSIX:**

   ```powershell
   .\scripts\Build-Package.ps1 -Sign -CertificatePath 'D:\daynote-cert\Daynote.Dev.pfx' `
       -EvidenceDir .omo\evidence\daynote-desktop-app\task-11
   ```

3. **Install / upgrade / uninstall / reinstall data-preservation QA** (this is the
   Todo 11 `Add-AppxPackage` QA; deferred):

   ```powershell
   # Install:
   Add-AppxPackage -Path .\artifacts\package\Daynote.Dev_1.0.0.0_x64.msix
   # Create a data marker, then confirm it lives in the REAL LocalAppData path:
   New-Item -ItemType File -Path "$env:LOCALAPPDATA\Daynote\qa-marker.txt" -Force
   # Upgrade (rebuild with a higher Version, then):
   Add-AppxPackage -Path .\artifacts\package\Daynote.Dev_1.0.1.0_x64.msix
   # Uninstall then reinstall; the marker + note/image data must still be present:
   Get-AppxPackage Daynote.Dev | Remove-AppxPackage
   Add-AppxPackage -Path .\artifacts\package\Daynote.Dev_1.0.0.0_x64.msix
   Test-Path "$env:LOCALAPPDATA\Daynote\qa-marker.txt"   # expect: True
   ```

4. **Cleanup after QA** (VM):

   ```powershell
   Get-AppxPackage Daynote.Dev | Remove-AppxPackage
   Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=Daynote.Dev' | Remove-Item
   # Confirm both are empty; keep the intentional data marker if verifying preservation.
   Get-AppxPackage Daynote.Dev
   ```

`scripts/Build-Package.ps1 -Install` also performs the install but refuses to run
unless you additionally pass `-IAcceptMachineMutation`, so it can never install by
accident during authoring.
