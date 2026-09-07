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

## 2. Platform services — DONE 2026-09-07

All four were written against the docs and never run on Windows. Each was exercised on a real
machine; one was wrong.

| Concern | Windows implementation | Verified |
|---|---|---|
| Single instance | Named mutex + pipe, via the new `SingleInstanceCoordinator.ForCurrentUserOnThisPlatform` | Yes — all three pairings |
| Open at login | `WindowsRunKeyStartupTaskGateway`, moved into `Daynote.Infrastructure.Startup` | Yes — four tests against the real `HKCU\...\Run` |
| Global hotkey | `WindowsGlobalHotkeyService` | Yes — registration and end-to-end summon. **Bug found and fixed** |
| Tray / hide-on-close | Avalonia `TrayIcon` | Partly — hide-to-tray verified; the icon's own menu is not machine-checkable |

### Single instance: the mutex, not the lock file

`Program.cs` called the portable lock file on every OS. On Windows it now takes the named mutex,
because that is what the WPF shell holds under the same base name — so while both builds exist, a
second launch of either activates the one already running instead of putting two processes on one
SQLite database. Verified by launching real processes in all three pairings
(Avalonia→Avalonia, WPF→Avalonia, Avalonia→WPF): every second launch exited as secondary.

An in-process test cannot cover this on Windows, because a named mutex is owned by the *thread* and
is recursive — two claims in one process both succeed. `PlatformSingleInstanceTests` therefore
asserts which primitive each OS picks, and the cross-process behaviour is the manual matrix above.

**One pairing is not covered, and cannot be**: the Microsoft Store build. A packaged app's named
kernel objects live in its own namespace, so its mutex is invisible from outside the package —
measured by holding the installed Store build open and finding no `Local\Daynote-<SID>`, while the
same code unpackaged creates one. An unpackaged build can therefore run beside an installed Store
build, on the same data folder (§3.1: they share one), and neither notices. That is a migration
hazard for the cutover, not a defect here; the cutover release should tell people to remove the
Store build.

### Open at login: moved, and it works

`WindowsRunKeyStartupTaskGateway` lived in the Avalonia app, where nothing could test it. It now sits
in `Daynote.Infrastructure.Startup` beside `MsixStartupTaskService`, and
`WindowsRunKeyStartupTaskGatewayTests` drives the real registry under a throwaway value name: enable
writes the entry, disable removes it (rather than blanking it), the command keeps its quotes so a
path with spaces survives, re-enabling from a new location replaces the old path, and disabling
something never enabled is not an error.

What it still cannot do is notice that the user switched the entry off in Task Manager — the value
stays, so the gateway keeps answering `Enabled`. The settings copy that says "turned off in Windows
startup settings" comes from the MSIX `StartupTask` API and does not apply to this build.

### Global hotkey: registered fine, summoned badly

Registration was never the problem. With the app running, `RegisterHotKey` for Ctrl+Alt+D from
another process fails with 1409 (already registered) and succeeds once the app exits, so the chord
is really held.

The defect was one step later. Closing the window hides it to the tray, and pressing the chord made
it visible again — **behind whatever the user was looking at**. Windows refuses to let a background
process take the foreground, and `Window.Activate()` did not get past that. The process that just
received a hotkey is one of the cases the rule allows through, so `WindowsForeground.Raise` calls
`SetForegroundWindow` directly after activating. Measured before and after: `foreground is Daynote`
went from False to True.

A summon that reveals a window without focusing it is the kind of thing that reads as working in a
screenshot and is useless in practice — which is the argument for exercising each of these rather
than trusting that they compile.

## 3. Packaging — decided: Store on Windows, unpackaged on macOS

**Decision (2026-09-07, revised): Windows keeps shipping as MSIX through the Microsoft Store. The
unpackaged installer built in §5b serves macOS, and stays parked as a ready Windows fallback.**

This reverses the earlier decision in this same section, and the reason is code signing. An
unpackaged Windows release needs an OV or EV certificate bought, renewed and held by someone;
without one every download raises SmartScreen. The Store re-signs at ingestion, so that cost
disappears — along with the auto-updater, the install base and the listing, all of which the
unpackaged route would have had to rebuild or abandon.

**What this changes about the consolidation: nothing structural.** The Avalonia `Daynote.Desktop` is
the app either way; MSIX is a wrapper around whatever executable it points at. The cutover (phase 6)
repoints `packaging/Daynote.Package` from `Daynote.App` to `Daynote.Desktop` instead of retiring it.

| | Effect of staying on the Store |
|---|---|
| Login item | `MsixStartupTaskService` stays the Windows implementation. `WindowsRunKeyStartupTaskGateway` is not dead code — it is the fallback path, and it is tested (§2) |
| MCP registration | The app-execution alias is still required: the real executable sits under `%ProgramFiles%\WindowsApps`, whose ACLs a client process cannot traverse. `McpServerCommand` already picks the alias when `Package.Current` resolves |
| Store policy | `docs/STORE.md` §10.8 keeps applying — third-party commerce, trial disclosure, Partner Center declarations. Paddle checkout stays inside those rules |
| Auto-update | The Store keeps doing it. `WindowsUpdateService` returns immediately in a packaged build (`manager.IsInstalled` is false), so it costs nothing but its assembly |
| Code signing | **The reason for the decision.** No certificate to buy for the shipping channel |
| Listing, ratings, install base | Carried over intact |

### Why not run both channels

They can coexist technically — `McpServerCommand.IsPackaged()` already proves a single binary can
detect which one it is in, and both `IStartupTaskGateway` and `IUpdateService` implementations exist.
The blocker is not the build. It is that **a packaged app's named kernel objects live in its own
namespace**, so the single-instance mutex is invisible from outside the package (§2). A user with
both channels installed can run both at once against the same `%LocalAppData%\Daynote` database, and
neither process notices the other. One channel, no hazard.

So the unpackaged Windows artefacts stay buildable and unshipped: `scripts/Build-WindowsApp.ps1`
still works, `Program.UpdateFeedUrl` stays empty, and nothing publishes a feed. Turning the channel
on later means buying a certificate and filling in that URL — not writing code.

### 3.1 The data path — checked, and there is no migration to write

Kept because it is what makes the fallback in §3 a real option, and because it corrects three files
that say otherwise. The worry with ever leaving MSIX is that Store users' notes are locked inside the
package's private store. **Measured on 2026-09-07: they are not.** The packaged build writes to the same real
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
- Both channels must keep writing to `%LocalAppData%\Daynote`. Changing the location is what would
  actually strand data, and there is now no reason to. Staying on the Store also means the
  cutover moves no data at all: same package family, same folder, an ordinary update.

## 4. Design system — re-measured 2026-09-07, and smaller than it looked

This section used to say the v3 palette and the pill/card/typography system did not exist on the
Avalonia side. **That was written before the palette sync and is no longer true.** Re-counting the
files rather than trusting the earlier note:

| | WPF | Avalonia |
|---|---|---|
| Theme XAML | ~2,300 lines across 18 dictionaries | ~440 lines across 2 |
| Views | 20 `.xaml` | 4 `.axaml` |

The line ratio is not the work ratio. WPF spends most of those lines on `ControlTemplate` rewrites
that Avalonia's selector styling does not need, and the Avalonia shell already carries the palette
(both variants, key-for-key, held that way by `DesktopPaletteParityTests`) and the primitives:
panel, card, chip, pill button, segmented tab, day cell, note row, check circle, editor, inline
input, empty state.

### Heat dots — done

`CalendarDayCellViewModel.ActivityLevel` now drives the Avalonia calendar the way it drives the WPF
one: light sky / sky / blue for 1, 2 and 3+ notes, extras alone counting as level 1. Verified by
seeding a month with 1, 2, 3 and 4 notes on different days and capturing the running window.

Two things the port needed that the WPF version did not:

- **Avalonia style classes take a bool, not a value match.** There is no `DataTrigger Value="2"`, so
  the level is exposed as `IsActivity1/2/3` beside `ActivityLevel`, and the dot binds three classes.
- **The selected day fills with the accent, and the heat brushes are shades of that same accent** —
  so the dot vanished into it. A descendant selector repaints it in the on-accent colour.

The dot sits in its own grid row rather than floating over the number, and is present-but-transparent
on an empty day, so the whole grid keeps one baseline. That is the same defect that was fixed in WPF
earlier, arrived at differently: WPF needed `Visibility="Hidden"` inside a `StackPanel`, Avalonia
needs the row to exist.

### What is actually left

| Surface | State |
|---|---|
| Account | Present but flattened: `AccountPanel.axaml` is one 200-line column covering sign-in, status, lock, recovery key and subscription. WPF splits this into a 520px window, a titlebar avatar with a popup menu, and a compact settings row. The Avalonia titlebar has the avatar and its initial, but clicking it opens the panel directly — there is no menu |
| Settings | Present as a modal card inside `MainWindow.axaml`. WPF has a dedicated 313-line view plus a 267-line dictionary. Feature coverage needs a side-by-side pass, not a port |
| Sticky notes | 58 lines against the WPF version's styling. Always-on-top, per-note colour and live two-way edit are unverified |
| High contrast | **Absent, and the gap is not where §4 used to claim.** WPF has no high-contrast *product* palette either — it swaps the older foundation layer (`Daynote.Colors.HighContrast.xaml`) for one that aliases `SystemColors`, chosen once at startup from `SystemParameters.HighContrast`. Avalonia has no equivalent and would need its own approach |

So the remaining design work is three screens to bring to parity and one accessibility mode to
design, not a design system to build.

## 5. Tests — the harness exists now (2026-09-07)

`tests/Daynote.Desktop.Tests` runs Avalonia headless: composed and laid out in memory, no window
server, so it runs on Windows CI and macOS CI alike. Five tests, and each one earned its place by
catching something the moment it was written.

| Test | Covers | What it caught |
|---|---|---|
| `ResourceResolutionTests` (×2, one per variant) | Every `{DynamicResource}` / `{StaticResource}` key in the app's `.axaml` resolves | A renamed brush is reported with the files that use it |
| `MainWindowCompositionTests` (×2, one per variant) | The shell measures and arranges with no binding errors | — |
| `LocalizationKeyTests` | Every `Strings[Key]` in markup exists in both catalogs | A made-up key that compiled **and** raised no binding error |

Three things learned while building it, all of which shape what is worth testing here:

1. **Compiled bindings already catch most of it.** `AvaloniaUseCompiledBindingsByDefault` plus
   `x:DataType` means `{Binding Calendar.MonthLabelTYPO}` fails the *build* (AVLN2000). The runtime
   composition test is therefore not the front line it is in WPF — it covers what stays dynamic:
   `$parent[Window]` walks, untyped contexts, template-driven lookups.
2. **The indexer is the hole.** Copy reached through `Strings[SomeKey]` is a string the compiler
   cannot check, and the catalog returns something for a key it does not have — so a typo renders
   quietly. That is what `LocalizationKeyTests` exists for, and it was verified by planting one.
3. **Bindings must be armed after the DataContext.** A control built during `InitializeComponent`
   evaluates its ancestor bindings while the window still has no DataContext, and logs an error that
   every real run also produces and then resolves. The harness attaches its log sink after assigning
   the DataContext, and says so.

Still not ported: the WPF **showcase evidence pipeline** (`ShowcaseCapture`, the fixture factories,
the interaction contract table) and `Daynote.UiQa.Tests`. Those are a body of work in their own
right, and §7 still asks whether they should be rebuilt on Avalonia or retired.

## 5b. Distribution — built, and now the macOS channel plus a Windows fallback

`scripts/Build-WindowsApp.ps1` is the Windows counterpart of `Build-MacApp.sh`: publish
self-contained (the runtime is inside, nothing to install first), sign, zip, and — with
`-Installer` — pack a Velopack release.

**Read this section knowing the outcome of §3: Windows ships through the Store, so none of what
follows is on the shipping path today.** It was built, installed and uninstalled for real, and it is
kept working so the fallback is a decision rather than a project. The unpackaged route is how macOS
ships.

**Velopack** was chosen over an installer plus a hand-rolled update check, because it is the one
option that replaces both things the Store does. Per-user install, no administrator prompt,
silent background updates: the experience Store users already have.

Self-contained but **not** single-file, for the same reason as the Mac bundle. `Daynote.Mcp` ships
beside the app and a client launches it by path; single-file would bury it in a temp directory that
changes every run.

### The app id is `Daynote.Desktop`, and that matters

Velopack installs into `%LocalAppData%\<packId>`. The database lives in `%LocalAppData%\Daynote`.
A pack id of `Daynote` would have unpacked the application on top of the user's notes.

Verified by installing for real and measuring either side:

```
install dir   %LocalAppData%\Daynote.Desktop   (current, packages, Daynote.Desktop.exe, Update.exe)
data root     %LocalAppData%\Daynote           daynote.db unchanged at 798,720 bytes
uninstall     install dir gone, desktop shortcut gone, daynote.db still there
```

That last line also answers half of a §7 question: **this** installer's uninstall leaves the notes
alone. What an MSIX uninstall does is still untested and still the reason to back up first.

### Updating

`WindowsUpdateService` checks the feed at startup, downloads in the background, and stages — the new
version applies on the next launch rather than restarting under the user. This is a note app people
leave open for days; interrupting one to install something they did not ask for is worse than
waiting. Every failure is swallowed: no feed, no network, a proxy, an unparseable release — none of
those are worth a dialog, and the consequence is staying on the version already running.

It does nothing unless the app is running from a Velopack install, so a developer build or an
unzipped copy never tries to rewrite itself. `VelopackApp.Build().Run()` is the first statement in
`Main`, before the single-instance claim, or an install hook would look like a second launch and
silently do nothing — `vpk pack` verifies that call is present and fails the build without it.

### Signing: the script is ready, the certificate is not

Signing is driven by the environment so no secret reaches a file or a build log:
`DAYNOTE_SIGN_THUMBPRINT` for a certificate in the user's store, or `DAYNOTE_SIGN_PFX` with
`DAYNOTE_SIGN_PFX_PASSWORD`. Both executables are signed, and Velopack signs the setup bundle with
the same parameters. With neither set the build warns and produces an unsigned output, which is fine
for testing and not for release. **Buying the certificate is still the open item** (§7): EV clears
SmartScreen immediately, OV builds reputation over time.

`vpk` is a global tool, installed once with `dotnet tool install -g vpk`.

## 6. Suggested order

Each phase leaves the tree shippable, and WPF stays the Windows product until phase 6. There is
no data-migration phase: §3.1 establishes that both builds read the same folder.

1. ~~**Chrome.**~~ **Done** (§1): platform-conditional title bar, app-drawn caption buttons with
   native hit-test roles, drag and Snap Layouts verified. Rounded corners and DPI changes remain.
2. ~~**Platform services.**~~ **Done** (§2): single instance settled on the mutex, login item moved
   and tested against the real registry, hotkey summon fixed. The tray icon's own menu and the
   startup copy that assumes `StartupTask` semantics are the leftovers.
3. ~~**Test harness.**~~ **Done** (§5): headless Avalonia, resource resolution in both variants,
   shell composition, and catalog keys. The showcase pipeline is still unported.
4. **Design system.** Palette, primitives and heat dots are done (§4). What remains is parity on
   three screens — account, settings, sticky notes — and a high-contrast mode, which Avalonia has no
   equivalent of and which needs designing rather than porting.
5. ~~**Distribution.**~~ **Done** (§5b): publish/sign/zip script, Velopack installer and updater,
   verified by installing and uninstalling. Per §3 it is now the macOS channel and a parked Windows
   fallback — the wapproj and the Store submission scripts stay.
6. **Cut over.** Repoint `packaging/Daynote.Package` at `Daynote.Desktop`, submit that MSIX, keep
   `Daynote.App` in the tree for one release as a fallback, then delete it and fold
   `Daynote.Desktop` back into a single app project.

## 7. Still open

- ~~**Signing certificate.**~~ **Settled by §3**: the Store re-signs at ingestion, so no certificate
  is needed for the shipping channel. It becomes an open question again only if the unpackaged
  Windows fallback is ever turned on.
- ~~**Where the update feed lives.**~~ Deferred with the fallback. `Program.UpdateFeedUrl` stays
  empty and the updater stays inert; a packaged build would not use it anyway.
- **Does `packaging/Daynote.Package` build against `Daynote.Desktop`?** Untried. The wapproj's
  `EntryPointProjectUniqueName` and its hand-written layout rules for `Daynote.Mcp` both name
  `Daynote.App`, and the Avalonia publish has a different file layout. This is now phase 6 work.
- **Does uninstalling the Store package delete `%LocalAppData%\Daynote`?** (§3.1). Answering it
  needs one throwaway machine and one uninstall. It decides how loudly the cutover has to warn.
- **Does the cutover wait for full parity, or ship in stages?** Staying on the Store removes the
  ugly half of this: the cutover is an update to the same package, not a second install, so nobody
  ends up running two shells at once.
- **What happens to the showcase evidence pipeline?** Rebuild it on Avalonia, or retire it and keep
  only binding/composition tests. It is a large body of work either way.
- **macOS distribution.** The unpackaged route covers Windows mechanics; notarisation, the Apple
  Developer ID and the DMG are their own open questions.
