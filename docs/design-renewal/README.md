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
| `Daynote B Tasks - Events.dc.html` | To-dos and events on the desktop (docs/TODOS.md §7, §11) |
| `Daynote Mobile B Tasks - Events.dc.html` | The same on the phone |

## The two Tasks · Events files are incomplete

**Both are exactly 262,144 bytes — 256 KiB — and end in the middle of a tag.** That is the cap on
the tool that reads a file out of the design project, not the end of the document. They are
truncated imports, and anything past the cut is simply not here.

What survived, as of 2026-10-08:

- **`Daynote B Tasks - Events`** — the heading, §01 "@ 커맨드 팝업" in full (1a–1d: desktop light
  and dark, Korean and English, and all seven states), and §02 "생성 확인 = 객체가 나타남" up to
  the middle of its slide-in frames. The intro says four areas change; the sidebar's lists and the
  day panel are only visible inside the full-screen mocks, and whatever §03 and §04 would have
  said is past the cut.
- **`Daynote Mobile B Tasks - Events`** — the heading and §01 "@ 팝업 → 키보드 위에 붙는 바",
  Korean states ①–⑦ in full with the "정한 것" notes, and the English set cut during state ⑦. The
  intro promises answers to six phone-specific problems and a closing list of where the phone
  diverges from the desktop; **only the first of the six is here, and the divergence list is
  not.** The five that are missing are the ones that decide real structure: what plays the part of
  "the object appearing" when the to-do list is on another tab, how the Lists tab nests to-do lists
  under a segmented control that is already spoken for, where events live on a phone with no
  timeline, and how alarms are set.

To get the rest, each document has to come over as several files under the cap. Until then, do not
read the absence of a section as a decision not to have one.
