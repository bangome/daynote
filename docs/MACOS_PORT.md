# macOS port (Avalonia)

Daynote is being brought to macOS with [Avalonia UI](https://avaloniaui.net). The domain and
infrastructure layers are shared with the Windows app unchanged; the WPF view layer is being
re-implemented as `src/Daynote.Desktop`, an Avalonia application that runs on macOS today and is
meant to replace the WPF shell on Windows once it reaches feature parity.

## Layout

| Project | Target | Role |
|---|---|---|
| `Daynote.Core` | `net10.0` | Domain, use cases, ports. Unchanged. |
| `Daynote.Infrastructure` | `net10.0` + `net10.0-windows` | SQLite, sync crypto, backup, MCP registration. The Windows flavour keeps DPAPI, the named-pipe single instance and the MSIX startup task; the portable flavour adds their macOS/Linux counterparts. |
| `Daynote.Mcp` | `net10.0` + `net10.0-windows` | The MCP stdio server, built in both flavours so each app bundles one consistent graph. |
| `Daynote.Presentation` | `net10.0` | The framework-neutral presentation layer both apps reference: note workspace, calendar, todo/favorites/tags/files/search/timeline view models, the shortcut model (`Hotkey`, `HotkeyKey`, `ConfigurableShortcuts`), the onboarding tutorial, localisation, date display, options. Namespaces stay `Daynote.App.*`. |
| `Daynote.App` | `net10.0-windows` | The WPF app: views, converters, Win32 services (`HotkeyInterop`, `GlobalHotkeyService`), the WPF settings and account view models. |
| `Daynote.Desktop` | `net10.0` | The Avalonia app: views, theme, `DesktopShellViewModel`, `DesktopSettingsViewModel`, and the macOS platform services. |
| `tests/Daynote.Infrastructure.Portable.Tests` | `net10.0` | Tests for the portable implementations; run on macOS, Linux and Windows. |

## Platform services

| Concern | Windows (WPF) | macOS (Avalonia) |
|---|---|---|
| Single instance | Named mutex + current-user ACL named pipe | Exclusive lock file + Unix domain socket under `$TMPDIR/.daynote` (`SingleInstanceCoordinator.ForCurrentUserPortable`) |
| Session secret (`credentials.dat`) | DPAPI (`DpapiSecretProtector`) | AES-GCM under a key held in the login Keychain (`MacKeychainSecretProtector`) |
| Open at login | MSIX `StartupTask` | `~/Library/LaunchAgents/cc.arachat.daynote.plist` (`LaunchAgentStartupTaskGateway`) |
| Data root | `%LocalAppData%\Daynote` | `~/Library/Application Support/Daynote` |
| Claude Desktop config | `%AppData%\Claude\claude_desktop_config.json` | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Resident presence | WinForms `NotifyIcon` | Avalonia `TrayIcon` (status bar) + Dock reopen |
| Global hotkeys | Win32 `RegisterHotKey` | Carbon `RegisterEventHotKey` (`MacGlobalHotkeyService`); Ctrl→⌃, Alt→⌥, Win→⌘ |
| In-app shortcuts | WPF `KeyBinding`s from `ConfigurableShortcuts` | Avalonia `KeyBinding`s from the same `ConfigurableShortcuts` |

All of these sit behind the ports `Daynote.Core` already defined, so the view models never see the
difference. `DAYNOTE_DATA_ROOT` overrides the data root on every OS, as before.

## Building and running on a Mac

```bash
dotnet build src/Daynote.Desktop/Daynote.Desktop.csproj
dotnet run --project src/Daynote.Desktop
dotnet test tests/Daynote.Core.Tests tests/Daynote.Infrastructure.Portable.Tests
```

The whole solution, including the WPF app and its tests, also **compiles** on a Mac (the SDK downloads
the Windows reference packs; `Directory.Build.props` turns on `EnableWindowsTargeting` off-Windows).
Only Windows can run the WPF app and its test projects.

## Packaging

```bash
scripts/Build-MacApp.sh -r osx-arm64      # or -r osx-x64
open dist/mac/Daynote.app
```

Publishes self-contained, wraps the output in `Daynote.app` with an `.icns` rendered from the brand
favicon and an `Info.plist` (bundle id `cc.arachat.daynote`), then signs it. Without
`DAYNOTE_SIGN_IDENTITY` the signature is ad-hoc (runs here, Gatekeeper warns elsewhere); with a
Developer ID identity it signs with the hardened runtime and a timestamp, ready for `notarytool`.
`Daynote.Mcp` ships inside `Contents/MacOS`, which is what the settings panel registers with Claude.

## Release checklist (what only a person with the Apple account can do)

1. Enroll in the Apple Developer Program and, in Xcode → Settings → Accounts (or developer.apple.com),
   create a **Developer ID Application** certificate so it lands in the login keychain.
2. Find its name: `security find-identity -v -p codesigning` → `Developer ID Application: <Name> (<TEAMID>)`.
3. Store notary credentials once (an app-specific password from appleid.apple.com):
   `xcrun notarytool store-credentials daynote-notary --apple-id <email> --team-id <TEAMID> --password <app-specific>`
4. Build and notarize:
   ```bash
   DAYNOTE_SIGN_IDENTITY="Developer ID Application: <Name> (<TEAMID>)" scripts/Build-MacApp.sh -r osx-arm64
   scripts/Notarize-MacApp.sh -a dist/mac/Daynote.app
   ```
   Repeat with `-r osx-x64` for Intel Macs (or ship two zips). The output `dist/mac/Daynote.zip` is what users download.
5. Decide whether the Mac App Store is a target. It would add sandboxing (the LaunchAgent and
   `~/Library/Application Support` paths change) and an App Store Connect listing; not needed for direct download.

## Status

Done in phase 1:

- Infrastructure and MCP server multi-targeted; portable single instance, Keychain-sealed session
  store, LaunchAgent login item, platform data roots, with tests.
- Avalonia shell: calendar, per-day note list, editor with autosave, light/dark theme persisted
  under the same setting key as the WPF app, tray icon, hide-on-close, flush-guarded Quit, single
  instance, first-run sample note, language resolution.
- macOS CI workflow (`.github/workflows/macos.yml`).

Done in phase 2:

- `Daynote.Presentation` extracted; the WPF app and the Avalonia app compile the same view models.
  `IFilePicker` went async and `IThumbnailLoader` replaced the WPF `ImageSource` dependency.
- Avalonia shell: unified search dropdown, right rail (todo with due dates and toggling, favorites,
  tags with occurrences, files with thumbnails and the native open panel), tag chips on the editor,
  timeline view, settings (language, open at login, storage location, Claude MCP registration).
- `scripts/Build-MacApp.sh` and the macOS workflow produce a signed `Daynote.app`.

Done in phase 3:

- Shortcut model made framework-neutral: `HotkeyKey`/`HotkeyModifiers` share WPF's and Avalonia's
  numeric values, so both apps cast; persisted strings ("Ctrl+Alt+D") are unchanged.
- macOS global summon hotkey and the fixed ⌥` quick-note chord (Carbon), configurable in-app
  shortcuts as Avalonia key bindings, chord capture/reassign/reset in Settings.
- Sticky (post-it) windows and the first-run tutorial (auto-shown once, re-openable from Settings).

Done in phase 4:

- Settings: backup to zip and staged restore (native save/open panels; the app quits and relaunches
  to apply a restore).
- Account and sync UI (`AccountPanel`): sign-in, status and sync-now, note lock with passphrase,
  one-time recovery key (copy/save), subscription and upgrade. `AccountViewModel` moved to
  Presentation; the recovery-key exporter port went async. Registered only when a sync endpoint is
  configured (`DAYNOTE_SYNC_ENDPOINT` or the build flag), exactly as in the WPF app.
- `scripts/Notarize-MacApp.sh` and the release checklist above.
- Windows platform services for the Avalonia build: Run-key login item and a Win32 `RegisterHotKey`
  service on a message-only window. `dotnet publish -r win-x64` produces `Daynote.Desktop.exe`;
  these two services compile but have only been exercised on macOS so far.

Next phases:

1. **Release.** Run the checklist above; add an update channel (Sparkle or a plain download page).
2. **Windows on Avalonia.** Run the win-x64 build on a Windows machine, verify the login item and
   hotkeys, then decide when the Avalonia app replaces the WPF one (MSIX packaging of the Avalonia
   build, data-root compatibility is already identical).
3. **Parity leftovers.** Editor extras from the WPF card (Markdown toolbar, file-link/URL click,
   drag-and-drop of files into the body, tag-hit highlighting), the left-panel collapse affordance,
   and a high-contrast palette.
