# Where the phone and the desktop diverge

A transcription of `Daynote Mobile B Tasks - Events 99 Divergences`, which is small enough that the
design tool returns it inline rather than as a file. Re-import the canvas version any time; this is
here so the list is readable in the repository and can be ticked off.

Both shells switch in the same release (docs/TODOS.md §12), so a concept that exists on one side
only is a bug waiting to happen. The designer's own words, grouped by what they ask of us.

## Needs a desktop change

| | |
| --- | --- |
| **"이 노트의 항목 N"** | Built for the phone, where the to-do list is on another tab and the slide-in cannot play the part of "the object appeared". The desktop has no equivalent. Proposal: the same count and list to the right of the note-tab row, beside "1 / 4 ▾". |
| **알림 필드** | The desktop popover (3a, 3d) offers "알림", but the desktop never rings. Relabel it "휴대폰 알림", and show the same unsupported notice on a repeating item that the phone shows. |

## Agreed to unify

| | |
| --- | --- |
| **완료 표현** | The desktop list view uses a `[진행 중 | 완료]` segment; the day panel and the whole phone use a collapsed "완료 N" row. Make the desktop list view a collapsed row too. |
| **`-[ ]` 문법** | Replace the phone toolbar's "할 일 (`-[]` 삽입)" button with "@ 할 일·일정". The desktop's empty-state copy still tells the user to type `-[] 할 일`. |

## Phone changes to match the desktop

| | |
| --- | --- |
| **체크 모양** | Mobile B's rounded-square checkbox becomes the desktop's circular ring. The ring's colour is the list's colour. |

## Phone gains something the desktop has

| | |
| --- | --- |
| **타임라인 표시 (자동 / 항상 / 안 함)** | It belongs to a desktop-only screen but it is a property of the item, so the phone's edit sheet exposes it as "데스크톱 · 타임라인에 표시" — on repeating to-dos only (6d). |

## Deliberately different

| | |
| --- | --- |
| **시각 있는 할 일의 위치** | The desktop timeline mixes them with events; the phone's Day screen keeps them in the to-do card. A layout difference that follows from the screen, with the same data behind it. |
| **키 매핑** | Tab ↔ tapping the other row · Enter ↔ "만들기" (the button and the keyboard's return key) · Esc ↔ × (the typed text stays either way). |

## Built differently from the drawing

| | |
| --- | --- |
| **리턴키 라벨** | §01 has the keyboard's return key read "만들기" while the bar is up. The key *does* create — the bar claims it on the way down — but its label is set by the OS from a field-level hint that cannot be changed mid-session, so it still reads "줄바꾸". The "만들기" button on the selected line is on screen either way, which is why the bar exists. |
| **리스트 색·Reminders 연결** | §03's long-press menu offers both. Neither exists: the schema has no colour column and the integration is §10. They are absent from the menu rather than greyed. |
