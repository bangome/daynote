# To-dos and events as entities (design)

> **Status 2026-10-08: to-dos and events sync end to end; nothing reads them.**
> Migration 005 creates the tables, `Daynote.Core.Agenda` holds the model and
> `SqliteAgendaRepository` reads and writes it. Migration 006 makes every agenda edit queue —
> triggers, tombstones, and two new entities on the outbox — and `SqliteSyncStore.Agenda.cs`
> drains and merges them against `AgendaPayload`'s wire format. Migration 0013 on the Worker and
> `SyncEngine.Agenda.cs` close the loop: a to-do made on one device is on the other after a sync,
> and the server sees only the envelope. What is still missing is every reader. Every
> panel still parses `-[ ]` out of note bodies through
> [`TodoParsing`](../src/Daynote.Presentation/Notes/TodoParsing.cs), the `@` command does not exist,
> the one-time migration of §8 is written but deliberately not registered — §12 requires desktop
> and phone to cut over in the same release, so the store lands first and unused. [CLOUD_SYNC.md](CLOUD_SYNC.md)
> describes the sync engine §9 would add entities to; that part is not built either.

## 1. Decisions taken

| Question | Decision |
| --- | --- |
| Where a to-do lives | **Its own entity**, not a line of note body. The body goes back to being prose |
| Relationship to the note | **None, beyond a one-way `source_note_id` used only for navigation.** No references in the text, no round-trip, no markers |
| Creating one | An **`@` command in the editor**. It reads the line it sits on, creates the entity, and leaves the typed text alone as plain prose |
| To-dos and events | **One entity** with a `kind` discriminator, not two. They differ by whether a time range is set |
| Recurrence | **RRULE**, even where the UI offers three choices. Exceptions as **EXDATE** + **RECURRENCE-ID** overrides |
| Times | **Wall-clock time plus an IANA zone id**, never a UTC instant. See §6 |
| Interop target | **iCalendar (RFC 5545)**. Our model is a superset; every field that has an iCal name uses it |
| First integration | A **published `.ics` subscription feed** from the Worker. Then **one** two-way client: CalDAV |
| `-[ ]` in the body | Stops feeding the to-do panel. One-time migration, text left untouched. See §8 |
| Lists | **Their own entity**, created and managed by the user. Anything unassigned goes to a default list |
| An item created with no date | A task **starting today** — `DTSTART` today, no `DUE` — whatever date the note carries |
| Timeline | Shows **notes and events together**, and is **editable** — it stops being a read-only view |
| A repeating task in the Timeline | **Per-item option**, defaulting to hidden. Not a global setting |
| Lists and CalDAV collections | **Map onto collections the user already has. Daynote never creates or deletes one.** See §10.1 |
| Desktop and phone | **Cut over in the same release.** Phone reminders already ship against the parsed model |

## 2. What the text-parsed model cannot do

Three limits, and only the third is structural.

**A to-do has no stable identity.** `ReminderPlanner.IdFor(noteId, text, occurrence)` derives it from
the body text, deliberately excluding the line index and the due stamp so that reordering lines does
not swap reminders. The cost is that **editing the wording makes it a different to-do**: its reminder
is cancelled and a new one scheduled.

**A checkbox holds one bit.** `ToggleLine` flips `[ ]` to `[x]` in place. A repeating task needs to
say "this Monday done, next Monday not", and there is nowhere to put that.

**Recurrence breaks "one line, one to-do".** A repeating line is a *rule*; the to-dos are the
instances it generates. That is a 1:N relationship, and no amount of grammar makes a single line of
text hold it. This is the reason the model has to change rather than grow.

There is a fourth, quieter cost. Checking a box today **edits a document the user wrote weeks ago**.
A meeting note is a record of a moment; finishing a task afterwards should not rewrite it.
Separating the two fixes that, which is a reason to do it even setting recurrence aside.

## 3. The entity

One table, one sync entity, `kind` telling tasks from events. Columns are named for their iCal
counterparts wherever one exists.

| Field | Type | iCal | CalDAV | Todoist | MS Graph To Do |
| --- | --- | --- | --- | --- | --- |
| `id` | uuid | `UID` | `UID` | *(external ref)* | *(external ref)* |
| `kind` | `task` \| `event` | `VTODO` / `VEVENT` | same | task only | task only |
| `title` | text | `SUMMARY` | `SUMMARY` | `content` | `title` |
| `description` | text | `DESCRIPTION` | `DESCRIPTION` | `description` | `body.content` |
| `list_id` | uuid | *(the collection)* | collection | `project_id` | `todoTaskList` id |
| `due_at` | wall clock | `DUE` | `DUE` | `due.date` / `.datetime` | `dueDateTime` |
| `has_due_time` | bool | `DUE` is DATE vs DATE-TIME | same | which `due` field | — |
| `starts_at` / `ends_at` | wall clock | `DTSTART` / `DTEND` | same | — | — |
| `tz` | IANA id | `;TZID=` | `;TZID=` | `due.timezone` | `…DateTime.timeZone` |
| `rrule` | text | `RRULE` | `RRULE` | `due.string` *(natural language)* | `recurrence.pattern` |
| `exdates` | wall clock[] | `EXDATE` | `EXDATE` | — | — |
| `status` | enum | `STATUS` | `STATUS` | `checked` | `status` |
| `completed_at` | instant | `COMPLETED` | `COMPLETED` | `completed_at` | `completedDateTime` |
| `priority` | 0–9 | `PRIORITY` | `PRIORITY` | `priority` 1–4 | `importance` |
| `alarms` | lead[] | `VALARM`/`TRIGGER` | same | `due` reminders | `reminderDateTime` |
| `timeline_visibility` | `auto`\|`always`\|`never` | — | — | — | — |
| `source_note_id` | uuid? | `X-DAYNOTE-NOTE` | same | — | — |
| `created_utc` / `updated_utc` | instant | `CREATED` / `LAST-MODIFIED` | same | `added_at` / `updated_at` | `created…` / `lastModified…` |
| `external_refs` | list | *(local only)* | | | |

Three of these are easy to leave out now and expensive to add later.

**`list_id`.** Every task service has a container — a Reminders list, a Todoist project, a To Do
list. Daynote's to-dos are keyed to a date and have no container at all, so a sync has nowhere to
put them. Lists are **their own small entity**, created, renamed and deleted by the user:

| Field | Type | Notes |
| --- | --- | --- |
| `id` | uuid | What a CalDAV collection or a Todoist project maps onto |
| `name` | text | |
| `sort_order` | int | |
| `is_default` | bool | Exactly one. Anything created without a list lands here |

Reusing the existing tags for this was considered and dropped: a tag is per *note* and a container
is per *to-do*, so sharing the vocabulary would not have let them share the table. A list is also
something a user wants to rename and reorder, which tags are not.

The default list cannot be deleted, and deleting any other list moves its items to the default
rather than taking them with it — a container is not a reason to lose a task.

**`priority`.** Free now. Note the scales disagree: iCal `PRIORITY` is 1 = highest and 9 = lowest
with 0 meaning unset, Todoist's `priority: 4` is its p1, and Graph has only low/normal/high. Store
the iCal scale and map outward.

**`external_refs`.** A list, not a column:

```json
[ { "service": "caldav",  "id": "…", "etag": "…",  "synced_utc": "…" },
  { "service": "todoist", "id": "…", "etag": null, "synced_utc": "…" } ]
```

A single `external_id` column works until the second integration and then forces a migration.

## 4. Not fields

- **No link back into the note text.** No marker, no reference, no embedded block. The body stays
  plain text, which is what keeps export, backup, the MCP server and the privacy claim intact.
- **No per-instance rows.** Occurrences of a repeating rule are generated for display, not stored.
  Only exceptions and completions are persisted (§5).
- **No subtasks.** Nothing in the first cut needs them, and they are the field most likely to be
  modelled differently by whichever service we integrate with second.

## 5. Recurrence, exceptions and completion

The rule is an `RRULE` on the entity. Occurrences are computed. Two things are stored:

- **`EXDATE`** removes an occurrence outright ("skip this week").
- A **`RECURRENCE-ID` override** is a second row carrying the same `UID` plus the original
  occurrence's start, holding whatever differs — a moved time, or `STATUS:COMPLETED` with its
  `COMPLETED` stamp.

Per-occurrence completion is therefore the override mechanism, which is what RFC 5545 intends. **The
catch is client support, not the standard.** Apple's Reminders collapses a repeating to-do into a
single item that rolls forward when completed; Todoist keeps one task with a last-completed date.
Neither will reconstruct our completion history. The rule for export is: **emit the next outstanding
occurrence only**, and accept that history stays ours. Our model is a superset; it must not pretend
the receiving app can hold all of it.

This replaces the `done: ["2026-10-06", …]` array sketched in discussion. It is the same information
in the shape every calendar tool already speaks.

## 6. Times are wall clock plus a zone, never an instant

A repeating "every Monday 07:00" is a repetition of a **wall-clock time**, not of an instant. Across
a DST boundary the absolute time must change for the alarm to stay at 07:00. Storing a UTC instant
loses the information needed to do that, and it cannot be recovered afterwards.

So: `due_at` is a local wall-clock value and `tz` is an IANA zone id, exactly as iCal writes
`DTSTART;TZID=Asia/Seoul:20261008T070000`.

The present code is already confused on this point, harmlessly because Korea has no DST:

```csharp
// TodoParsing.cs — the offset of *today* stamped onto a future date
return new DateTimeOffset(now.Year, month, day, hour, minute, 0, now.Offset);

// ReminderPlanner.cs — and thrown away again, because it means nothing
DateTime wall = DateTime.SpecifyKind(due.DateTime, DateTimeKind.Unspecified);
```

The model knows the offset is meaningless and the type still carries it. Fix it at the same time as
the split, not after.

## 7. Creating one from the body: the `@` command

Typing is the only entry point worth optimising; a day-note app loses if capturing a task means
leaving the note.

```
- 예산안 초안 공유 @내일 오전 9시
                   ┌──────────────────────────────┐
                   │ ✓ 할 일   예산안 초안 공유      │
                   │           10월 8일 (수) 09:00  │
                   │ ─────────────────────────────│
                   │ ▦ 일정    09:00 – 10:00        │
                   └──────────────────────────────┘
                     Enter 만들기 · Tab 종류 전환 · Esc 취소
```

- The **title comes from the line to the left of `@`**, so there is nothing extra to type.
- **After `@` is date, time and recurrence only**, in natural language: `내일`, `금요일`, `10/8 9시`,
  `매주 월 7시`. The popup **reads the parse back as a sentence** so a misreading is visible before
  Enter.
- **Tab switches task/event.** They differ only by a time range, so the switch must be cheap.
- **Esc creates nothing and leaves the text.** `@` is an ordinary character; trigger only at a word
  start after whitespace, and dismiss silently when nothing matches.
- **What stays in the body is exactly what was typed.** `@` is an input accelerator, not a binding.
- **With no date given, the item is a task starting today** — `DTSTART` today, no `DUE` — regardless
  of the note's own date. A task typed into last Tuesday's note is something the user is dealing
  with now, not something that was due then; starting it today puts it in front of them without
  inventing a deadline, and nothing arrives already overdue.
- **With no list given, it lands in the default list.** Choosing a list is never required to capture
  something.

**The design is the `Daynote B Tasks - Events` set** and its phone counterpart, one file per
section — see [design-renewal/README.md](design-renewal/README.md). It keeps the Desktop B shell
that shipped and changes four places: this popup, the creation confirmation below, the sidebar's
lists, and the day panel. Both documents share a reference moment, Wednesday 7 October 2026 at
14:30, which half their examples turn on.

**Built, as `AgendaPhraseParser`.** The reading half only; the popup and the editor hook are not.
What it settles, beyond what the prose above says:

- **Both languages at once.** A Korean UI does not stop someone typing `tomorrow`, and the
  vocabulary is small enough that recognising both costs nothing. Parsing in the UI language would
  fail the bilingual user in the one place where failing means the popup silently does not appear.
- **A small bare hour is the afternoon.** `3시` and `3:30` are 15:00, because nobody schedules a
  meeting for three in the morning; 7 and later are left as typed. This is a guess, and the
  readback line is what makes it visible before Enter — which is the whole reason the readback is
  a sentence rather than a field.
- **A one-character Korean weekday only counts after `매주`.** On its own it is far too eager:
  `@일정 잡기` would be Sunday and `@수정 필요` Wednesday.
- **An alphabetic word has to end where it says it does**, or `@everyone` is a weekly rule.
- **The longest readable prefix wins and trailing text is ignored.** Requiring the whole remainder
  to parse makes the popup flicker away on every half-typed word — `내일 오` is not a date and
  `내일 오전 9시` is. What the *first* token cannot start is no match at all, and that is what keeps
  `자료는 @지원 님께` an ordinary sentence.
- **No year is ever typed**, so `@1/3` in October is next January rather than ten months ago, and
  a weekday means the next one — a day that has already started is not what someone is scheduling.
- **An event made from a bare time runs an hour**, and from a bare day is all day. The popup reads
  both lines back side by side, which is what makes Tab cheap.

Not built here: `FREQ=MONTHLY` and anything with a count or an until. The design shows neither, and
a rule the readback cannot state in one line is a rule the user cannot check.

**Confirmation is the object appearing, not a toast.** The day panel is already on screen; the new
item sliding into it is the honest signal that something was created. For another date, one
transient line in the panel — "10/8에 추가됨 · 보기" — is enough. Silent creation with no visible
change is the failure mode to avoid.

## 8. Retiring `-[ ]`

If body checkboxes keep feeding the panel while `@` also creates entities, the same task can exist
twice and the user has to decide which kind to write. That is the outcome this whole design exists
to avoid, so `-[ ]` has to stop being a source.

1. **One-time migration.** Walk every note, create an entity per `-[ ]` line, carry `[x]` across as
   completed. **Leave the body text exactly as it is** — deleting it would be the app rewriting the
   user's own writing. Take an automatic backup first; this is not reversible.

   **Built, as `TodoCaptureMigration` (version 7), and not registered.**
   `MigrationRunner.FromEmbeddedResources()` still stops at 6, so no database runs it; step 3 adds
   it to the set and that is the whole change. What it settles:

   - **Ids are derived, not generated.** Both devices run this offline against bodies that already
     synced, so random ids would hand the user every task twice. The id is a UUIDv5 over the note
     id, the task text, and how many identical texts precede it in that note — so a line moving up
     or down the note keeps its id, two genuinely identical lines stay two tasks, and both devices
     land on the same row. The failure window is an edit made on one device between the two
     migrations; that duplicates, and no keying avoids every case.
   - **Timestamps come from the note, not the clock.** `created_utc`, `updated_utc` and the
     `completed_utc` of a `[x]` line are all the note's own `updated_utc`: deterministic across
     devices, so the two migrations do not flap against last-write-wins, and the best evidence
     there is of when the line was last touched.
   - **A task with no due stamp starts on its note's date, not today.** §7's "starts today" rule is
     for live capture. Applied to history it would empty years of notes onto this morning's panel.
   - **A `(M/D)` stamp takes its year from the note it was written in.** Today's year would be
     wrong for every note older than January.
   - **A bare `- []` is skipped.** It is formatting the user left behind, not a task, and an
     untitled row in the panel is something nobody can act on. Something that only looks like a due
     stamp — `(13/40)` — stays in the title rather than being dropped.
   - **The grammar moved to `Daynote.Core.Agenda.TodoBodyScan`** and `TodoParsing` now reads it
     from there. The migration has to find exactly the lines the panel has been showing; two copies
     that drifted by one character would leave some of them behind as plain text.
   - **`MigrationRunner` gained code steps** so this runs in the same transaction as a schema
     change, is recorded as a `schema_versions` row rather than a flag, and therefore runs once per
     device — the three properties §12 asks for. A code step that throws rolls the whole thing back
     and stays unrecorded, so the next launch retries against an untouched database.

   Still owed by step 3: the automatic backup before it runs, which belongs with the startup call
   that registers it.
2. **Keep the keystroke, change what it does.** Typing `-[]` at the start of a line opens the same
   popup as `@`. Muscle memory survives; the model does not fork. Afterwards `-[ ]` in a body is
   just text that looks like a checkbox.

## 9. Sync

This adds **two new sync entities** — items and lists — which is the real cost of the separation and
is worth stating plainly. Notes ride as one opaque encrypted envelope per note, so anything *inside*
a note is free; a sibling entity is not. Each needs its own table on the Worker, its own
last-write-wins rule on `updated_utc`, and its own tombstones — the same shape as notes, in
[CLOUD_SYNC.md](CLOUD_SYNC.md).

Recurrence overrides share the parent's `UID` and must be carried as rows in their own right, so the
conflict rule applies per row, not per series.

Lists are small and rarely edited, so they are cheap to sync, but they are a **foreign key the items
depend on**. A device that applies items before lists would show tasks with no container. Pull lists
first, and on an item whose `list_id` is unknown, fall back to the default list rather than dropping
it.

**Built, as of migration 006:** the outbox and tombstone tables carry `agenda_item` and
`agenda_list`, triggers on both tables queue every write, `AgendaPayloadCodec` is the wire format
(iCalendar's names wherever iCalendar has one, so the `.ics` feed and CalDAV are built from the
same shape), and `SqliteSyncStore.Agenda.cs` holds the two merges. Three rules it settles that the
prose above only implies:

- A series is pushed, and applied, **before any override of it** — an override is a foreign key
  onto the row carrying the rule, so the order a page happens to be in must not decide whether it
  lands.
- `is_default` is **derived from the fixed id, never taken from the wire**. The column is unique,
  so honouring a remote claim would make applying a page depend on arrival order and fail outright
  when two rows claim it. A remote delete of the default list is refused for the same reason: it
  is where everything else lands.
- Agenda `_utc` columns were writing a second timestamp format. 006 normalises them to
  `DateTimeOffset.ToString("O")` like every other table, because `AcknowledgePushAsync` matches
  `queued_utc` as an exact string and a second format would leave every agenda row queued forever.

**Built, as of Worker migration 0013:** one `agenda` table holds both kinds — an agenda row
carries nothing a note row does not, so two tables would be the same five columns twice and a
fourth join in the pull — and `change_log` was rebuilt to allow the two new entities, copying
`seq` verbatim so no client's cursor moved. They ride `/v1/sync/push` and the notes' cursor, and
they are **free**: the paywall is on attachment bytes and nothing else. `SyncEngine.Agenda.cs`
pushes lists before items in one request, so a list takes the lower `seq` and a device pulling
from zero meets the container before its contents.

One thing the client has to get right about an old deployment: a Worker that predates this
answers the push without the new arrays at all. That silence is read as *unsupported*, not as
*rejected* — reading it as a rejection would clear the queue and lose the to-dos. The run reports
`AgendaSyncUnsupported` and the next run after the service is updated sends them.

**Not built:** nothing reads any of this. No panel, no Timeline, no `@` command, and the one-time
`-[ ]` migration of §8 has not been written, so the queue fills, drains, converges, and is
invisible.

## 10. Integrations, in order

**Build one export shape and one sync client, not N integrations.** The long tail arrives through
CalDAV at no extra cost: Apple Reminders is a CalDAV VTODO store, and so are Fastmail, Nextcloud
Tasks, Synology, mailbox.org, Posteo and any self-hosted Radicale or Baïkal.

| Tier | What | Cost | Why |
| --- | --- | --- | --- |
| 1 | **`.ics` subscription feed** from the Worker | one handler | Google, Apple and Outlook all subscribe by URL. No OAuth, no SDK, no per-platform code |
| 1 | **MCP tools** for to-dos | a few tool definitions | `src/Daynote.Mcp` already reads and writes notes. Structured entities make this trustworthy; regex over body text never could |
| 2 | **CalDAV**, two-way | one client | Apple Reminders plus the whole self-hosted tail, once |
| 2 | **iOS EventKit** | moderate | The only OS with a system task store to write into. Android and Windows have none |
| 3 | Microsoft Graph To Do | moderate | Good API with recurrence, and the natural Windows answer since there is no OS store |
| 3 | Todoist | moderate | Best API of the group; recurrence is a natural-language string, so RRULE needs translating both ways |
| — | Google Tasks | — | **Skip.** No recurrence in the API and poor time handling, whatever its reach |
| — | Jira, Asana, Linear | — | **Skip.** Work trackers; wrong audience for a day-note app |

The feed is a bearer URL, so it must carry a per-user secret token and be revocable. **It cannot
serve accounts with the opt-in lock on** (CLOUD_SYNC.md §4.1b): the Worker has no key for those, and
the honest answer there is local export, not a degraded feed.

**Two-way sync is permanent maintenance.** Each one owns a conflict rule, delete propagation, etag
handling, token expiry and a service quota, forever. One is a feature; five is a bug factory. Most
users asking to "connect my tasks" want them *visible* in their calendar or Reminders, which tier 1
already gives.

### 10.1 Lists and CalDAV collections

A list is the obvious counterpart of a CalDAV collection, and the tempting design is one collection
per list, created by us. **Do not create collections.** Three reasons, in order of how much they
would cost:

- **It is a destructive capability we do not need.** A sync bug that creates collections leaves
  litter in someone's iCloud or Nextcloud account; one that deletes them takes real data with it.
  There is no version of "we got the list diff wrong" that is survivable if we hold `MKCOL` and
  `DELETE` on collections.
- **Support is uneven.** Extended `MKCOL` is optional, and several servers refuse collection
  creation over CalDAV or restrict it to their own web UI. A design that needs it works on iCloud
  and fails on someone's Baïkal with an error we cannot explain.
- **It puts the wrong thing in charge.** Where a person's tasks live in their own account is their
  decision, not a mirror of how they happened to organise Daynote.

**So: map, do not create.** The user connects an account, Daynote lists the collections already
there, and each Daynote list is pointed at one of them. The mapping is stored on the list entity in
the same `external_refs` shape the items use:

```json
{ "service": "caldav", "connection": "<account id>", "id": "/calendars/me/work-tasks/" }
```

The rules that fall out of it:

- **An unmapped list does not sync.** Not an error, not a silent default — it stays local and says
  so. Choosing nothing is a legitimate choice, including for the default list.
- **Two lists cannot share a collection.** Items would merge on the way out and be
  indistinguishable on the way back.
- **A collection that disappears unmaps the list** and leaves its items alone. Remote deletion of a
  container is never a reason to delete local tasks.
- **The mapping syncs between the user's own devices; the credentials do not.** A collection href is
  just a path and belongs with the list. The account password or token stays on the device that
  holds it, so the phone will show the list as mapped but not connected until the account is added
  there too.

Flattening every list into one collection with `CATEGORIES` was the alternative, and it is worse in
the way that matters: Apple Reminders and the rest show one undifferentiated list and ignore the
categories, so the user loses exactly the organisation they were syncing for.

"Create a collection for me" can arrive later as an explicit, one-at-a-time action the user presses
— never as something the sync does on its own.

## 11. Where they appear

**The Timeline carries notes and events together.** It stops being a list of notes in time order and
becomes the day's record and the day's schedule in one column, which is the only place in the app
where "what happened" and "what is booked" can be read against each other. Events sort into the same
stream by start time; a note keeps its existing card, an event gets a narrower one with its time
range. Items without a time — a task due on the day, a note with no stamp — group above the timed
run rather than being given a false position.

**The Timeline becomes editable.** Today `TimelineView` only displays; with events in it, reading a
schedule you cannot adjust is the wrong half of the feature. The first cut is deliberately modest:

- **Click an event** → the same popover the day panel opens: title, time range, repeat, list,
  alarm. One editor, two entry points, no second implementation to keep in step.
- **Title edits inline**, because renaming is the common case and a popover for it is friction.
- **No drag to reschedule yet.** Dragging on a surface that also scrolls, on touch as well as
  mouse, and against a Timeline that scales time non-linearly, is its own piece of work. It should
  arrive once the popover path is solid, not with it.
- **Notes keep their current behaviour** — click to open the note. The Timeline gains editing for
  events only.

**A repeating task shows in the Timeline only if its own option says so.** `timeline_visibility`
defaults to `auto`, which means *events yes, one-off tasks yes, recurring tasks no* — a daily rule
would otherwise be on every day forever and the Timeline would stop being a record of anything.
`always` and `never` are the per-item overrides, so the one routine a person genuinely wants to see
in their day can be, without dragging the other five in with it. A global switch was considered and
rejected for exactly that reason: the useful answer differs per rule, not per user.

The field has **no iCal counterpart** and is not exported. It is a view preference, and a receiving
app has its own.

**The day panel stays the working list.** Tasks for that date, in one list regardless of where they
came from, with a repeat mark on occurrences of a rule. This is what someone looks at in the
morning, so it must not be split by origin.

**Lists get a sidebar home**, beside the existing 할 일 entry: the lists themselves, and a count.
Selecting one filters the cross-date task view. The default list is first and cannot be removed.

## 12. Cutting over desktop and phone together

Phone reminders already ship against the parsed model (`ReminderPlanner`, `ReminderCoordinator`,
`ReminderStateStore`). Both shells therefore have to move in the **same release**: if the phone
still parses `-[ ]` while the desktop has migrated those lines into entities, every migrated task
exists twice and reminds twice.

The order that makes that safe:

1. Entities, lists and their sync land first, with nothing reading them yet. *(Done: migrations
   005 and 006 locally, 0013 on the Worker, and the engine drives both ends.)*
2. The migration runs once per device and is **idempotent and recorded** — a `schema_versions` row,
   not a flag in settings — because two devices will both try it. *(Done: `TodoCaptureMigration`,
   version 7, written and tested but not yet in the runner's set. See §8.)*
3. Desktop and phone switch their readers in the same version, and `TodoParsing` stops feeding
   panels on both at once.

   **In progress.** The order below keeps `main` buildable at every step; nothing user-visible
   changes until the last two, which go together.

   - [x] `AgendaDay` — which rows a day has and in what order, shared by both panels. The two
         shells lay them out differently (§99 calls that intended) but *which* rows there are is
         one decision: two implementations of "what is due today" is how the same to-do ends up on
         one screen and not the other.
   - [ ] Desktop day panel and the 할 일 tab read it.
   - [ ] Sidebar lists (§04), and the phone's Lists tab (phone §03).
   - [ ] Ticking a row writes the entity's status — and, on an occurrence, an override against its
         `RECURRENCE-ID` rather than the series, or "I did it this Monday" becomes "I did it every
         Monday". Today both shells rewrite the note body instead.
   - [ ] Phone Day screen (phone §04) and the `@` bar (phone §01).
   - [ ] `ReminderCoordinator` reads entities rather than note bodies.
   - [ ] Register `TodoCaptureMigration`, with the automatic backup §8 requires in front of it.
   - [ ] `TodoParsing` stops feeding panels; the desktop's empty-state copy and the phone
         toolbar's `-[]` button go with it (§99).
4. `ReminderPlanner.FireTime` keeps its shape; only its input changes from `TodoLine` to the entity.
   The lead-time hook its comment already describes is where `VALARM` triggers arrive.

   **Built, as `ReminderPlanner.Agenda.cs`, and not called.** `ReminderCoordinator` still reads
   note bodies; step 3 swaps the call. What it settles:

   - **A lead is subtracted, so one item can hold several reminders.**
     `AlarmLeadMinutes` is the `VALARM` trigger list, and the id carries the lead so two alarms on
     one item do not replace each other.
   - **An empty alarm list is not "no reminder"** for a task: it is one reminder at the item's own
     time, which is what every dated to-do has done since reminders shipped and what every row the
     §8 migration writes. An **event** with no alarm is silent — a block of time is not something
     to be nagged about unless the user asked for it.
   - **A task's `DTSTART` is a day, not a clock reading.** The migration sets it to midnight of the
     note's date; taking that literally would remind everyone at 00:00, so a task with no `DUE`
     reminds at the default time. An event's start is taken literally.
   - **Reminder ids follow the item, not its text.** Renaming a to-do used to cancel its reminder
     and schedule a different one, because a line of prose has no identity and the id had to be
     rebuilt from its text. `Diff` needs no special case for the changeover: every old id is
     absent from the new set and is cancelled in the first pass after the upgrade.
   - **The body names the list** when it is not the built-in one, in place of the note title the
     line-based planner used. An entity's context is its list; the note it happened to be typed
     into is a jump target, not a label.

   **Built: a repeating to-do reminds**, on every occurrence inside a sixty-day horizon, honouring
   `EXDATE` and overrides. Sixty days and not a year because iOS holds 64 pending notifications
   and the set is topped up on every run, so computing further buys nothing.

   Turning the expander on exposed a reading that had been wrong since this file was written. A
   repeating to-do keeps its clock in `DTSTART`, because that is what an `RRULE` anchors on, and
   `FireTime` looked for it in `DUE`, found none, and quietly moved every repeating to-do to the
   default hour. `has_due_time` now means "this to-do carries a clock reading" whichever field is
   holding the wall clock, which is one field doing one job rather than two fields that have to
   agree.

   **Still not built: a rule the expander cannot read** — monthly, yearly, a positional `BYDAY`.
   Those are silent, deliberately: a to-do that reminds on the wrong day is worse than one that
   does not remind.

Until step 3 ships on both, the migration must not run anywhere.

## 12.1 What the design asks of the other shell

The phone document ends with a list of every concept that exists on one side only, because both
switch in the same release. Transcribed in
[design-renewal/99-divergences.md](design-renewal/99-divergences.md); the two that are work on
the desktop:

- **"이 노트의 항목 N".** Built for the phone, where the to-do list is on another tab and the
  slide-in cannot play the part of "the object appeared". The desktop needs the same count and
  list beside its note-tab row.
- **The 알림 field in the desktop popover.** The desktop never rings. Relabel it 휴대폰 알림, and
  show the same unsupported notice on a repeating item that the phone shows.

And one that is neither shell's feature but both shells' copy: the desktop's empty-state still
tells the user to type `-[] 할 일`, and the phone toolbar still has a button that inserts one.
Both go when step 3 switches the readers.

## 13. Open questions

- **Drag to reschedule in the Timeline** (§11): worth doing, but it needs its own interaction pass
  across mouse and touch, and the Timeline's time axis is not linear.
- **Whether an unmapped list should be offered an export** instead of simply staying local — a
  per-list `.ics` would cover the user who wants their work tasks visible but not writable.
- **What the `@` popover offers for list choice** when the user has many. A recent-first short list
  is probably right, but it is the one part of the capture flow that can get slow.
- ~~**Expanding `RRULE` into occurrences**~~ — built, as `AgendaRecurrence`. `FREQ=DAILY` and
  `FREQ=WEEKLY` with `INTERVAL`, `BYDAY`, `COUNT` and `UNTIL`, honouring `EXDATE` and overrides.
  Anything else — monthly, yearly, `BYSETPOS` — is reported unreadable and expands to nothing,
  because a rule half-understood puts occurrences on the wrong days and a to-do that silently
  appears on the wrong day is worse than one that visibly does not appear.
  The reminder planner uses it, so §12 step 4's gap is closed, and §06 was redrawn to match: the
  unsupported-alert notice now appears only for a rule the expander refuses, and names it —
  "이 반복(매월 25일)은 아직 알림을 보내지 않아요. 매일·매주 반복은 알림이 옵니다." Naming a rule
  and scheduling one are separate jobs with different standards, so `AgendaRecurrence.Summarize`
  will name what `Expand` refuses: putting an occurrence on the wrong day is a broken to-do, while
  calling a rule by a slightly wrong name is cosmetic and saying nothing at all is worse.
- ~~**Whether a dated to-do should be allowed no reminder at all.**~~ Settled by the phone's §06.
  A dated item is *created* carrying one alert, which the user can remove, so the behaviour every
  dated to-do has had since reminders shipped survives as a visible default and an empty list now
  means exactly what it says. The planner used to read empty as "the usual one" because there was
  nowhere to say otherwise; it no longer guesses. The sheet offers 정각 / 5 / 10 / 30분 전 / 1시간
  전 / 하루 전 오전 9:00 / 직접 설정, and the last of those is a lead like any other — iCalendar
  has one kind of trigger, a duration before the item, so it depends on the item's own time.
