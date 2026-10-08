# Design documents

Canvas documents exported from the design project, rendered by `support.js` (the generated
dc-runtime — do not edit it by hand).

Open one by loading the `.dc.html` in a browser from this directory, so the relative
`<script src="./support.js">` resolves.

| File | Covers |
| --- | --- |
| `Calendar Notes.dc.html`, `Calendar Notes v2.dc.html` | The original product design |
| `Daynote v3.dc.html` | The v3 palette and shell, shipped 2026-08-31 |
| `Daynote Account.dc.html` | The account window |

## To-dos and events

One document per section, because the tool that reads a file out of the design project caps at
256 KiB and the originals were larger — they arrived cut in the middle of a tag, with no sign that
anything was missing. The split set below is complete; the two truncated originals were deleted
once it landed.

Desktop (`Daynote B Tasks - Events …`), on the Desktop B shell that shipped:

| § | Covers |
| --- | --- |
| `01 - Command` | The `@` popup: the readback, Tab, and all seven states |
| `02 Creation Feedback` | The body chip, the slide-in into the day panel, the other-date notice |
| `03 Timeline` | Notes and events in one column, and what becomes editable |
| `04 Day Panel - Lists` | 이 날의 할 일, and the lists in the sidebar |
| `05 Phone Width` | The sidebar collapsing when the window is narrow |

Phone (`Daynote Mobile B Tasks - Events …`):

| § | Covers |
| --- | --- |
| `01a`, `01b - Bar` | The `@` bar in the keyboard accessory slot, in two parts |
| `02 Creation Feedback` | What stands in for "the object appeared" when the list is another tab |
| `03 Lists` | To-do lists as a chip row under the segmented control |
| `04 Day - Events` | Where events live on a phone with no timeline |
| `06 Alerts` | The alert stack, the default, "알림 없음", and the repeating-to-do notice |
| `99 Divergences` | Transcribed to [`99-divergences.md`](99-divergences.md) — it is small enough that the tool returns it inline rather than as a file |

Both documents share a reference moment, Wednesday 7 October 2026 at 14:30. Half their examples
turn on what has already passed that day, so a mock drawn at any other time would not line up.
