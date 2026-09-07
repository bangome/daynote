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

## 1. Window chrome — DONE 2026-09-07

`Views/MainWindow.axaml` was built for macOS and said so: `Margin="84,0,16,0"`, the inset for the
traffic lights. On Windows the caption buttons are on the **right**, which is where this app keeps
its own actions, so the two collided — and the theme's drawn title bar printed a second "Daynote"
over the app's brand.

`MainWindow.Chrome.cs` now shapes the strip per platform:

- **macOS** keeps the 84px inset and draws no buttons; the traffic lights are the system's.
- **Windows** drops to a 16px inset, sets `WindowDecorations = BorderOnly` (which removes the theme's
  title bar while keeping the frame, shadow and resize grips), and draws its own minimize / maximize /
  close at the right, styled with the shell rather than the theme.

Drawing them costs nothing in behaviour. Avalonia 12 maps each element's
`WindowDecorationProperties.ElementRole` onto the Win32 hit-test codes, so Windows still drives them.
Verified by sending `WM_NCHITTEST` at each control on the running build:

```
close button      -> HTCLOSE
maximize button   -> HTMAXBUTTON     (this is what opens Snap Layouts on hover)
minimize button   -> HTMINBUTTON
empty strip       -> HTCAPTION       (drag to move, double-click to maximize)
search / gear / theme / brand -> HTCLIENT   (clicks reach the controls)
```

Two things worth remembering, because both were silent failures:

1. Interactive controls inside the strip need the `User` role, or the title-bar hit test swallows
   their clicks.
2. `TitleBarRow` needs `Background="Transparent"`. A Grid with no brush does not take part in hit
   testing, so the strip reported `HTCLIENT` and the window could not be dragged — the buttons
   worked, the drag did not.

**The taskbar overhang does not exist here.** The WPF shell needed `WM_GETMINMAXINFO` handling
(`ProductWindow.Maximize.cs`) because it maximized over the taskbar; measured on the same machine,
the Avalonia build's maximized *client* area is exactly the work area (1920×1032 on a 1080 screen
with a 48px taskbar). Nothing to port.

Still open in this area: Windows 11 rounded corners have not been checked, and neither has behaviour
across a DPI change or a monitor with different scaling.

## 2. Platform services that compile but have never run on Windows

`docs/MACOS_PORT.md` is candid that these are unexercised. Each needs to be run and tested on
Windows, and one still needs a decision:

| Concern | WPF (shipping) | Avalonia (Windows) | Status |
|---|---|---|---|
| Open at login | MSIX `StartupTask` | `WindowsRunKeyStartupTaskGateway` (HKCU Run key) | Settled by §3 — the Run key is correct for an unpackaged build. Never run on Windows; the "disabled by user" copy no longer applies |
| Global hotkey | `GlobalHotkeyService` + `HotkeyInterop` | `WindowsGlobalHotkeyService` (message-only window) | Written against the docs, never run on Windows |
| Single instance | Named mutex + user-ACL named pipe | `SingleInstanceCoordinator.ForCurrentUserPortable` (lock file + Unix socket) | **Undecided.** `Program.cs` calls the portable path on every OS; the Windows implementation exists in Infrastructure and the Avalonia app does not use it |
| Tray / resident | WinForms `NotifyIcon` | Avalonia `TrayIcon` | Needs Windows behaviour checked: balloon, context menu, double-click restore, hide-to-tray on close |
| Data root | `%LocalAppData%\Daynote` | `%LocalAppData%\Daynote` | Genuinely the same folder — measured, see §3.1 |

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

### 3.1 The data path — checked, and there is no migration to write

The obvious worry with leaving MSIX is that Store users' notes are locked inside the package's
private store. **Measured on 2026-09-07: they are not.** The packaged build writes to the same real
folder an unpackaged build reads, so switching installers does not strand anyone's data and no
import step is needed.

How it was checked, since three files in this repo claim otherwise:

```
Package:  BreadJinhwaJeong.-Daynote_jz227wzmk3a2g   (Store build 1.5.0.0, installed)
Ran:      C:\Program Files\WindowsApps\...\Daynote.App\Daynote.App.exe   (only Daynote process)
Started:  09:52:21

%LocalAppData%\Packages\<PFN>\LocalCache\...     no Daynote folder at all
%LocalAppData%\Daynote\daynote.db-wal             modified 09:52:28   ← the packaged app wrote here
```

`DAYNOTE_DATA_ROOT` was unset at both user and machine scope, so nothing was overriding the default.

**Three places in the repo state the opposite and are now known to be wrong.** They predate this
measurement and should be corrected on their own, outside this plan:

| File | Claim | Reality |
|---|---|---|
| `packaging/.../Package.appxmanifest` header | "File-system virtualization is LEFT ENABLED, so the app's writes to `%LocalAppData%\Daynote` are transparently redirected ... into this package's per-app store" | No redirection observed |
| `docs/MCP.md` §"It gives the server the package identity" | An MCP server started outside the package "would open an empty second database" | It would open the same database. The *other* reason for the alias — `%ProgramFiles%\WindowsApps` ACLs make the real executable unreachable — still holds |
| `docs/DATA_AND_RECOVERY.md` | "uninstalling clears the data" | **Untested.** If the data is outside the package, an uninstall probably leaves it, but confirming that means uninstalling the Store build. Do not assume either way until someone checks |

What survives from the original worry:

- The **uninstall** question above. Until it is answered, the cutover release should still prompt for
  a Backup on first run — cheap, and it covers the case where the claim turns out to be right.
- Whichever installer is chosen must keep writing to `%LocalAppData%\Daynote`. Changing the location
  is what would actually strand data, and there is now no reason to.

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

Each phase leaves the tree shippable, and WPF stays the Windows product until phase 6. There is
no data-migration phase: §3.1 establishes that both builds read the same folder.

1. ~~**Chrome.**~~ **Done** (§1): platform-conditional title bar, app-drawn caption buttons with
   native hit-test roles, drag and Snap Layouts verified. Rounded corners and DPI changes remain.
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
- **Does uninstalling the Store package delete `%LocalAppData%\Daynote`?** (§3.1). Answering it
  needs one throwaway machine and one uninstall. It decides how loudly the cutover has to warn.
- **Does the cutover wait for full parity, or ship in stages?** A staged Windows release means two
  shells in the wild at once, which the single-instance mismatch (§2) currently breaks.
- **What happens to the showcase evidence pipeline?** Rebuild it on Avalonia, or retire it and keep
  only binding/composition tests. It is a large body of work either way.
- **Does the Store listing get withdrawn, or left up pointing at the last MSIX?** Leaving it stale
  means users keep installing a build that will not receive updates.
