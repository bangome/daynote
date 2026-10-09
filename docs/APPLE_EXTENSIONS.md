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

App Groups is enabled on all four App IDs. **The group itself has to be created and assigned in
the developer portal** (Identifiers → App Groups → +, then each App ID → App Groups → Configure):
the App Store Connect API has no endpoint for groups. Until it is, every profile carries an empty
group list and a device install refuses the App Group entitlement. After assigning it, run
`scripts/New-AppleGlanceProfiles.py` again (§8).

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
  the way the day panel does — by `itemId`, or by series and `RECURRENCE-ID` — and does nothing
  if it has gone or is already done. `capture` makes its item under the action's own `id`.
- `complete` on an occurrence writes the override `ToggleAgendaItem` writes in the app.
- `capture` with `task`/`event` is parsed again on the phone with `AgendaPhraseParser.ParseTrailing`
  and built with the `@` command's own `AgendaCapture.Compose` (default list, default alert on a
  to-do, no source note). `note`, or a sentence with no date, appends the text as a new line at
  the end of that day's first note, creating one if the day has none.
- **An extension marks its own guess in the snapshot** (the tick shows at once) and the app
  overwrites the whole file from its store after the drain.
- When: on launch (end of `InitializeAsync`), on every resume, and at once when the watch relay
  delivers something while the app runs. **Nothing applies while the app is not running** — a tick
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

`DaynoteBridge.framework` (iOS 15+) exports two C functions, called with `[DllImport("__Internal")]`
from `IosGlanceHost`:

| Function | Does |
| --- | --- |
| `daynote_glance_reload()` | `WidgetCenter.reloadAllTimelines()`, and `ControlCenter.reloadAllControls()` on iOS 18 |
| `daynote_glance_sync_activity()` | Starts, updates or ends the event Live Activity from the snapshot |

The csproj links it with a conditional `NativeReference`, so a plain `dotnet build` without the
native build still works; the first call then throws `DllNotFoundException`, the host stops
calling, and widgets fall back to their own quarter-hour timeline.

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

`scripts/Build-IosApp.sh` does all of it (`DAYNOTE_IOS_EXTENSIONS=0` leaves it out):

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
DAYNOTE_IOS_SIGN_IDENTITY="Apple Distribution: …" \
DAYNOTE_IOS_PROVISIONING="Daynote Glance cc.arachat.daynote" scripts/Build-IosApp.sh
scripts/Build-IosApp.sh -t simulator
```

Tests: `xcodebuild test -project native/apple/Daynote.xcodeproj -scheme DaynoteKitTests
-destination platform=macOS` (parser vectors, readbacks, shared-folder round trip);
`-scheme DaynoteRenderTests` on an iOS simulator with `TEST_RUNNER_DAYNOTE_RENDER_DIR=<folder>`
draws every widget and watch screen to PNG, light and dark, Korean and English, for comparison with
the design renders.

## 9. Not built, or not verifiable here

- Notification actions 완료 / 30분 뒤 다시 / 회의 노트 열기 (watch design §05, "폰 알림에도 같은 동작"):
  see the report for status.
- Widgets cannot run code at midnight or at an event's start except through their own timeline;
  the provider lays out entries for each quarter hour, each event boundary and midnight.
- The widget gallery's own names follow the system language (a string catalog); everything a
  widget draws follows the app's.
