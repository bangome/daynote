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
| Login item | Corrected by §6: the packaged Avalonia build uses `WindowsRunKeyStartupTaskGateway`, not the manifest's `StartupTask`. `MsixStartupTaskService` is still the service, but the WinRT `StartupTask` API it would need sits behind `#if WINDOWS` and `Daynote.Desktop` targets plain `net10.0`. HKCU `Run` is honoured inside a package, so the behaviour is right; the manifest declaration is now unused |
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
| Account | **Done.** `AccountMenu.axaml` ports the titlebar avatar, its sync dot and the menu (identity, sync row, Manage account / Settings). The worded sync label came off with it, as in v3. The account body stays one panel rather than WPF's separate 520px window |
| Settings | **Done, and it needed almost nothing.** The modal already bound every row the WPF view has — through view-model label properties instead of markup lookups, which is why a string-by-string diff made it look empty. The only real gap was the About card |
| Sticky notes | **Done, and it had two defects.** The window hard-coded `#FFFDF0A0` / `#FF3A3520` instead of the Sticky brushes, so the palette could not reach it; and it carried the macOS traffic-light inset as a literal 70px margin, leaving a hole on every Windows sticky |
| High contrast | **Was not an Avalonia gap at all** — the product had no working high-contrast mode on either shell. Now built for both, following the OS theme; see below |

### High contrast: it never worked, and now it does — from the OS theme

`WpfProductThemeApplier` said High Contrast still wins, because the HC aggregate is merged after the
product brushes and would override them by key. **Measured 2026-09-07: it overrode nothing.**

```
Daynote.Colors.HighContrast.xaml   28 keys, all Daynote.Brush.*          (pre-v3 foundation layer)
Daynote.Product.Light.xaml         39 keys, all Daynote.Product.Brush.*  (what v3 actually paints)
overlap                            0
```

Every v3 surface — every file under `Shell/Product`, `Settings` and `Account` — reads product keys
only; not one reads a foundation brush. So `SystemParameters.HighContrast` was read at startup, the
dictionary was merged, and nothing the user could see changed. It was accessibility support that
compiled.

**Decided: follow the OS theme.** Someone who has chosen a high-contrast theme has chosen those
colours, and a fixed palette of our own would override the point of the feature.

Built once for both shells, as three pieces:

| | |
|---|---|
| `Daynote.Presentation/Design/HighContrastPalette.cs` | The shared table: each product brush → a role (`Window`, `WindowText`, `WindowFrame`, `ControlFace`, `ControlText`, `GrayText`, `Highlight`, `HighlightText`, `Hotlight`), plus the literals. Roles rather than colours, because that is the only part the two shells can agree on without one referencing the other's framework |
| `WpfHighContrastPalette` | Resolves a role through `SystemColors` |
| `WindowsHighContrastPalette` | Resolves a role through Win32 `GetSysColor`. Avalonia surfaces a contrast *preference*, not the colours behind it, so this is the price of following the theme |

Both appliers merge the result **last**, and re-merge on every apply. That ordering is the whole
mechanism and it is easy to get backwards: the light/dark palette is removed and re-inserted on every
theme change, so a high-contrast dictionary merged once at startup ends up ahead of it the first time
the user flips the theme, and the mode comes off with nothing to say so. There is a test for exactly
that on each side.

**The mapping is lossy, deliberately.** A high-contrast theme has no vocabulary for "green means
saved, amber means look at this, red means wrong", and no alpha for a tint:

- The status colours collapse. `Ok` becomes ordinary text; `Warn`, `Danger` and `Overdue` become
  `Hotlight`. The words already said which is which.
- The `*Soft` tints fall back to the page. The accent still reads through the border and the text of
  whatever it was tinting.
- The calendar heat ramp keeps two visible steps instead of three.
- Weekend tinting becomes plain text. The column header says which day it is.
- The four Google brand colours stay literal. Recolouring a trademark produces something that is no
  longer the Google logo, which is both wrong and against the brand terms.

### macOS — derived, because there is nothing to follow (2026-09-08)

"Increase contrast" on macOS is a preference, not a theme: it darkens borders and drops
translucency inside the system appearance and publishes no colour set. Avalonia's macOS backend does
report it — `Avalonia.Native` sets `PlatformColorValues.ContrastPreference`, checked in the 12.1.2
assembly — so `DetectHighContrast` was already answering true there, and `Apply` was then returning
without doing anything because the only palette source was `GetSysColor`.

So macOS gets a **derived** palette instead: `DerivedHighContrastPalette` (portable) resolves the
same nine roles from the same shared table to fixed maximum-contrast values, one set per variant.
That is a real difference from the Windows behaviour, not a translation of it. On Windows the app
defers to the user's theme; on macOS there is no theme to defer to, only a request for more contrast
than v3 gives, so the app answers it itself — light stays light, black on white, borders in the text
colour, secondary text at ~11:1 instead of v3's 4.6:1. The lossy decisions in the role table (status
colours collapsing, the heat ramp losing a step, tints falling back to the page) are properties of
high contrast, not of Win32, so they are shared rather than re-made per platform.

`AvaloniaThemeApplier` now takes a `HighContrastSource` — `SystemColors` on Windows, `Derived`
elsewhere — and the derived dictionary is rebuilt per variant, with the other variant's copy removed
before the merge. The source is injectable so the macOS path is tested on the Windows build machine.

Not verified: what it looks like on a Mac with the preference on. The colours are measured, the merge
is tested, the rendering is not.

Tests, on both sides plus one that needs neither framework:

| Test | Guards |
|---|---|
| `HighContrastPaletteTests` (portable) | The table covers the product palette exactly, both directions — the test that was missing when the mode quietly did nothing. A brush the table forgets keeps its normal-theme colour in a high-contrast session, which is precisely what nobody testing in the normal theme will ever see |
| `HighContrastPaletteTests` (WPF, 6 cases) | System colours win, the theme toggle does not take the mode off, every mapped brush resolves, brand colours survive |
| `HighContrastThemeTests` (Avalonia, 6 cases) | The same, headless, in both variants — plus the derived path forced on, proving the variant swap replaces the dictionary rather than stacking a second one |
| `DerivedHighContrastPaletteTests` (portable, 9 cases) | The derived palette covers the table exactly, and is actually high contrast: every text-on-surface pairing the table implies is measured with the WCAG formula and has to clear 7:1 (AAA) in both variants. Mutation-checked: putting the v3 mid-grey back fails it at 4.8:1 |

The old `Daynote.Colors.HighContrast.xaml` is left where it is. It is still merged at startup and
still overrides the foundation brushes; nothing reads them, so it is dead weight rather than a
hazard, and removing it belongs with retiring the pre-v3 foundation layer.

So phase 4 is done.

## 5. Tests — the harness exists now (2026-09-07)

`tests/Daynote.Desktop.Tests` runs Avalonia headless: composed and laid out in memory, no window
server, so it runs on Windows CI and macOS CI alike. Five tests, and each one earned its place by
catching something the moment it was written.

| Test | Covers | What it caught |
|---|---|---|
| `ResourceResolutionTests` (×2, one per variant) | Every `{DynamicResource}` / `{StaticResource}` key in the app's `.axaml` resolves | A renamed brush is reported with the files that use it |
| `MainWindowCompositionTests` (×2, one per variant) | The shell measures and arranges with no binding errors | — |
| `LocalizationKeyTests` | Every `Strings[Key]` in markup exists in both catalogs | A made-up key that compiled **and** raised no binding error |
| `RenderedFrameTests` (×3, added 2026-09-08) | The shell (both variants) and the sticky note render to real pixels; an empty day lists no projection row | The phantom "노트 1" row under "노트 0개"; see below |
| `TutorialOverlayTests` (×4, added 2026-09-09) | Every step's target name resolves in this shell, a hole is cut and follows the step, a target-less step dims everything, and the radius is read from the target rather than fixed | An overlay that covered only the body clipped the title bar's search hole away to nothing and fell back to dimming everything — silently, because that is also the no-target behaviour |
| `NoteRowActionsTests` (×4, added 2026-09-09) | The sidebar row's context menu resolves all three commands, Delete on a focused row deletes it, and the heading's rename editor commits on Enter and cancels on Escape | A `MenuItem` inside a `ContextMenu` popup binds through a separate visual tree, so a `$parent[Window]` walk comes back null there and the item renders fine while doing nothing |

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

### Rendered frames — the showcase's replacement (2026-09-08)

The fixture now draws. `HeadlessAppFixture` runs with `UseHeadlessDrawing = false` and `.UseSkia()`,
so a shown window produces real pixels and `CaptureRenderedFrame()` returns them; Skia was already
in the test output transitively through `Avalonia.Desktop`, so no package was added.
`RenderedFrameTests` shows the shell, runs the same `InitializeAsync` the app runs at start-up
(pumping the dispatcher until it completes — without it the calendar is a weekday header and no
days), captures light and dark, and captures the sticky note. The PNGs land in `frames/` next to the
test binary. The assertions are coarse on purpose — right size, more than a handful of colours, the
two variants differ, the corner is the page colour, the sticky's top rows carry glyphs and the band
beneath them is flat — because a pixel-exact oracle is what made the WPF showcase expensive to keep
true. The frames are the evidence; the tests only guarantee the evidence is real.

Two things the first frame showed that nothing else had:

- **The sticky note has one title strip.** The doubled-bar fix had been "not visually verified" since
  it was made, because synthetic input never reached the app. The frame shows the app's strip and
  nothing above it, and the test pins the band under it as flat.
- **An empty day listed a phantom row.** The sidebar drew the editor's blank projection ("노트 1") as a
  note row, under a header reading "노트 0개" and above "이 날짜에 노트가 없습니다" — three statements
  that could not all be true. The WPF shell hides projection rows (`ProductWindow.xaml`,
  `IsProjection` → collapsed) and the port had dropped that. Fixed with `IsVisible="{Binding
  !IsProjection}"`, and `An_empty_day_lists_no_note_row` fails without it (checked by removing it).

One test artefact worth recording so nobody chases it: switching `RequestedThemeVariant` directly
repaints the palette but leaves the light wordmark on the dark ground, because the wordmark follows
the view model's `IsDark`. The test flips `IsDark`, as the theme button does.

### The coaching overlay — ported 2026-09-09

The Avalonia shell had no spotlight: one flat `#66000000` scrim with a centred card, so every step
described a part of the UI without pointing at it, and only two of the deck's target names existed
here. That was the largest remaining parity gap, and it mattered because **the MSIX ships this
shell** (§6) — the WPF tutorial polish would otherwise never reach a user.

`Views/TutorialOverlay.axaml` ports `Daynote.App/Onboarding/TutorialView`: a `Path` scrim whose
`Data` is a `CombinedGeometry` in `Exclude` mode (Avalonia's counterpart to WPF's even-odd
`GeometryGroup`), an accent ring, and a callout placed below / above / beside the target. The radius
rule is the same one — the target's own chrome plus the padding — and the six missing anchors
(`TutCalendar`, `TutEditor`, `TutSearch`, `TutSettings`, `TutTabTodo`, `TutTabFiles`) are named.

Two things this cost, both worth remembering:

- **Declaring `InitializeComponent()` by hand shadows the generated one**, so every `x:Name` field
  stays null and the first line that touches one throws. The generated method is the only one to call.
- **The overlay has to span the whole window.** Put in the body grid it looked right on five of the
  six targets and clipped the search pill's hole — which lives in the title bar — away to nothing,
  falling back to the no-target appearance rather than failing.

The WPF **showcase evidence pipeline** and `Daynote.UiQa.Tests` are not ported and will not be; see §7.

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
4. ~~**Design system.**~~ **Done** (§4): palette, primitives, heat dots, the account, settings and
   sticky-note surfaces, and a high-contrast mode that follows the OS theme — which turned out to be
   new work rather than a port, because neither shell had one that did anything.
5. ~~**Distribution.**~~ **Done** (§5b): publish/sign/zip script, Velopack installer and updater,
   verified by installing and uninstalling. Per §3 it is now the macOS channel and a parked Windows
   fallback — the wapproj and the Store submission scripts stay.
6. **Cut over.** The packaging is repointed and builds (§6). What is left is the release itself:
   submit the MSIX, keep `Daynote.App` in the tree for one release as a fallback, then delete it and
   fold `Daynote.Desktop` back into a single app project.

## 6. Cutover — the packaging is repointed and builds (2026-09-07)

`packaging/Daynote.Package` now packages `Daynote.Desktop`. Repointing it was four files and one
real bug.

```
baseline (Daynote.App)      541 entries   Daynote.App/Daynote.App.exe        + Daynote.Mcp.exe
after   (Daynote.Desktop)   292 entries   Daynote.Desktop/Daynote.Desktop.exe + Daynote.Mcp.exe
```

Both executables sit where `AppxManifest.xml` says they do, the Avalonia assemblies are in, and no
`Daynote.App/` folder survives. `scripts/Build-Package.ps1` runs the whole way through — locked
restore, `-warnaserror` build, self-contained publish, package — and its post-package check confirms
all **215** assemblies `Daynote.Mcp.deps.json` names are present in the merged folder.

### The bug: package identity was compiled out

`McpServerCommand.IsPackaged()` asked WinRT `Package.Current` inside `#if WINDOWS`, with the
`#else` branch commented "only the MSIX build has a package identity; the portable build never
does."

That was true exactly as long as the packaged entry point was `Daynote.App`, which targets
`net10.0-windows10.0.19041.0`. `Daynote.Desktop` targets plain `net10.0`, so it links this
assembly's non-Windows build — **the one where the fence removed the only code that could notice.**
A packaged Avalonia build would have told every MCP client to launch its real path under
`%ProgramFiles%\WindowsApps`, whose ACLs a client process cannot traverse, and nothing would have
failed: no exception, no log, just a feature that does not work in the shipped build and works
perfectly in every dev run.

Fixed by asking Win32 `GetCurrentPackageFullName` instead, which needs no Windows target framework
and no projection. It also removes the reason to multi-target the Avalonia app, which was the other
way out and a much larger change.

The same fence still hides the WinRT `StartupTask` API in `MsixStartupTaskService`, so the packaged
Avalonia build takes the HKCU `Run` gateway instead. That works inside a package and is tested
(§2); what it costs is the thing §2 already recorded — the Run key cannot tell that the user
switched the entry off in Task Manager. The manifest's `startupTask` declaration was therefore
unused, and on 2026-09-08 it came out — see "Startup: one mechanism" below.

### Startup: one mechanism (2026-09-08)

Two questions had to be answered before the declaration could go.

**Does a Run value written from inside the package reach the real hive?** MSIX virtualizes HKCU
writes into the package's private hive by default, and if that applied here the Avalonia build's
"open at login" would be a toggle that writes a key nobody reads. Measured with
`Invoke-CommandInDesktopPackage`, which runs a command under the package identity: `reg add` of a
probe value into `HKCU\…\CurrentVersion\Run` from inside, `reg query` from outside — **visible**, and
the package's `SystemAppData\Helium\User.dat` was not touched. So the Run key works in the shipped
build. (A first attempt at this probe was blocked by tooling before it ran and read as "virtualized";
the conclusion above is from the run that actually executed.)

**What does the declaration cost while it stays?** A second switch. Windows lists a declared task
in Settings → Apps → Startup whether or not the app ever touches it; a user who turns it on there
gets a login launch the app's own toggle knows nothing about, and the two can sit in opposite
states indefinitely. The WPF shell could read that state through WinRT; the Avalonia shell cannot.

So the extension is gone, and `PackageManifestPolicy` now **rejects** a `windows.startupTask` instead
of requiring one — the same inversion `internetClient` went through when cloud sync shipped. The
`WindowsStartupTaskGateway` (WinRT) stays in `Daynote.Infrastructure` behind `#if WINDOWS` for the WPF
shell until that is retired.

### What the guardrails caught, and what they did not

`PackageManifestPolicy` pinned `Daynote.App\Daynote.Mcp.exe`, so the MCP path change failed two
tests immediately — which is the guardrail working. It had **no** check on the app's own
`Executable`, though, and that is the one attribute that must move when the packaged shell changes:
a stale value produces a package that builds, installs, and fails to launch. There is a check now,
derived from a single `ExpectedAppFolder` constant so the two paths cannot drift apart again.

`McpServerCommand`'s identity fence was caught by reading the code, not by a test, because there was
no test that could run it — `IsPackaged` was private and the only assertion was on the pure
`Resolve` core. It is now public and `PackageIdentityTests` exercises the P/Invoke, which is the part
that fails silently if the entry point name or the marshalling is wrong.

### Installed and launched — 2026-09-08

The Store build was already off this machine, so the repointed layout was registered in place
(`Add-AppxPackage -Register …\bin\x64\Release\AppxManifest.xml`, development mode) and started
through its `shell:appsFolder` entry, which is how the Start menu starts it.

- It runs. A visible 1256×788 top-level window titled "Daynote", class `Avalonia-…`, under the
  packaged `Daynote.Desktop` process. First launch took roughly 14 seconds to show a window; a probe
  at 8 seconds found the process and no window yet.
- It opened the real database. `%LocalAppData%\Daynote\daynote.db` was touched at launch and its WAL
  checkpointed; the package's `LocalCache` contains only `Microsoft` folders and no `daynote.db`
  anywhere under the container. Same finding as the manifest's STORAGE note, now for the Avalonia
  entry point too. Integrity check `ok`, 43 notes, unchanged.
- The alias resolves and starts the server: `daynote-mcp.exe` on PATH is
  `%LocalAppData%\Microsoft\WindowsApps\daynote-mcp.exe`, and running it starts the stdio host.
- The identity fix from above is now observable rather than inferred. The server logs one line to
  stderr at startup — database path, `IsPackaged()`, and the command `McpServerCommand.Current`
  resolved to — so the answer can be read from any client's log instead of trusted. Under the alias
  it reports `packaged True` and the alias as the command; the same binary started from the
  unpackaged publish folder reports `packaged False` and its sibling path.

Still not done: the same test with a real Store-signed install, and the uninstall question in §7.

## 7. Still open

- ~~**Signing certificate.**~~ **Settled by §3**: the Store re-signs at ingestion, so no certificate
  is needed for the shipping channel. It becomes an open question again only if the unpackaged
  Windows fallback is ever turned on.
- ~~**Where the update feed lives.**~~ Deferred with the fallback. `Program.UpdateFeedUrl` stays
  empty and the updater stays inert; a packaged build would not use it anyway.
- ~~**Does the packaged Avalonia build actually run?**~~ Yes — registered from the loose layout and
  launched 2026-09-08, see §6. What remains is the same check on a Store-signed install.
- ~~**The manifest still declares a `windows.startupTask` nothing enables**~~ Removed 2026-09-08
  after measuring that the Run key reaches the real hive from inside the package (§6). Keeping it
  would have left a second startup switch the app cannot see.
- ~~**Does uninstalling the Store package delete `%LocalAppData%\Daynote`?**~~ **No** — measured
  2026-09-08 on the dev registration: `Remove-AppxPackage` deleted the package container under
  `%LocalAppData%\Packages\<PFN>` and left `%LocalAppData%\Daynote` untouched (17 files before and
  after, `daynote.db` SHA-256 identical). That follows from §3.1: the data was never inside the
  container, so uninstall has nothing of ours to remove. Caveat: a development-mode registration, not
  a Store-signed install — but the mechanism (where the data lives) is the same, and it is the
  mechanism that decides. The cutover does not need a data-loss warning; the in-app Backup stays the
  recommendation for the ordinary reason.
- ~~**High contrast on macOS.**~~ Built 2026-09-08 as a derived palette (§4); what is left is
  looking at it on a Mac with the preference on.
- **Does the cutover wait for full parity, or ship in stages?** Staying on the Store removes the
  ugly half of this: the cutover is an update to the same package, not a second install, so nobody
  ends up running two shells at once.
- ~~**What happens to the showcase evidence pipeline?**~~ **Decided 2026-09-08: retire it with
  `Daynote.App`, and replace the one thing it was for with headless Skia capture.** The showcase
  (`src/Daynote.App/Showcase`, 23 files / 3,529 lines, plus 25 tests / 1,293 lines) renders isolated
  WPF fixtures under `--showcase` and writes PNG + JSON evidence. Its catalogue still lists
  `clipboard-item`, `clipboard-drawer`, `consent-panel` and `tray-menu` — features removed in August —
  and its fixtures draw the old `MainWindow` shell, not the v3 `ProductWindow`, so it has not been
  proving the shipped UI for some time even on WPF. Rebuilding it on Avalonia would mean rewriting it
  and first re-deciding what it should prove. What this port actually needed from it — a rendered
  frame of the running Avalonia UI inside a test, which is why the sticky window and the flyouts were
  left "not visually verified" — is obtained far more cheaply by enabling Skia in
  `HeadlessAppFixture` (`UseHeadlessDrawing = false`) and capturing frames; see §5. The showcase code
  is left untouched until `Daynote.App` goes, so it is deleted once rather than maintained twice.
- **macOS distribution.** The unpackaged route covers Windows mechanics; notarisation, the Apple
  Developer ID and the DMG are their own open questions.
