# iPhone widgets and the Apple Watch app

The designs are `Daynote iPhone Widgets.dc.html` (home small/medium/large, Lock Screen, StandBy,
Control Center, Live Activity) and `Daynote Apple Watch.dc.html` (today, tap to complete, dictation
capture, complications, notifications), with motion M8 from the Motion Spec. This file is how they
are built, and §3–§5 are the contract every other "glance" surface — the Mac widgets, a menu bar —
reads and writes as well.

## 1. Why Swift, and why a contract

.NET for iOS builds the app and nothing that runs beside it: a WidgetKit extension, a watchOS app
and a complication extension can only be Swift, and WidgetKit's `WidgetCenter` and ActivityKit have
no Objective-C surface for .NET to bind. So:

- **`native/apple/`** is an Xcode project (generated from `project.yml` with xcodegen, and checked
  in so a build needs only Xcode) with the widget extension, the watch app and its widget
  extension, and **DaynoteBridge**, a small framework the .NET app links to call those two
  Swift-only frameworks.
- None of them opens the database. **The app decides; they draw.** The app writes the day as a
  snapshot file into a folder they can read (§3), and they write what the user did as action files
  the app carries out through its own store (§4), so a tick on a widget syncs and reminds exactly
  like a tick in the app.

```
 .NET app ──writes──▶ App Group/Glance/snapshot.json ──read by──▶ widgets, Live Activity
    ▲                        │                                  (and, via WatchConnectivity,
    │                        └──updateApplicationContext──────▶ the watch and its complications)
    └──drains──── App Group/Glance/actions/*.json ◀──written by── widget intents
                             ▲
                             └── transferUserInfo ◀── the watch (ticks, dictation)
```

## 2. Identifiers

| What | Value |
| --- | --- |
| App | `cc.arachat.daynote` (App ID `4JV753P6RA`) |
| Widget extension | `cc.arachat.daynote.widgets` (App ID `HRQ4F3LWNQ`) |
| Watch app | `cc.arachat.daynote.watchkitapp` (App ID `96JXMQVG7U`), `WKCompanionAppBundleIdentifier` = the app |
| Watch widgets | `cc.arachat.daynote.watchkitapp.widgets` (App ID `22U774B32B`) |
| Bridge framework | `cc.arachat.daynote.bridge`, signed with the app, no profile |
| App Group | `group.cc.arachat.daynote`, folder `Glance` inside it |
| URL scheme | `daynote://` (§7) |

App Groups is enabled on all four App IDs, and the group exists and is assigned to them
(2026-10-09; the App Store Connect API has no endpoint for groups, so that was done in the
developer portal). Assigning a group invalidates every profile for the App ID: regenerate the four
"Daynote Glance" profiles with `scripts/New-AppleGlanceProfiles.py` and "cc.arachat.daynote
AppStore" likewise, and check each grants the group (`security cms -D -i <profile>`). The build
script refuses to embed the extensions with a profile that does not.

## 3. The snapshot (`Glance/snapshot.json`)

Written by `GlanceCoordinator` (phone) through `GlanceSnapshotBuilder.Build` and
`GlanceFolder.WriteSnapshot` (`src/Daynote.Presentation/Glance/`), read by `GlanceStore`
(`native/apple/Shared/Model/`). UTF-8 JSON, camelCase, null fields left out, Hangul left as Hangul.

```jsonc
{
  "schema": 1,                       // bumped only for a change an old reader would misread
  "generatedUtc": "2026-10-07T05:30:00Z",
  "zone": "Asia/Seoul",              // IANA; every date and time below is a wall clock in it
  "language": "ko",                  // "ko" | "en": the APP's language, which readers follow
  "today": "2026-10-07",
  "locked": false,                   // true: the account lock has notes sealed; everything below is empty
  "lists": [ { "id": "…da7e", "name": "내 할 일", "color": "#ee7f35", "colorDark": "#ff9a52" } ],
  "days": [                          // today and the six days after it
    { "date": "2026-10-07",
      "todos": [ { "id": "…", "seriesId": "…", "occurrence": "2026-10-07T08:00",
                   "title": "비타민 먹기", "listId": "…", "time": "08:00",
                   "repeats": true, "done": false, "noteId": "…", "noteDate": "2026-10-05" } ],
      "events": [ { "id": "…", "title": "디자인 리뷰", "listId": "…", "start": "16:00", "end": "17:00",
                    "repeats": false, "noteId": "…", "noteDate": "…" } ] } ],
  "week": [ { "date": "2026-10-04", "noteCount": 0, "titles": [] } ],   // Sunday–Saturday around today
  "favorites": [ { "id": "…", "date": "2026-10-07", "title": "주간회의 준비", "preview": "1. … · 2. …" } ]
}
```

Rules a reader relies on:

- **Rows and their order are the app's.** Each day is `AgendaDay.For` split by kind: to-dos open
  first (timed in time order, then undated), then done; events by start. A reader never re-sorts
  or re-derives "what is due". It does pick which of the seven days is today when it draws,
  because it draws long after the app ran.
- **A repeating to-do appears as its occurrence**: `seriesId` + `occurrence` identify it, `id` is
  the row standing for it (the series, or an override). `rowKey` = `seriesId@occurrence`, else `id`.
- **`time` null** is a to-do with a day and no clock; **`start` null** is an all-day event.
- **List colour** is the list's place in the sidebar's order (`AgendaListPalette`) until lists
  have a colour of their own (TODOS.md §12 defers the column). Readers take `color` on light,
  `colorDark` on dark and on the watch; Lock Screen and StandBy use none (system tint).
- **No note bodies**, beyond a two-line preview of at most four favourites. Everything textual is
  `privacySensitive` in the widgets, so a locked phone redacts it as the user's Lock Screen
  setting says. While the account lock has the notes sealed the snapshot says `locked` and nothing else.
- Written **only when its content changed** (the stamp aside): a reload costs a widget budget, and
  the watch context is a transfer.

## 4. Actions (`Glance/actions/*.json`)

One file per action, named `<15-digit unix ms>-<action id>.json` so names sort oldest first, each
written to a dot-prefixed temporary name and renamed into place. The app reads them in name order,
applies each, and deletes exactly the files it read (`GlanceFolder`, `GlanceActionApplier`); a
dot-prefixed file is never read.

```jsonc
{ "schema": 1, "id": "<uuid>", "type": "complete", "createdUtc": "…",
  "itemId": "<todo.id>", "seriesId": "<todo.seriesId>", "occurrence": "<todo.occurrence>",
  "date": "2026-10-07" }                                  // the day the row was shown on

{ "schema": 1, "id": "<uuid>", "type": "capture", "createdUtc": "…",
  "text": "회의자료 초안 공유 오늘 5시",                       // what was said, whole
  "kind": "task" | "event" | "note",
  "capturedLocal": "2026-10-07T14:30" }                   // the date in the text is read against this
```

- **Applying twice changes nothing.** `complete` completes (never toggles), finding the row again
  the way the day panel does — by `itemId` for a one-off, and for an occurrence by its
  `RECURRENCE-ID` (never by id alone: every occurrence carries the series id) — and does nothing
  if it has gone or is already done. `capture` makes its item under the action's own `id`.
- **One drain at a time.** Requests that arrive during a drain are folded into one more pass, never
  run beside it and never dropped. Nothing is drained or published until the app has loaded the
  day and read the account (`StartGlanceAsync`, after `account.InitializeAsync`).
- **Deleted when done or impossible, kept when it might work later.** An unreadable file, an
  unknown `type`, or a row that has gone is deleted. An action whose apply throws, or asks to be
  retried (a note that would not save), stays in the queue for up to a day after the file was
  written, then is dropped. The Mac's `GlanceRelay` uses the same day.
- **The snapshot is compared with the file**, not with what the app last wrote, stamp aside, so a
  widget's read-modify-write that lands late is noticed and replaced.
- `complete` on an occurrence writes the override `ToggleAgendaItem` writes in the app.
- `capture` with `task`/`event` is parsed again on the phone with `AgendaPhraseParser.ParseTrailing`
  and built with the `@` command's own `AgendaCapture.Compose` (default list, default alert on a
  to-do, no source note). `note`, or a sentence with no date, appends the text as a new line at
  the end of that day's first note, creating one if the day has none.
- **An extension marks its own guess in the snapshot** (the tick shows at once) and the app
  overwrites the whole file from its store after the drain.
- When: on start (after the day and the account are read), on every resume, and at once when the
  watch relay or a notification action delivers something while the app runs. **Nothing applies while the app is not running** — a tick
  on a widget is queued until Daynote is next opened, and other devices see it after that.

## 5. The watch

- **In:** the phone sends the snapshot as `updateApplicationContext(["snapshot": json])` whenever
  it writes one (and again once a watch is paired or the app installed). The watch saves the bytes
  into its own App Group container, where its complications read them, and reloads them.
- **Out:** each action as `transferUserInfo(["glanceAction": json])`, which the system queues and
  delivers in order even across disconnections; the phone writes it into the queue (§4).
- **Tap to complete:** the ring fills with `.spring(response: 0.35, dampingFraction: 0.7)` and the
  success haptic (M8), the title is struck through, and only after **1.5 s** is the action sent and
  the row removed; a second tap inside the window takes it back and nothing is sent.
- **Dictation:** `TextFieldLink` (dictation first, Scribble and keyboard behind it), then the
  readback: title, 할 일 / 일정 / 노트에 한 줄, crown to move, tap to make. **The readback is computed
  on the watch** with a Swift port of the app's parser (`native/apple/Shared/Model/AgendaPhraseParser.swift`),
  because it has to appear before the user chooses, and a round trip that wakes a .NET app on the
  phone is seconds at best and nothing at all with the phone away. **The item is made on the phone**,
  from the raw text, with the C# parser. The two parsers are held to one table,
  `tests/fixtures/agenda-phrase-vectors.json`: `AgendaPhraseTrailingTests` (C#) writes and checks
  it, `AgendaPhraseParserTests` (Swift) checks the port against it. Change the grammar in C#, run the
  C# test with `DAYNOTE_WRITE_PHRASE_VECTORS=1`, then fix the Swift until its tests pass again.
- "Suffix mode" (`ParseTrailing`): the longest run of whole words at the end that reads completely;
  dictation's full stop, a Korean particle on the last word (`5시에`, `내일까지`) and English
  `at`/`on`/`by` are read past. No date, or nothing left for a title → only 노트에 한 줄.
- Confirmation is the object appearing: a same-day item goes to the top of the list as
  "17:00 · 방금"; another day shows "10/8에 추가됨 · 보기".
- Notifications: the watch mirrors the phone's, which iOS does on its own while the phone is
  locked. The 완료 / 30분 뒤 다시 actions are defined on the phone's notification category (§9).

## 6. The bridge

`DaynoteBridge.framework` (iOS 15+) exports two C functions, which `IosGlanceHost` looks up at run
time (`NativeLibrary.TryLoad` on the bundled framework, then `TryGetExport`) and calls through
function pointers — not `[DllImport]`, which would make them symbols the native link requires:

| Function | Does |
| --- | --- |
| `daynote_glance_reload()` | `WidgetCenter.reloadAllTimelines()`, and `ControlCenter.reloadAllControls()` on iOS 18 |
| `daynote_glance_sync_activity()` | Starts, updates or ends the event Live Activity from the snapshot |

The csproj links it with a conditional `NativeReference`, so a plain `dotnet build` without the
native build still links and runs; the lookup then finds nothing, the calls do nothing, and
widgets fall back to their own quarter-hour timeline.

**Live Activity (design §08): local only.** From 15 minutes before an event to its end, events
only. With no push, it starts the first time the app runs (launch, resume, a new snapshot) inside
that window, not on the minute. The countdown and progress are the system's timer views; the
content goes stale at the start so the system redraws it as 진행 중 without the app; it is ended
the next time the app runs after the event, by 알림 끄기 (an App Intent in the extension, which also
remembers the event so it is not started again), or by the system after 8 hours.

## 7. Links

| Link | Opens |
| --- | --- |
| `daynote://capture` | Today's first note (a new one if the day has none) — "+ 새 노트", Control Center "오늘 노트" |
| `daynote://capture?at=1` | The same, with a new line and `@` typed on it, so the `@` bar is up — "@ 할 일·일정" |
| `daynote://day?date=yyyy-MM-dd` | That day — any widget tap, a week-strip day |
| `daynote://note?date=…&id=…` | That note — a favourite, "회의 노트 열기" |
| `daynote://todos` | The to-do list — Lock Screen accessories |

Avalonia delivers them as `ProtocolActivatedEventArgs` on activation; `App.OpenLink` holds one
that arrives before the day has loaded.

## 8. Building and signing

`scripts/Build-IosApp.sh` does all of it with `DAYNOTE_IOS_EXTENSIONS=1`. **It is off by default**
until the App Group exists (§2): a default build is the app alone, signed with
`DAYNOTE_IOS_PROVISIONING` and exactly the entitlements it had before (`Entitlements.plist`, Sign
in with Apple only). With extensions on, the app is built with `-p:DaynoteGlance=true`, which
signs it with `Entitlements.Glance.plist` (the same plus the group), and the script stops before
building if any of the four profiles lacks the group.

1. `xcodebuild` the bridge and widget extension (iOS) and the watch app with its complications
   (watchOS) into `native/apple/build` — unsigned for a device, ad hoc by Xcode for the Simulator,
   which is the only way the Simulator sees an extension's App Group (macOS refuses to launch an
   ad hoc signature that claims one).
2. Build or publish the .NET app, which links the bridge into `Frameworks`.
3. Copy `DaynoteWidgets.appex` into `PlugIns` and `DaynoteWatch.app` into `Watch`.
4. Device: open the IPA and sign inside out — watch widgets, watch app, widget extension, app —
   each with the profile named `Daynote Glance <bundle id>`, its `.entitlements` file plus its
   `application-identifier` and team, then zip it again. The app keeps the entitlements the .NET
   build gave it (Sign in with Apple, the App Group).

```sh
python3 scripts/New-AppleGlanceProfiles.py            # after any App ID change
# Today, until the App Group exists: the app alone, as before.
DAYNOTE_IOS_SIGN_IDENTITY="Apple Distribution: …" \
DAYNOTE_IOS_PROVISIONING="cc.arachat.daynote AppStore" scripts/Build-IosApp.sh
# With widgets and watch (the app's profile must grant the group too):
DAYNOTE_IOS_EXTENSIONS=1 DAYNOTE_IOS_SIGN_IDENTITY="Apple Distribution: …" \
DAYNOTE_IOS_PROVISIONING="Daynote Glance cc.arachat.daynote" scripts/Build-IosApp.sh
DAYNOTE_IOS_EXTENSIONS=1 scripts/Build-IosApp.sh -t simulator
```

Tests: `xcodebuild test -project native/apple/Daynote.xcodeproj -scheme DaynoteKitTests
-destination platform=macOS` (parser vectors, readbacks, shared-folder round trip);
`-scheme DaynoteRenderTests` on an iOS simulator with `TEST_RUNNER_DAYNOTE_RENDER_DIR=<folder>`
draws every widget and watch screen to PNG, light and dark, Korean and English, for comparison with
the design renders.

## 9. Not built, or not verifiable here

- Notification actions (watch design §05, "폰 알림에도 같은 동작"): a to-do's notification carries
  완료 and 30분 뒤 다시 on iOS (category `daynote.todo`, so the watch mirrors them). 완료 queues a
  `complete` action (§4), written before the handler returns; 30분 뒤 다시 adds the same notification
  half an hour on under its own id (`….snooze`), which every reminder pass withdraws once its
  to-do is ticked, skipped or deleted. Without an App Group folder no 완료 is offered.
  Android's notifications do not have them yet, and an event's 회의 노트 열기 is the plain tap.
- Widgets cannot run code at midnight or at an event's start except through their own timeline;
  the provider lays out entries for each quarter hour, each event boundary and midnight.
- The widget gallery's own names follow the system language (a string catalog); everything a
  widget draws follows the app's.

## 10. macOS: desktop and Notification Center widgets

Design: `Daynote Menu Bar - Desktop - Android Widgets` §02 (B8 light/Korean, B9 dark/English). The
Mac gets the iPhone's set — small 할 일, small 다음 일정, medium 오늘, large 이번 주 — and, because
macOS 14 widgets are interactive on the desktop, the same tick-in-place. The card is translucent so
the wallpaper shows through.

### Same contract as the phone

Nothing about the data is Mac-specific. The app writes the phone's `GlanceSnapshot` with
`GlanceSnapshotBuilder` into `GlanceFolder`, the widget queues `complete` actions with the phone's
`GlanceStore.complete`, and the app carries them out with `GlanceActionApplier`. The widget target
compiles `native/apple/Shared/Model/{GlanceSnapshot,GlanceStore,LocalTime,GlanceText}.swift`
directly, so the Codable model, the strings and the queue code are one copy on both platforms.

| | iPhone | Mac |
| --- | --- | --- |
| App Group | `group.cc.arachat.daynote` | `4T8C76SP99.group.cc.arachat.daynote` |
| Folder | `<container>/Glance/` | `~/Library/Group Containers/4T8C76SP99.group.cc.arachat.daynote/Glance/` |
| Snapshot / queue | `snapshot.json`, `actions/*.json` | same |
| Widget bundle id | `cc.arachat.daynote.widgets` (registered, platform IOS) | `cc.arachat.daynote.mac.widgets`, unregistered (inside `Daynote.app/Contents/PlugIns/DaynoteWidgets.appex`) |
| Who writes the snapshot | `GlanceCoordinator` (Daynote.Mobile) | `MacWidgetBridge` (Daynote.Desktop/Platform) |
| Who reloads WidgetKit | the iOS bridge | `libDaynoteWidgetBridge.dylib`, loaded into the app |

`GlanceStore.appGroup` picks the Mac form under `#if os(macOS)`, reading `DaynoteAppGroup` from the
extension's Info.plist (the build writes it), so another team changes one setting.

### Why the team-prefixed group

Apple's rule for `com.apple.security.application-groups` on macOS: an id of the form
`<TEAMID>.<name>` needs no registration on the developer site and no provisioning profile; the
`group.<name>` form must be registered and authorised by a profile. A Developer ID build of an app
that is otherwise unprovisioned therefore takes the team-prefixed form. Since macOS 15 the system
also protects group containers: a process whose signature does not carry the group (an ad-hoc
build, which has no team at all) gets a "would like to access data from other apps" prompt when it
touches the folder. That is why the bridge does nothing unless the build stamped the group into
Info.plist — it never touches the container from an ad-hoc build.

If the Mac app ever goes to the Mac App Store, the group stays team-prefixed (also valid there), but
both targets then need Mac App Store profiles (`DAYNOTE_APP_PROFILE`, `DAYNOTE_WIDGET_PROFILE`) and
the app itself has to be sandboxed — out of scope for direct download.

### The app side (`src/Daynote.Desktop/Platform/MacWidgetBridge.cs`)

- **Not before the day is read.** Nothing is drained or published until `App.InitializeAsync` has
  read the day and the lock state (`MacWidgetBridge.StartAsync`); an earlier publish would hand the
  widgets an empty day.
- **Publish.** Subscribes to `TodoPanelViewModel.Refreshed`, which every edit, tick, note save, sync
  pull and `@` capture already ends in, plus language changes and a midnight timer. Debounced
  400 ms; builds the snapshot from the rows the panel just read, the lists and the notes; writes it
  only if it differs (stamp excluded) from **the file on disk** — not from what the app last wrote,
  because the widget's read-modify-write of its optimistic tick can land after the app's newer
  snapshot. A watcher on `snapshot.json` triggers that comparison, so such an overwrite is undone;
  the app's own writes come back through it and compare equal. Then
  `WidgetCenter.reloadAllTimelines()` through the dylib — each reload costs the extension budget,
  hence the comparison.
- **Drain.** A watcher on `Glance/actions/` (both sides write a dot-name and rename it into place,
  so `Renamed` is the event), plus a drain at start and whenever the window comes forward. A drain
  asked for while one runs makes it run another pass, so a tick queued mid-drain is applied before
  the republish rather than un-ticked by it. Each file is applied with `GlanceActionApplier` — a
  completion completes, so a repeat is harmless — and deleted once applied or once it can never
  apply (unreadable, or naming a row that is gone, which the applier answers without throwing). One
  that throws — a busy database — or that the applier answers with `Retry` is kept for the next
  drain, for at most a day, as `GlanceCoordinator` does on the phone. A completion first cancels any
  tick the panel is holding on the same row (motion M3, `beforeComplete`), so the held tick cannot
  be written afterwards and untick it. Then the panel
  refreshes and the snapshot is republished regardless, replacing the widget's guess.
  `GlanceRelay` holds this logic; `GlanceRelayTests` drives it.
- **One folder for every profile.** The group container, and so `Glance/`, belongs to the Mac
  user, not to a Daynote profile (docs/PROFILES.md). A tick queued while one profile was showing
  and drained after the app relaunched into another is looked up in the other profile's store,
  finds no such row, and is dropped; the snapshot is simply rewritten for the profile now open.
- **Links.** `daynote://day?date=yyyy-MM-dd` (and a note's `daynote://note?date=…&id=…`) arrive as
  Avalonia `ProtocolActivatedEventArgs`; the window comes forward on that day. The phone's other
  links (`capture`, `todos`, §7) are not produced by the Mac widgets; if one arrives it opens today. The scheme is
  declared in `CFBundleURLTypes` only in a build that has the widgets.
- **Only when present.** It attaches when `Contents/Info.plist` has `DaynoteAppGroup`, the
  `.appex` and the dylib are in the bundle, and the dylib can resolve the group container.
  Otherwise — ad-hoc builds, Windows, `dotnet run` — it is null and nothing changes.

### The widget side (`native/mac`)

`project.yml` (XcodeGen; the generated `DaynoteMac.xcodeproj` is committed) has three targets:

| Target | What |
| --- | --- |
| `DaynoteWidgets` | The `.appex`, macOS 14+. Four `StaticConfiguration` widgets over one timeline provider: an entry every 15 minutes for six hours, one at each event's start and end, one just after midnight. The ring is a `Button(intent: CompleteTodoIntent)`; clicking anywhere else follows a `daynote://day` link. Pretendard is bundled from `src/Daynote.Desktop/Assets/Fonts` via `ATSApplicationFontsPath`. |
| `DaynoteWidgetBridge` | `libDaynoteWidgetBridge.dylib`, macOS 12+: `daynote_widgets_container(group)` and `daynote_widgets_reload()`. A dylib in the app's own process rather than a helper executable, because `WidgetCenter` answers to the calling bundle and only `Daynote.app` contains the widgets. |
| `DaynoteWidgetPreviews` | A command-line renderer: `DaynoteWidgetPreviews <snapshot.json> <out> [yyyy-MM-ddTHH:mm]` draws all four widgets light/dark × KO/EN plus ticked, empty, locked and no-snapshot boards with `ImageRenderer`, laid out like B8/B9. Set `DAYNOTE_WIDGET_FONTS` and `DAYNOTE_WIDGET_ASSETS` to `src/Daynote.Desktop/Assets/Fonts` and `native/mac/Widgets/Resources`. |

Strings come from the phone's `GlanceText` (the snapshot's `language`, not the Mac's), with the
Mac-only lines — the large widget's "클릭하면 Daynote에서 그 날짜를 엽니다" and the gallery
descriptions — in an extension beside the views. The week strip's dots are the day's note count, as
on the phone. List colours are `GlanceList.color`/`colorDark`, i.e. `AgendaListPalette`.

### Build, sign, notarize

`scripts/Build-MacApp.sh` builds the two native targets with `xcodebuild` for the RID's
architecture and embeds them **only when `DAYNOTE_SIGN_IDENTITY` is set and Xcode is installed**;
otherwise it prints why and builds the app without widgets, exactly as before.

```bash
# Distributable: a Developer ID Application identity of team 4T8C76SP99.
DAYNOTE_SIGN_IDENTITY="Developer ID Application: <Name> (4T8C76SP99)" scripts/Build-MacApp.sh -r osx-arm64
scripts/Notarize-MacApp.sh -a dist/mac/Daynote.app

# This Mac only (what the verification below used): an Apple Development identity of the same team.
DAYNOTE_SIGN_IDENTITY="Apple Development: Created via API (TUZYBFQ4CK)" scripts/Build-MacApp.sh -o dist/mac-dev
```

| Variable | |
| --- | --- |
| `DAYNOTE_TEAM_ID` | Default `4T8C76SP99`. Names the group; the script refuses an identity from another team, or one whose team it cannot read (the identity may be a name or a SHA-1). |
| `DAYNOTE_WIDGETS=0` | Build without widgets even with an identity. |
| `DAYNOTE_APP_PROFILE`, `DAYNOTE_WIDGET_PROFILE` | Embedded as `embedded.provisionprofile` when a channel needs them (Mac App Store). Developer ID needs neither: nothing either target uses is a restricted entitlement. |

Signing is inside-out and **without `--deep`**, which would re-sign the extension with the app's
entitlements and strip its sandbox: every file in `Contents/MacOS` (Mach-O executables — the app's
`Daynote.Mcp` — with the .NET JIT entitlements, libraries and the managed assemblies without), then
the `.appex` with `app-sandbox` + the group, then the app with the JIT entitlements + the group. All
with the hardened runtime and a secure timestamp, which is what `notarytool` requires; the existing
`Notarize-MacApp.sh` submits, staples and zips the whole bundle, extension included, unchanged.

Nothing was registered in App Store Connect for this: Developer ID needs no bundle id or profile for
these entitlements. The extension's id is its own, `cc.arachat.daynote.mac.widgets`, rather than the
iPhone's: that one is registered with platform IOS, which App Store Connect cannot change, and a Mac
App Store build would need a MAC_OS App ID of its own anyway. The team has no Developer ID Application certificate yet (only Apple
Development and Apple Distribution), so a notarized build is still the release checklist's step 1.

### Verified, and what only a person can check

Verified on this Mac (macOS 26.4, Xcode 26.6): `xcodebuild` builds all three targets; the bundle
builds with the extension and dylib embedded and passes `codesign --verify --deep --strict`; the
extension's signature carries the sandbox and the group, team `4T8C76SP99`; `pluginkit -m -i
cc.arachat.daynote.mac.widgets` lists it after LaunchServices registers a copy of the bundle; that copy,
run against a temporary `DAYNOTE_DATA_ROOT` and `TMPDIR` (so it neither shares the real profile nor
hands off to the running app), wrote `Glance/snapshot.json`, applied a queued `complete` action to
the database within the watcher's latency, and republished; and the snapshot it wrote renders
through the Swift model in the preview tool.

Not verifiable without the GUI: adding a widget to the desktop or Notification Center (the gallery
is drag-and-drop), the widget process actually running `CompleteTodoIntent` on a click, the
WidgetKit reload visibly redrawing it, and a widget click arriving as an OpenUri activation.
Gatekeeper rejects the Apple Development build by design; only a Developer ID build can be assessed
and notarized.
