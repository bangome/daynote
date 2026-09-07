# Retiring the WPF shell: what Windows-on-Avalonia needs

`docs/MACOS_PORT.md` states the intent — the Avalonia app is "meant to replace the WPF shell on
Windows once it reaches feature parity". This file is the list of what stands between here and
there, written after actually running `Daynote.Desktop` on Windows rather than from the plan.

## Where it already stands (measured 2026-09-07)

`dotnet build src/Daynote.Desktop/Daynote.Desktop.csproj -r win-x64` → 0 warnings, 0 errors.
The resulting `Daynote.Desktop.exe` launches on Windows 11 and draws a working 1256×788 window:
calendar, day note list, editor with tags, right rail, search bar, first-run tutorial. So this is
not a port that has to start; it is one that has to be finished.

The gap is not the domain. `Daynote.Core`, `Daynote.Infrastructure` and `Daynote.Presentation` are
already shared, and `Daynote.Presentation` holds every view model both apps bind to. What is missing
is the Windows half of the *shell*: chrome, platform services, packaging, the design system, and the
test harness that currently guards all of it.

## 1. Window chrome — the blocker you can see

`Views/MainWindow.axaml` is built for macOS and says so:

```xml
ExtendClientAreaToDecorationsHint="True"
ExtendClientAreaTitleBarHeightHint="52"
...
<Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Margin="84,0,16,0">
```

The `84` is the inset for macOS traffic lights. On Windows there are no traffic lights on the left —
there are caption buttons on the **right**, which is where the app draws its own actions. The
running build shows the result: the OS title bar and the app header stack, and the timeline/settings/
close controls overlap in the top-right corner.

Needed:

- A platform-conditional title bar: 84px left inset and no caption buttons on macOS; zero left inset
  and minimize/maximize/close on Windows. WPF does this with `WindowChrome`; Avalonia's equivalent is
  `ExtendClientAreaChromeHints` plus hit-test regions.
- Snap and Aero Snap behaviour, including the maximize fix already solved once in WPF
  (`ProductWindow.Maximize.cs`): a borderless window maximizes over the taskbar unless
  `WM_GETMINMAXINFO` is answered with the monitor's work area. Avalonia will need the same handling
  or a demonstration that it already does it.
- Windows 11 rounded corners (`DwmSetWindowAttribute`), which the WPF shell asks for explicitly.

## 2. Platform services that compile but have never run on Windows

`docs/MACOS_PORT.md` is candid that these are unexercised. Each needs to be run and tested on
Windows, and one still needs a decision:

| Concern | WPF (shipping) | Avalonia (Windows) | Status |
|---|---|---|---|
| Open at login | MSIX `StartupTask` | `WindowsRunKeyStartupTaskGateway` (HKCU Run key) | Settled by §3 — the Run key is correct for an unpackaged build. Never run on Windows; the "disabled by user" copy no longer applies |
| Global hotkey | `GlobalHotkeyService` + `HotkeyInterop` | `WindowsGlobalHotkeyService` (message-only window) | Written against the docs, never run on Windows |
| Single instance | Named mutex + user-ACL named pipe | `SingleInstanceCoordinator.ForCurrentUserPortable` (lock file + Unix socket) | **Undecided.** `Program.cs` calls the portable path on every OS; the Windows implementation exists in Infrastructure and the Avalonia app does not use it |
| Tray / resident | WinForms `NotifyIcon` | Avalonia `TrayIcon` | Needs Windows behaviour checked: balloon, context menu, double-click restore, hide-to-tray on close |
| Data root | `%LocalAppData%\Daynote`, redirected by MSIX | `%LocalAppData%\Daynote`, real path | Same code, **different folder in practice** — see §3.1 |

The single-instance one matters beyond tidiness: the existing app's mutex is `Local\Daynote-<SID>`,
so a WPF build and an Avalonia build **will not see each other** during a transition period, and two
copies would open the same SQLite file.

## 3. Packaging — decided: unpackaged installer

**Decision (2026-09-07): Windows ships as an unpackaged installer, not MSIX.** MSIX and the Store
listing are retired with the WPF shell.

What that settles, and what it costs:

| | Effect |
|---|---|
| Login item | `WindowsRunKeyStartupTaskGateway` is the right implementation, not dead code. It cannot tell that a user disabled the entry in Task Manager, so `SettingsStartupDisabledByUserText` ("turned off in Windows startup app settings") no longer applies on Windows and the copy needs revisiting |
| MCP registration | **Simpler.** The MSIX alias exists only because the real executable sits under `%ProgramFiles%\WindowsApps`, whose ACLs a client process cannot traverse. An unpackaged build registers its own path |
| Store policy | `docs/STORE.md` §10.8 (third-party commerce, trial disclosure, Partner Center declarations) stops applying. Paddle checkout is unconstrained |
| Auto-update | **Gone, and has to be rebuilt.** The Store did this. Without a replacement (Velopack, Squirrel, or a homegrown check) users are on manual reinstall |
| Code signing | An unsigned `.exe` raises SmartScreen on every download. An OV or EV certificate is now required, where the Store previously re-signed at ingestion |
| Listing, ratings, install base | Not carried over |

### 3.1 The data hazard — do this before anything else

This is the one irreversible item in the whole migration.

`Package.appxmanifest` leaves MSIX file-system virtualization **enabled** (a deliberate change from
the older sideload build, recorded in its header comment). So the two builds do not read the same
folder:

```
Store build (MSIX)   %LocalAppData%\Packages\<PackageFamilyName>\LocalCache\Local\Daynote
Unpackaged build     %LocalAppData%\Daynote            ← DaynoteDataRoot.Default()
```

An unpackaged build installed over a Store install therefore opens an **empty database**. To the
user that reads as "the update deleted my notes". Worse, `docs/DATA_AND_RECOVERY.md` records that
uninstalling the Store package *clears its data* — so a user who reacts by removing the old app
destroys the only copy. Nothing in `src/` currently references the packaged path; there is no
migration code today.

Required, and required first:

1. **One-time import.** On first run, if `%LocalAppData%\Daynote` holds no database, look for the
   packaged path and **copy** (never move) it across. A failed copy must leave the original intact.
   The `PackageFamilyName` is `Name` plus a hash of `Publisher` — from the manifest, `Name` is
   `BreadJinhwaJeong.-Daynote` and `Publisher` is `CN=7FDB7ABF-3343-4BA9-9F0C-C601ABED42EE`. The
   hash cannot be written by hand; read the real value with `Get-AppxPackage` on a machine that has
   the Store build installed, and pin it in a test.
2. **Enforce the order.** Install the new build → confirm the import → *then* remove the Store app.
   This has to be said inside the app, not only in release notes, because the destructive order is
   the intuitive one.
3. **Back up first.** The in-app Backup already exists and is the fallback when the import finds
   nothing. The cutover release should prompt for one on first run.

Also worth deciding here: whether the unpackaged build keeps writing to `%LocalAppData%\Daynote` (it
does today, and the import lands there) or moves somewhere else. Keeping it means a user who once ran
a dev or sideload build already has data in the right place.

## 4. Design system — the largest single piece of work

| | WPF | Avalonia |
|---|---|---|
| Theme XAML | ~2,300 lines across 18 dictionaries | 330 lines across 2 |
| Views | 20 `.xaml` | 3 `.axaml` |

The v3 renewal, the palettes (light/dark/high-contrast), the button/panel/typography systems, the
calendar heat dots, the account styles — none of that exists on the Avalonia side, which has its own
simpler Fluent-based look. Reaching parity means porting the design system, not translating files
one for one: Avalonia has `Styles`/selectors rather than WPF's implicit-key `Style`/`ControlTemplate`
model, so the structure differs even where the values carry over.

Concretely missing from the Avalonia shell today:

- The v3 palette and pill/card/typography system, light **and** dark **and** high contrast.
- Calendar heat dots. `CalendarDayCellViewModel.ActivityLevel` is already in `Daynote.Presentation`,
  but `MainWindow.axaml` still binds `NoteCountText` — the old number badge.
- The account surfaces from `docs/design-renewal/Daynote Account.dc.html`: the titlebar avatar with
  its sync dot, the account menu, and the 520px account window. Avalonia has a single 200-line
  `AccountPanel.axaml` covering sign-in, status, lock, recovery key and subscription in one column.
- Sticky-note windows exist on both, but the Avalonia one is 58 lines against the WPF version's
  styling; the always-on-top, per-note colour and live two-way edit need checking.
- Settings: the WPF panel is the v3 modal (`SettingsView.xaml`, 313 lines, plus a 267-line dictionary).

## 5. Tests and the showcase harness

This is the part most likely to be underestimated. `tests/Daynote.App.Tests` is 60 files and 362
tests, and a large share of them are **WPF-specific by construction**:

- Composition tests that build `ProductWindow` and assert zero data-binding errors.
- `DesignResourceTests` (light/dark key parity, raw ARGB confined to palette files, every
  `Daynote.*` resource reference resolves).
- The showcase capture pipeline (`ShowcaseCapture`, `PrimitiveFixtureFactory.*`,
  `ShowcaseInteraction*`) — render-to-bitmap evidence with a build-freshness check.
- Render tests using `RenderTargetBitmap` (`CalendarDayCircleTests`, `CompactEditorRenderingTests`,
  `PrimitiveStressRenderingTests`).
- `tests/Daynote.UiQa.Tests` on top of that.

None of it transfers automatically. Avalonia has its own headless test platform
(`Avalonia.Headless`), which can host controls and render, so the *kind* of testing is available —
but every fixture, the resource-parity rules and the showcase evidence format have to be rebuilt
against it. Until that exists, moving Windows to Avalonia means shipping Windows with materially
less automated UI coverage than it has today.

## 6. Suggested order

Each phase leaves the tree shippable, and WPF stays the Windows product until phase 6.

0. **Data migration (§3.1).** The import path, the ordering guard, and a test that pins the
   packaged path. First because it is the only step whose failure destroys user data; everything
   else can be redone.
1. **Chrome.** Platform-conditional title bar, caption buttons, work-area maximize, rounded corners.
   Cheapest fix with the most visible payoff, and it makes everything after it demoable on Windows.
2. **Platform services.** Run the hotkey, login item, tray and single instance on Windows; pick the
   single-instance implementation; revisit the startup copy that assumes `StartupTask` semantics;
   write tests in `Daynote.Infrastructure.Portable.Tests` or a new Windows-flavoured sibling.
3. **Test harness.** Stand up `Avalonia.Headless`: composition/binding-error tests first, then the
   resource-parity rules. Do this *before* the design port so the port has a net under it.
4. **Design system.** Port the palettes and the v3 primitives, then the screens in the order they are
   used: shell → settings → account. Heat dots come free once the palette exists.
5. **Distribution.** Installer, code-signing certificate, and an update mechanism — all three are new
   work that MSIX used to cover. Retire the wapproj and the Store submission scripts.
6. **Cut over.** Ship the Avalonia build to Windows, keep `Daynote.App` in the tree for one release
   as a fallback, then delete it and fold `Daynote.Desktop` back into a single app project.

## 7. Still open

- **Update mechanism.** Velopack, Squirrel, or a homegrown "check and download" — this is the
  largest thing MSIX was doing for free, and it gates the cutover.
- **Signing certificate.** OV or EV, and who holds it. EV clears SmartScreen immediately; OV builds
  reputation over time.
- **Does the cutover wait for full parity, or ship in stages?** A staged Windows release means two
  shells in the wild at once, which the single-instance mismatch (§2) currently breaks.
- **What happens to the showcase evidence pipeline?** Rebuild it on Avalonia, or retire it and keep
  only binding/composition tests. It is a large body of work either way.
- **Does the Store listing get withdrawn, or left up pointing at the last MSIX?** Leaving it stale
  means users keep installing a build that will not receive updates.
