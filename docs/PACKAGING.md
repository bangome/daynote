# Packaging (development MSIX)

This document covers how Daynote is packaged as an x64 MSIX for development and how
its user data survives update, uninstall, and reinstall. It is scoped to packaging
only; the full privacy, data-recovery, and QA docs are owned by Todo 12.

## What ships

- An **x64-only** development MSIX for `Daynote.Desktop` (self-contained publish).
- Package identity `Daynote.Dev`, publisher `CN=Daynote.Dev`, version `1.0.0.0`.
- A full-trust desktop app (`runFullTrust`).
- **No Windows startup task** in the manifest (removed 2026-09-08). "Open at login" is
  opt-in from the app's Settings and is an HKCU `Run` value written by
  `WindowsRunKeyStartupTaskGateway`; measured from inside the package identity, that
  write reaches the real hive, so Windows honours it. A manifest task as well would be a
  second switch in Settings → Apps → Startup that the Avalonia shell cannot read, and
  `PackageManifestPolicy` rejects one.
- The **MCP stdio server** (`Daynote.Mcp`), reachable through the app execution alias
  `daynote-mcp.exe`, declared as an extension on the app's own `<Application>`. It is
  deliberately **not** a second application: one hidden with `AppListEntry="none"` is a
  headless app, which the Store refuses without a `HeadlessAppBypass` entitlement. It ships in the same package on purpose: the alias
  is the only way a client process can launch it at all, since `%ProgramFiles%\WindowsApps`
  ACLs block the real path. It also shares the app's folder
  (`Daynote.Desktop\Daynote.Mcp.exe`) rather than getting its own, which keeps one copy
  of the .NET runtime in the package instead of two - 86 MB instead of 131 MB. The
  `_DaynoteCoLocateMcpServer` target does the merge and `Build-Package.ps1` verifies it.
  Settings -> AI integration registers the alias with Claude Desktop / Claude Code.
  See [MCP.md](MCP.md).

No x86/Arm64 artifact and no auto-update feed are produced here. For **Store**
submission see [STORE.md](STORE.md) (`scripts/Build-Package.ps1 -Store`).

## Where user data lives

Daynote's code writes under `%LocalAppData%\Daynote` (database, image/file assets,
settings), and **that is where a packaged install writes too**.

Measured on 2026-09-07: with the Store build (1.5.0.0) running and `DAYNOTE_DATA_ROOT`
unset, the database it touched was the real `%LocalAppData%\Daynote\daynote.db`, and
the package's `LocalCache` contained no Daynote folder. This package does not get its
`%LocalAppData%` writes redirected, despite carrying no `unvirtualizedResources`
capability. Earlier revisions of this file, `Package.appxmanifest`, `MCP.md`,
`PRIVACY.md` and `DATA_AND_RECOVERY.md` all stated the opposite; they were wrong.

**Uninstall leaves that folder alone** — measured 2026-09-08: `Remove-AppxPackage` deleted
the package container under `%LocalAppData%\Packages\<PFN>` and `%LocalAppData%\Daynote`
was untouched, same file count, same database hash. Back up anyway; the in-app
**Backup/Restore** (Settings → 백업 및 복원) costs nothing — see
[DATA_AND_RECOVERY.md](DATA_AND_RECOVERY.md). (Update and reinstall keep the data.)

> History: earlier development sideload builds declared the `unvirtualizedResources`
> restricted capability, on the understanding that without it the path would be
> redirected. That capability requires special Microsoft approval for the Store, so it
> was dropped — and, as measured above, nothing about the data path changed.

## Building the package

Authoring/CI (never signs, never installs):

```powershell
pwsh -File scripts/Build-Package.ps1 -Configuration Release -Architecture x64 `
    -EvidenceDir .omo\evidence\daynote-desktop-app\task-11 `
    -OutputDirectory artifacts\package
```

### Lock files and the win-x64 RID

Packaging publishes `Daynote.Desktop` and everything it references for `win-x64`, so restore records a
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
