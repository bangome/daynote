# Daynote Design System

This file is the implementation contract for the native WPF surface. XAML must consume the named resources here. A new color, type size, spacing value, radius, border, target size, or duration is added here before use. Browser conventions are not implementation guidance for this project.

## 0. Research Log

- Embedded references: shortlisted Notion, Linear, and Cal. Picked `minimalist-skill.md` plus `notion.md`, with `layout-skill.md` for WPF app-shell mechanics, because warm paper neutrals, whisper borders, document-first hierarchy, calendar clarity, and keyboard rigor fit a dated note workspace. These sources provide grammar and tokens only. Their brands, logos, assets, and copy are not product content.
- Lazyweb: ran exactly four desktop queries: `daily notes calendar desktop app`, `clipboard history desktop app`, `knowledge workspace calendar notes`, and `desktop productivity search overlay`. The endpoint and `lazyweb_search` schema were reachable, but every valid call returned `Lazyweb MCP Pro is required to finish this request.` Result records: 0. Screens downloaded: 0. Screens viewed: 0. No layout grammar or product pattern was taken from this lane.
- Skipped lane: Lazyweb real-product screens only. The precise reason was the verified Pro entitlement failure above, not a network failure, schema failure, or agent choice. Retrying or purchasing entitlement was outside the authorized research scope.
- Imagen drafts: `C:\aegis_dx\reference\daynote\.omo\evidence\daynote-desktop-app\design-research\concepts\warm-paper.png`, `C:\aegis_dx\reference\daynote\.omo\evidence\daynote-desktop-app\design-research\concepts\focused-ink.png`, and `C:\aegis_dx\reference\daynote\.omo\evidence\daynote-desktop-app\design-research\concepts\quiet-date-ribbon.png`. All three are verified PNG files at 1586 by 992 pixels. Picked Warm Paper Workspace as the reference-fidelity contract because it gives the editor the strongest dominance, keeps calendar and clipboard duties distinct, and expresses the quietest warm-paper surface.
- Chosen draft verification: `warm-paper.png` has PNG signature `89504E470D0A1A0A`, dimensions 1586 by 992 pixels, and SHA-256 `c05863027ebe7c4ce4d0c85c8c83459207b668523a752f4e6e173db848ffc796`. It was directly inspected before this contract was written.
- Selective secondary findings: Focused Ink contributes only the clear focused-search and selected-result treatment. Quiet Date Ribbon contributes only the adjacent plain-language save-failure and retry pattern. Neither changes the steady-state shell.
- Revision 2026-07-20 (user-approved pivot): the Warm Paper reference-fidelity contract is superseded by a user-supplied desktop reference — a quiet list workspace with cool white neutrals, a left navigation sidebar holding list navigation above a bottom-docked mini month calendar, a dominant fast-entry note surface, and side content that stays hidden until summoned. Only structure and grammar were taken from that reference; its brand, mascot imagery, icons, task titles, and red overdue accents are not product content. The user approved three decisions explicitly: cool white palette, sidebar note list alongside editor note tabs, and the mini calendar docked at the sidebar bottom; the clipboard inbox becomes a collapsed-by-default drawer per the same instruction.

- Revision 2026-07-21 (UI polish + Korean product language): Korean is now the product language. All user-visible text and informative `AutomationProperties.Name` values render in natural Korean, centralized in `Daynote.App.Localization.AppStrings` and consumed from XAML via `x:Static` or from view models. Contract changes made in this pass: (1) the default note title becomes `노트 <n>` (was `Note <n>`) at its single Core source; (2) the selected-date header/status use Korean-native date order — long form `yyyy'년' M'월' d'일' dddd` (e.g. "2026년 7월 21일 화요일"), month `yyyy'년' M'월'`, and a new short form `yyyy-MM-dd` composed into the quiet bottom status line (selected date · capture state · save state); (3) the MarkdownEditor uses a borderless full-pane template `Daynote.Template.EditorSurface` with no resting, hover, or focus border — the caret indicates focus and the empty-state hint "오늘의 첫 기록을 시작하세요" shows only when the editor is empty and unfocused; (4) sidebar note-row reorder affordances (`ChevronUp`/`ChevronDown`) reveal only on row pointer hover or keyboard focus and stay focusable when shown; (5) note tabs are single bordered units with a bottom `Accent.500` selection marker over a `Border.Subtle` strip hairline, an on-hover/selected close glyph, and a `Status.Warning`/`Status.Error` dirty/error cue; (6) the editor toolbar is a plain `Border` row (no WPF `ToolBar` overflow chrome). New registered resources (defined in Section 4 tables and the icon registry above): `Daynote.Size.SearchBox.Width` (320), `Daynote.Inset.CommandRow` (`12,0`), `Daynote.Icon.Geometry.ChevronUp`, and `Daynote.Icon.Geometry.ChevronDown` (both converted without redrawing from Microsoft Fluent System Icons 20 Regular, consistent with the existing chevron family). The command-row SearchBox is fixed-width and left-aligned with a leading `Search` glyph and the decorative Korean watermark "노트·클립보드 검색" (its meaning carried by the SearchBox automation name). No new colors, type values, radii, or motion were introduced.

- Revision 2026-07-21 (systemic consistency pass): (1) The editor measure is now LEFT-ALIGNED, not centered — `MarkdownEditor` fills from the pane inset (`Daynote.Inset.Pane.Wide`, 24) with its content capped at `Daynote.Size.EditorMeasure.Max` (608) and no internal left padding, so typed text shares one left edge with the date header, note tabs, toolbar SaveStatus, and the empty-state hint. The Section 4 measure algorithm's "then is centered" clause is superseded by "then is left-aligned at the pane inset"; it still never introduces a horizontal scroll owner. (2) `PaneSplitter` at rest shows only a full-height `Border.Subtle` 1-DIP hairline (no floating thumb/pill); the 44-DIP transparent hit target is unchanged; hover/drag/keyboard-focus color the divider `Accent.500`/`Accent.700`/`Focus`. This supersedes the earlier "`Border.Control` visible divider at rest" wording for the resting appearance. (3) Type hierarchy: the mini-calendar month title uses the new quiet `Daynote.Style.CalendarMonthTitle` (14 DIP, SemiBold) instead of `PaneTitle` (22) so `DateTitle` (28) is the only large text; weekday headings stay `Status` 12 Muted and day numbers `Label` 14. (4) Secondary/icon controls adopt a single visual size: `Daynote.Template.IconButtonChrome` renders a 36-DIP (`Daynote.Size.Target.Secondary`) visual box centered inside the 44-DIP hit target for every ghost/secondary/destructive icon button (month chevrons, add-note, tab close, toolbar formatting, settings, reorder); 44-DIP primary size remains for calendar day cells and primary text buttons. Command-row controls (SearchBox, capture chip, clipboard toggle) and note tabs render at 36 visual height. (5) Alignment: `Daynote.Inset.CommandRow` is now `16,0` so the SearchBox left edge and the right-cluster inset match the sidebar's 16-DIP pane inset; sidebar rows and the Today shortcut drop their left content padding so all sidebar text (Today, note rows, calendar title, weekday grid) shares that 16 edge; the command row gains a full-width bottom `Border.Subtle` hairline and the mini calendar gains a top `Border.Subtle` hairline with `Space.4` separation. No new colors, type sizes, radii, or motion were introduced.

- Revision 2026-07-21 (product visual contract — Calendar Notes redesign): `.omo/design-reference/calendar-notes.dc.html` is now the **product** visual contract and supersedes Sections 1–5 layout and palette wherever they conflict; the prior sidebar/editor-tabs/collapsed-drawer composition and the Warm-Paper-derived cool-white palette apply only to the legacy Showcase host, which is unchanged. New token families are registered additively as `Daynote.Product.*` (so the Showcase's `Daynote.*` system is untouched): two swappable theme brush dictionaries `Daynote.Product.Light.xaml` / `Daynote.Product.Dark.xaml` (keys `…Brush.Bg1/Bg2/Panel/Card/Stroke/Text/Text2/Accent/AccentSoft/Hover/Input/OnAccent/Extras/CloseHover/Weekend.Sun/Weekend.Sat/Overdue` plus `…Effect.CardShadow/DropdownShadow`), and a theme-independent `Daynote.Product.Styles.xaml` (new type sizes 13 base / 12.5 / 12 / 11 / 10.5 / 10 / 9 metadata / 18 note title / 14 month / 13.5 mono editor; radii 8 card, 6 panel/control, 5, 4 chip; icon geometries recreated from the reference SVGs; and control styles for card/panel surfaces, ghost/caption/secondary/primary/dashed buttons, plain text inputs, and tag chips). Accent is light `#0067C0` / dark `#4CC2FF`; the todo-extras dot is `#E8A33D` and the titlebar close-hover is `#C42B1C`. Translucent design tokens keep their alpha and composite over the window gradient (WPF has no backdrop blur; this is the accepted approximation). Structural changes the product shell adopts: a custom 48-DIP `WindowChrome` titlebar (app glyph + "달력 노트", centered 480-max unified search, "오늘로 이동" + theme toggle + real min/max/close where close keeps hide-to-tray); floating card panels (radius 8, 1px stroke, soft shadow, 10 gap); a collapsible left column (calendar card with content dots/count badges fed by `INoteRepository.GetMonthContentSummaryAsync` + notes-list card) ; a center editor card (title + favorite + delete, tag chips with inline add, syntax-highlighted body over a synchronized overlay); a collapsible right tab panel (할 일 / 클립보드 / 파일) that replaces the clipboard drawer, with the todo tab parsing `-[]` items across dates via `INoteRepository.GetAllNotesAsync`; and an anchored dropdown search with VM-computed 날짜 (date) results that replaces the full-screen SearchOverlay. The note engine (`NoteWorkspaceViewModel`) is reused: its tabs are the day's note list, its selected tab is the editor note, favorites/tags flow through the existing `ToggleNoteFavorite`/`SetNoteTags` use cases and the autosave/flush/dirty/conflict pipeline is unchanged.

- Revision 2026-07-22 (post-it window): the editor title row adds an icon-only `포스트잇으로 열기` command immediately beside `즐겨찾기`. It opens a resizable, independently movable `WindowChrome` post-it that contains only the current note title and body; the content is a snapshot so it cannot bypass the normal editor/autosave flow. The post-it starts with `Topmost=True`, offers an explicit icon-only screen-top toggle and close command, and remains visible when the primary window hides to the tray. Product theme dictionaries add paired `Daynote.Product.Brush.Sticky` and `Daynote.Product.Brush.StickyStroke` resources for the paper surface and its boundary in both palettes; `Daynote.Product.Geo.StickyNote` and `.Pin` are the registered vector geometries. On explicit app shutdown, every post-it closes with the product window.

- Revision 2026-07-27 (bilingual product — Korean and English): the product ships in Korean and English, switchable at runtime. This supersedes the Korean-only clauses of Revision 2026-07-21. Contract changes: (1) `Daynote.App.Localization.AppStrings` no longer holds `const` copy — every member resolves through `LocalizationService` at call time, backed by the paired `KoreanStrings`/`EnglishStrings` catalogs, which a parity test holds key-for-key identical (including `{0}` placeholder sets); (2) XAML consumes copy through the `{loc:Tr Key}` markup extension instead of `{x:Static loc:AppStrings.Key}` — `x:Static` resolves once at load and would strand a loaded window in the old language, and a test now fails any markup that reintroduces it; (3) date presentation is catalog-driven, not hardcoded: `DateFormatLong`, `DateFormatMonth`, and `DateFormatDayHeading` are per-language .NET format patterns applied with the active culture (`ko-KR` / `en-US`), so English reads "Monday, July 27, 2026" rather than an English weekday inside the Korean 년/월/일 order; (4) the default note title is `노트 <n>` or `Note <n>`, still produced at its single Core source (`UntitledNote`), which the app layer points at the catalog — the title remains presentation-only and is never persisted; (5) Settings gains a **표시 언어 / Display language** row of `Daynote.Style.Segment` `RadioButton`s (a new segmented single-choice style over the shared button chrome, giving screen readers the SelectionItem pattern), persisting to `ui.language` as a `ko`/`en` tag; (6) first run with no persisted choice follows the Windows display language, Korean only for a Korean desktop and English otherwise. Switching is live — no restart: the service raises an indexer change that re-reads every `{loc:Tr}` binding, and view models holding derived text refresh through `ILanguageAware` (weakly registered, so transient view models do not leak). No new colors, type values, radii, or motion were introduced.

## 1. Atmosphere & Identity

Daynote is a quiet daily-notes desk inside a precise Windows shell; its signature material is cool white surfaces separated by whisper borders and slight tonal shifts, its color story is near-white and cool near-black with one scarce blue interaction ramp, and its memorable moment is the selected date updating first while the dominant editor settles into that scope with explicit status feedback and the clipboard drawer stays out of the way until summoned.

The workspace should feel calm, local, dependable, and immediately writable. The selected date is the persistent spine connecting calendar, note header, clipboard scope, and search recovery. Authored notes are the visual center. Captured material is a clearly separate inbox, never styled as authored content. Restraint is functional: stable regions and plain feedback reduce memory load without hiding capability.

### Product content inventory and jobs

| Content block | Job in the shell | Priority and behavior |
| --- | --- | --- |
| Window command region | Navigate and retain context | Keeps search, capture state, settings access, and current scope stable without becoming a second navigation system. |
| Sidebar navigation region | Navigate | Hosts the Today shortcut, the selected date's ordered note list, and a bottom-docked mini month calendar that selects the single local date scoping notes and clipboard items. Today and selection remain distinct. |
| Date header | Retain context | Repeats the selected date in plain language before the editable work area changes. |
| Note tab strip | Create and switch | Creates, names, reorders, selects, and closes stable notes for the current date. |
| Markdown editor | Create and edit | Dominant work surface for authored Markdown, dirty state, save progress, and recoverable failure. |
| Clipboard drawer | Capture and review | Collapsed by default in every layout state. Its labeled command-row toggle opens a drawer showing newest-first captured text or image items for the selected date, with copy and delete actions; closing restores the editor-dominant layout. |
| Search surface | Search and recover | Searches persisted note title/body and clipboard text, identifies source and date, then restores exact context. |
| Consent and settings | Consent and control | Explains local capture, keeps it off until explicit consent, and exposes pause, startup, storage, and privacy controls. |
| Status feedback | Retain confidence | Reports save, capture, loading, and failure states without revealing note or clipboard payloads or stealing focus. |
| Tray menu | Recover and control | Restores the window, pauses or resumes capture, opens settings, and quits explicitly. |

### Inclusive personas and task criteria

| Persona | Need | Contract pass condition |
| --- | --- | --- |
| Keyboard-first knowledge worker | Predictable pane order and shortcuts | Can select a date, switch or create a note, edit, inspect clipboard items, search, open a result, and recover prior focus without a pointer. |
| Low-vision and high-DPI user | Legible scale and stable targets | Can complete every primary task at 200% scaling and in High Contrast without clipped text, hidden primary actions, or horizontal scrolling of primary content. |
| Distractibility or memory-load user | Stable context and explicit recovery | Can always identify the selected date, save state, and capture state; destructive actions and recoverable failures state the consequence and next action. |
| Korean/CJK writer | Native glyphs, IME safety, and useful wrapping | Can compose Korean text without premature save/search, glyph fallback, baseline clipping, broken caret behavior, or inaccessible truncation. |

### Reference-fidelity contract

Revision 2026-07-20: the user-supplied quiet-list desktop reference replaces Warm Paper Workspace as the layout intent. Match its slim left sidebar with list navigation above a bottom-docked mini month calendar, its dominant immediately-writable main column, and its supporting content that stays hidden until summoned. Do not treat the reference bitmap as a source of literal XAML measurements or content.

Generated text, payloads, photographs, app marks, mascot imagery, icons, overflow glyphs, and shortcut labels are untrusted and must not be copied. Product labels come from approved Daynote behavior and plain-language content. Vector glyphs must be newly sourced from Windows-consistent geometry or drawn for Daynote, registered as resources, and given accessible names when informative.

Reference decisions are closed as follows:

| Reference element | Decision | WPF contract |
| --- | --- | --- |
| Slim left sidebar with list navigation | Take | Wide and Regular render a left sidebar at `Daynote.Size.Sidebar.Default` containing the Today shortcut, the selected date's ordered note list, and the bottom-docked mini month calendar. Compact reaches the same content through the Navigate workspace view. |
| Bottom-docked mini month calendar | Take | `CalendarDay` cells compose a month grid docked at the sidebar bottom; month paging, today, and selection cues follow Sections 4 and 5. The mini calendar remains fixed while the note list above it scrolls. |
| Dominant main content column | Adapt | The main column is the plain Markdown note editor with its date header, note tabs, and `EditorToolbar`; a note opens for editing in one activation from the sidebar note list or the tab strip. |
| Far-left icon-only rail | Leave | No second icon-only rail exists. Search, capture state, Settings, and the clipboard drawer toggle live in the client command row with accessible names. |
| Hidden-until-summoned side content | Take | The clipboard inbox is a drawer, collapsed by default in every layout state, opened and closed only through its labeled command-row toggle. |
| Formatting toolbar | Adapt | Retain the `EditorToolbar` primitive below the editor. Its Bold, Italic, bulleted-list, numbered-list, and inline-code commands insert or remove Markdown syntax in the plain text editor. It is not a rich-text toolbar. |
| Native title bar plus repeated app/title row | Leave | Use the native Windows title bar for the app name and window commands. The client command row contains no second Daynote label or app mark. |
| Hamburger and decorative overflow glyphs | Leave | No global hamburger exists. Note-tab overflow uses the registered `NoteTabOverflow` command only when tabs exceed the bounded strip. Clipboard actions are visible Copy and Delete commands. No unlabeled ellipsis is rendered. |
| Reference branding, task titles, dates, red overdue accents | Leave | None is copied into product resources, fixtures, or screenshots. Semantic status colors remain the registered Section 2 roles. Deterministic Daynote-owned test data replaces reference content. |

The later reference-fidelity visual QA set must contain fresh actual-WPF screenshots after the final UI edit for:

1. Wide default shell with the sidebar (Today shortcut, note list, mini calendar), dominant editor, and the clipboard drawer collapsed.
2. Wide shell with the clipboard drawer expanded to `Daynote.Size.ClipboardRail.Default`, and Regular shell with the drawer overlay open and closed.
3. Compact editor view, compact Navigate view, and compact clipboard view, each reached through labeled commands.
4. Search overlay with populated results, keyboard focus, source/date metadata, and exact-result selection.
5. Empty note projection, empty clipboard, and no-search-results states.
6. Note load, clipboard load, and search load states.
7. Save failure with Retry, clipboard failure, missing-image state, stale search result, and load failure.
8. First-run consent declined, capture enabled, capture paused, and settings states.
9. TrayMenu representation plus actual Windows notification-area menu in later OS-level QA.
10. Visible focus on calendar day, note tab, editor, clipboard action, search result, primary button, and safe dialog action.
11. Compact, regular, and wide primitive-showcase captures at 200% scaling.
12. High Contrast captures of the shell, focus, selection, errors, and disabled controls.
13. Korean IME composition and mixed Korean/Latin wrapping in editor, tabs, clipboard items, search results, and status text.
14. Long title, long paragraph, and unbroken-string stress in every pane without shell expansion or hidden actions.
15. A standard-palette Wide actual-WPF window capture in the default deterministic state for side-by-side region-by-region reviewer comparison against the 2026-07-20 user-supplied reference direction. Structural fidelity is judged by that review, not an automated image diff; the superseded `warm-paper.png` bitmap no longer defines fidelity, and no similarity score can substitute for the review.
16. Normal-motion interaction sequences for every animated primitive with rest, `Daynote.Motion.Evidence.Midpoint`, and settled frames, plus forced-reduced-motion sequences proving there is no intermediate opacity or transform state. Each sequence includes the initiating pointer and keyboard action, final state, focus owner, and scroll owner.

No gradients, glass, acrylic simulation, decorative imagery, emojis, copied brand identity, decorative brand color blocks, or ornamental motion are permitted. There is no hero, bento grid, trust strip, faux browser, or faux macOS chrome.

## 2. Color

### WPF color and brush resources

Every `Daynote.Color.*` entry is a `Color`; its paired `Daynote.Brush.*` entry is the only `SolidColorBrush` used by controls. Brushes are frozen when static. Opacity variants are not synthesized ad hoc. If a new semantic role is required, register both resources here first.

| Role | Color resource key | Brush resource key | Value | Usage |
| --- | --- | --- | --- | --- |
| Canvas | `Daynote.Color.Canvas` | `Daynote.Brush.Canvas` | `#FFF7F8FA` | Root window and calm open area. |
| Primary surface | `Daynote.Color.Surface.Primary` | `Daynote.Brush.Surface.Primary` | `#FFFFFFFF` | Editor, inputs, menus, and primary reading surface. |
| Secondary surface | `Daynote.Color.Surface.Secondary` | `Daynote.Brush.Surface.Secondary` | `#FFF2F4F7` | Sidebar, clipboard drawer backing, support regions, and grouped settings. |
| Hover surface | `Daynote.Color.Surface.Hover` | `Daynote.Brush.Surface.Hover` | `#FFECEFF3` | Pointer hover on neutral interactive rows. |
| Pressed surface | `Daynote.Color.Surface.Pressed` | `Daynote.Brush.Surface.Pressed` | `#FFE4E8EE` | Active or pressed neutral controls. |
| Selected surface | `Daynote.Color.Surface.Selected` | `Daynote.Brush.Surface.Selected` | `#FFEAF3FB` | Selected day, note, item, or result. |
| Disabled surface | `Daynote.Color.Surface.Disabled` | `Daynote.Brush.Surface.Disabled` | `#FFEFF1F5` | Unavailable controls where a fill is necessary. |
| Primary text | `Daynote.Color.Text.Primary` | `Daynote.Brush.Text.Primary` | `#FF21262E` | Headings, body, editor, and critical labels. |
| Secondary text | `Daynote.Color.Text.Secondary` | `Daynote.Brush.Text.Secondary` | `#FF575F6C` | Metadata and descriptions. |
| Muted text | `Daynote.Color.Text.Muted` | `Daynote.Brush.Text.Muted` | `#FF5D6470` | Input hints and noncritical 12-DIP status text on any declared surface. |
| Disabled text | `Daynote.Color.Text.Disabled` | `Daynote.Brush.Text.Disabled` | `#FF8E96A3` | Explicitly disabled labels only. |
| Subtle border | `Daynote.Color.Border.Subtle` | `Daynote.Brush.Border.Subtle` | `#FFE3E6EB` | Nonessential pane separators and row dividers only; never the sole cue for a control boundary, focus, selection, or state. |
| Strong border | `Daynote.Color.Border.Strong` | `Daynote.Brush.Border.Strong` | `#FFC8CED8` | Nonessential grouped-surface outline only; never the sole cue for a control boundary, focus, selection, or state. |
| Control boundary | `Daynote.Color.Border.Control` | `Daynote.Brush.Border.Control` | `#FF6C7583` | Essential input, outlined button, splitter, overlay, menu, and dialog boundary. |
| Accent 100 | `Daynote.Color.Accent.100` | `Daynote.Brush.Accent.100` | `#FFEAF3FB` | Quiet selected fill and focus-gap support. |
| Accent 500 | `Daynote.Color.Accent.500` | `Daynote.Brush.Accent.500` | `#FF0067C0` | Primary action, current scope, link, and selection marker. |
| Accent 600 | `Daynote.Color.Accent.600` | `Daynote.Brush.Accent.600` | `#FF005A9E` | Blue interactive hover. |
| Accent 700 | `Daynote.Color.Accent.700` | `Daynote.Brush.Accent.700` | `#FF004B83` | Blue interactive active or pressed state. |
| Text on accent | `Daynote.Color.Text.OnAccent` | `Daynote.Brush.Text.OnAccent` | `#FFFFFFFF` | Text and informative icons on Accent 500, 600, or 700. |
| Focus | `Daynote.Color.Focus` | `Daynote.Brush.Focus` | `#FF005FB8` | Keyboard focus ring. |
| Focus gap | `Daynote.Color.Focus.Gap` | `Daynote.Brush.Focus.Gap` | `#FFFFFFFF` | Inner or outer separation that keeps focus visible. |
| Success text | `Daynote.Color.Status.Success` | `Daynote.Brush.Status.Success` | `#FF2F6F3E` | Saved or capture-enabled confirmation. |
| Success surface | `Daynote.Color.Status.Success.Surface` | `Daynote.Brush.Status.Success.Surface` | `#FFEDF3EC` | Success StatusBanner backing. |
| Warning text | `Daynote.Color.Status.Warning` | `Daynote.Brush.Status.Warning` | `#FF8A5A00` | Recoverable caution. |
| Warning surface | `Daynote.Color.Status.Warning.Surface` | `Daynote.Brush.Status.Warning.Surface` | `#FFFBF3DB` | Warning StatusBanner backing. |
| Error text | `Daynote.Color.Status.Error` | `Daynote.Brush.Status.Error` | `#FFB42318` | Validation, load, capture, or save failure. |
| Error surface | `Daynote.Color.Status.Error.Surface` | `Daynote.Brush.Status.Error.Surface` | `#FFFDEBEC` | Error StatusBanner backing. |
| Transparent | `Daynote.Color.Transparent` | `Daynote.Brush.Transparent` | `#00FFFFFF` | Hit testing and unfilled controls where transparent is intentional. |

### High Contrast resource mapping

When `SystemParameters.HighContrast` is true, the application-level theme dictionary replaces fixed brushes. No control may locally force the standard palette.

| Daynote role | Windows system brush mapping |
| --- | --- |
| Canvas and primary surface | `SystemColors.WindowBrushKey` |
| Secondary, hover, pressed, selected, and disabled surfaces | `SystemColors.ControlBrushKey` or `SystemColors.HighlightBrushKey` when selected |
| Primary and secondary text | `SystemColors.WindowTextBrushKey` |
| Muted and disabled text | `SystemColors.GrayTextBrushKey` |
| Subtle, strong, control, overlay, menu, dialog, and splitter borders | `SystemColors.ControlTextBrushKey` |
| Accent, focus, and selected marker | `SystemColors.HighlightBrushKey` |
| Text on selected or accent fill | `SystemColors.HighlightTextBrushKey` |
| Status text and surfaces | System window/control brushes plus text, icon, and pattern cues; semantic meaning never depends on hue |

### Color rules

- The blue `Accent.100/500/600/700` sequence is the only interaction ramp. Blue is not decoration.
- Cool white surfaces and cool near-black text carry the identity. Warm cream or beige neutrals are out of contract.
- Success, warning, and error brushes communicate semantic state only and always include plain text plus a non-color cue.
- Selection and keyboard focus are separate. Selection uses `Daynote.Brush.Surface.Selected`; focus uses `Daynote.Brush.Focus` with `Daynote.Brush.Focus.Gap`.
- Every essential outlined control boundary uses `Daynote.Brush.Border.Control`. A Primary Button uses its verified Accent fill edge as the boundary. A Ghost Button is permitted only inside a persistent labeled command group or toolbar and has no resting outline; its text or informative icon remains visible at rest and its hover, pressed, and focus states remain explicit. `Border.Subtle` and `Border.Strong` may separate surfaces only when shape, spacing, heading, or another persistent cue already establishes the region.
- Text or informative icons on the Accent 500, 600, and 700 fills use `Daynote.Brush.Text.OnAccent` only.
- Disabled content must remain identifiable and must not be conveyed by opacity alone.
- No raw color literal may appear in product XAML, code-behind, converters, or tests. Extend this table first.

### Deterministic contrast verification

Ratios use WCAG relative luminance with sRGB linearization and are rounded to two decimals for display. The implementation gate recomputes them from the ARGB values above.

| Required pair | Verified ratios | Result |
| --- | --- | --- |
| `Text.Muted` on Canvas, Primary, Secondary, Hover, Pressed, Selected, Disabled, Success Surface, Warning Surface, Error Surface | 5.61, 5.96, 5.41, 5.17, 4.85, 5.31, 5.27, 5.29, 5.38, 5.19 | Passes 4.5:1 on every declared surface; Pressed is the minimum at 4.85:1. |
| `Border.Control` on the same surface sequence | 4.38, 4.66, 4.23, 4.04, 3.79, 4.15, 4.12, 4.13, 4.20, 4.05 | Passes 3:1 on every declared surface; Pressed is the minimum at 3.79:1. |
| `Focus` on the same surface sequence | 5.94, 6.31, 5.73, 5.47, 5.13, 5.62, 5.58, 5.60, 5.69, 5.49 | Passes 3:1 on every declared surface; Pressed is the minimum at 5.13:1. |
| Accent 500, 600, and 700 filled-control boundaries on every declared surface | Minimums 4.61, 5.78, 7.32 | Each accent fill passes 3:1 as the persistent boundary of a Primary Button. |
| `Text.OnAccent` on Accent 500, 600, and 700 | 5.67, 7.10, 9.00 | Passes 4.5:1 for normal text on every accent action state. |

## 3. Typography

### WPF font-family resources

| Resource key | Value | Usage |
| --- | --- | --- |
| `Daynote.FontFamily.UI` | `Segoe UI Variable Text, Segoe UI, Malgun Gothic` | Body, editor, controls, metadata, Korean/CJK content. |
| `Daynote.FontFamily.Display` | `Segoe UI Variable Display, Segoe UI, Malgun Gothic` | Date and top-level pane heading only. |
| `Daynote.FontFamily.Mono` | `Cascadia Mono, Consolas, Malgun Gothic` | Keystroke hints and technical snippets only. |

The fallback chain is a WPF resource contract, not a web font stack. If runtime fallback resolution is inconsistent, supply a WPF composite font that lists the same families and script ranges. Do not package an identity font merely to mimic a web reference. Do not apply negative tracking to Korean or mixed-script text.

### Type resources

All values are WPF device-independent pixels. Each role is implemented through keyed `FontSize`, `FontWeight`, and `LineHeight` resources, grouped below for readability. `TextOptions.TextFormattingMode` is `Display` for UI labels and `Ideal` for editor reading text. `TextOptions.TextRenderingMode` follows system defaults.

| Role | Resource-key prefix | Size | Line height | WPF `FontWeight` | Usage |
| --- | --- | ---: | ---: | --- | --- |
| Date title | `Daynote.Type.DateTitle` | 28 | 36 | `FontWeights.Bold` (700) | Selected-date identity and rare top-level empty heading. |
| Pane title | `Daynote.Type.PaneTitle` | 22 | 28 | `FontWeights.SemiBold` (600) | Calendar, clipboard, settings, and overlay headings. |
| Item title | `Daynote.Type.ItemTitle` | 18 | 24 | `FontWeights.SemiBold` (600) | Note or result title with enough room for mixed script. |
| Body | `Daynote.Type.Body` | 16 | 24 | `FontWeights.Regular` (400) | Editor, body text, consent explanation, and primary labels. |
| UI label | `Daynote.Type.Label` | 14 | 20 | `FontWeights.Regular` (400) | Buttons, tabs, calendar cells, list rows, and metadata. |
| Status | `Daynote.Type.Status` | 12 | 16 | `FontWeights.SemiBold` (600) | Short status, badge, and keystroke text only. |

Each prefix expands to `.FontSize`, `.LineHeight`, and `.FontWeight`, for example `Daynote.Type.Body.FontSize`. Each `.FontWeight` resource is a WPF `FontWeight` using the exact `FontWeights.*` value in the table. Font family is assigned separately through `Daynote.FontFamily.*`.

### Typography rules

- Body and editor content use `Daynote.Type.Body`; routine interactive labels use `Daynote.Type.Label`. Status text is never the only place a critical instruction appears.
- The release uses the UI and display families only. Mono is allowed solely where the content is genuinely a keystroke or technical fragment.
- `LineStackingStrategy="BlockLineHeight"` may be used only after Korean glyph clipping is verified. Otherwise use natural Windows line layout while preserving the role's minimum line height.
- Note titles and search snippets expose their full accessible value when visual truncation is necessary.
- Wrapping is intentional: pane titles wrap at most to two lines; body and errors wrap freely; tabs trim visually but expose full names to UI Automation and tooltip.
- Font size never shrinks to preserve panes. Layout collapses secondary regions first.
- No raw `FontSize`, `FontWeight`, `LineHeight`, `FontFamily`, or character-spacing value appears outside the resource definitions derived from this section.

## 4. Spacing & Layout

### Base unit and spacing resources

The base unit is 4 WPF device-independent pixels.

| Resource key | Value | Intent |
| --- | ---: | --- |
| `Daynote.Space.1` | 4 | Icon gap and tight internal separation. |
| `Daynote.Space.2` | 8 | Compact row rhythm. |
| `Daynote.Space.3` | 12 | Standard control inset. |
| `Daynote.Space.4` | 16 | Compact pane inset and common group gap. |
| `Daynote.Space.5` | 20 | Regular pane inset. |
| `Daynote.Space.6` | 24 | Wide pane inset and generous local gap. |
| `Daynote.Space.8` | 32 | Major local separation. |
| `Daynote.Space.10` | 40 | Sparse empty-state separation. |
| `Daynote.Space.12` | 48 | Maximum shell-level spacing. |

Use keyed `Thickness` resources derived from this scale for repeated insets, for example `Daynote.Inset.Control` uses vertical `Daynote.Space.2` and horizontal `Daynote.Space.3`, while `Daynote.Inset.Pane.Compact`, `.Regular`, and `.Wide` use `Daynote.Space.4`, `.5`, and `.6`. A one-off `Thickness` is not permitted merely because each side uses an approved number. Register its semantic key first.

| Thickness resource key | WPF value | Usage |
| --- | --- | --- |
| `Daynote.Inset.Control` | `12,8` | Button, input, tab, menu item, and compact interactive row content. |
| `Daynote.Inset.Pane.Compact` | `16` | Compact pane content. |
| `Daynote.Inset.Pane.Regular` | `20` | Regular pane content. |
| `Daynote.Inset.Pane.Wide` | `24` | Wide pane content. |
| `Daynote.Inset.Calendar.Cues` | `0,0,0,4` | Bottom-centered CalendarDay cue cluster. |
| `Daynote.Inset.CommandRow` | `12,0` | Horizontal-only command-row padding so 44-DIP targets center inside the 48-DIP row without clipping (Revision 2026-07-21). |

### Geometry, target, and motion-distance resources

| Resource key | Value | Usage |
| --- | ---: | --- |
| `Daynote.Border.Thin` | 1 | Whisper separator and resting outline. |
| `Daynote.Border.Focus` | 2 | Visible focus ring. |
| `Daynote.Border.FocusGap` | 1 | Separation between focus and adjacent fill. |
| `Daynote.Radius.None` | 0 | Explicit square corner for region panels that must not read as cards, such as the inline clipboard drawer rail. |
| `Daynote.Radius.Control` | 4 | Buttons, inputs, calendar selection, and tabs. |
| `Daynote.Radius.Panel` | 6 | Menus, overlay, consent, and compact grouped surfaces. |
| `Daynote.Radius.Dialog` | 8 | Dialogs and the largest bounded surface. |
| `Daynote.Size.Target.Primary` | 44 | Primary click/touch target and icon-button box. |
| `Daynote.Size.Target.Secondary` | 36 | Dense secondary visual box with a centered `Daynote.Size.Target.Primary` hit area and full keyboard path. |
| `Daynote.Size.CommandRow` | 48 | Stable command/header row. |
| `Daynote.Size.SearchBox.Width` | 320 | Fixed comfortable command-row SearchBox width so it does not span the row (Revision 2026-07-21). |
| `Daynote.Size.Sidebar.Min` | 300 | Minimum sidebar width. |
| `Daynote.Size.Sidebar.Default` | 340 | Initial sidebar width: seven 44-DIP mini-calendar day targets (308 DIP) plus the 16-DIP Compact pane inset on both sides. |
| `Daynote.Size.Sidebar.Max` | 380 | Maximum sidebar width while preserving the editor minimum at `Daynote.Layout.RegularMin`. |
| `Daynote.Size.ClipboardRail.Min` | 300 | Minimum expanded clipboard drawer width. |
| `Daynote.Size.ClipboardRail.Default` | 320 | Initial expanded clipboard drawer width in Wide, and the fixed Regular drawer overlay width. |
| `Daynote.Size.ClipboardRail.Max` | 340 | Maximum expanded clipboard drawer width in Wide. |
| `Daynote.Size.Splitter.Visual` | 4 | Visible PaneSplitter divider centered inside its larger interaction target. |
| `Daynote.Size.Splitter.HitTarget` | 44 | Transparent centered PaneSplitter pointer and keyboard target. |
| `Daynote.Size.NoteTab.TitleMax` | 320 | Maximum visual width of a note-tab title before its registered wrapping or trimming rule applies. |
| `Daynote.Size.NoteTab.TitleMaxHeight` | 40 | Exactly two `Label` lines for the additive long-title stress state. |
| `Daynote.Size.StatusText.MaxHeight` | 32 | Exactly two `Status` lines inside a fixed status or toolbar row. |
| `Daynote.Size.Editor.Min` | 480 | Practical regular/wide editor minimum. |
| `Daynote.Size.Window.MinWidth` | 760 | Minimum supported window width before OS chrome. |
| `Daynote.Size.Window.MinHeight` | 600 | Minimum supported window height before OS chrome. |
| `Daynote.Size.EditorMeasure.PreferredMin` | 544 | Preferred lower readable editor content measure when available in WPF DIPs. |
| `Daynote.Size.EditorMeasure.Max` | 608 | Maximum readable editor content measure in WPF DIPs. |
| `Daynote.Size.Calendar.TodayMarker` | 6 | Today cue ellipse diameter. |
| `Daynote.Size.Calendar.ContentMarker` | 4 | Has-note filled-circle and has-clipboard hollow-square cue bounds. |
| `Daynote.Size.Calendar.CueGap` | 4 | Gap within the bottom-centered CalendarDay cue cluster. |
| `Daynote.Size.PaneTitle.MaxHeight` | 56 | Two `PaneTitle` lines at the registered line height. |
| `Daynote.Size.ClipboardPreview.MaxHeight` | 80 | Four `Label` lines for a clipboard text preview. |
| `Daynote.Size.SearchSnippet.MaxHeight` | 60 | Three `Label` lines for a search-result snippet. |
| `Daynote.Icon.Size.Small` | 16 | Inline status, compact command, and menu glyph box. |
| `Daynote.Icon.Size.Standard` | 20 | Standard IconButton and toolbar glyph box. |
| `Daynote.Icon.Size.Large` | 24 | Dialog or empty-state informative glyph box. |
| `Daynote.Icon.Stroke` | 0 | Registered Fluent source paths are fill geometry; the shared presenter sets no added stroke. |
| `Daynote.Border.SelectionMarker` | 2 | Persistent selected tab, result, and current-scope marker. |
| `Daynote.Motion.Offset.Subtle` | 4 | Search or pane orientation shift. |
| `Daynote.Motion.Offset.Standard` | 8 | Maximum allowed orientation shift. |

WPF `BorderThickness` dependency properties consume typed `Thickness` values rather than the registered scalar border values. `Daynote.Thickness.Border.Thin`, `Daynote.Thickness.Border.Focus`, `Daynote.Thickness.Border.FocusGap`, and `Daynote.Thickness.SelectionMarker.Left` (left-only `Daynote.Border.SelectionMarker` for the sidebar note-row marker) are required typed adapters derived exactly from their same-suffix `Daynote.Border.*` scalar resources. They introduce no new visual values and are used only where a `Thickness` dependency property requires them; numeric layout, stroke, marker, and evidence code continues to consume the scalar resources.

| Layout resource key | WPF double value | Usage |
| --- | ---: | --- |
| `Daynote.Layout.CompactMax` | 819 | Highest Compact effective content width. |
| `Daynote.Layout.RegularMin` | 820 | Lowest Regular effective content width. |
| `Daynote.Layout.RegularMax` | 1199 | Highest Regular effective content width. |
| `Daynote.Layout.WideMin` | 1200 | Lowest Wide effective content width. |
| `Daynote.Layout.Hysteresis` | 8 | Required threshold-crossing distance before changing an established layout state. |

Every product icon is a frozen `StreamGeometry` resource converted without redrawing from the exact filled path of the matching Microsoft Fluent System Icons Regular vector. The first release registers exactly these keys: `Daynote.Icon.Geometry.Search`, `Daynote.Icon.Geometry.Dismiss`, `Daynote.Icon.Geometry.Settings`, `Daynote.Icon.Geometry.Calendar`, `Daynote.Icon.Geometry.Notes`, `Daynote.Icon.Geometry.Clipboard`, `Daynote.Icon.Geometry.Add`, `Daynote.Icon.Geometry.Close`, `Daynote.Icon.Geometry.Copy`, `Daynote.Icon.Geometry.Delete`, `Daynote.Icon.Geometry.Bold`, `Daynote.Icon.Geometry.Italic`, `Daynote.Icon.Geometry.BulletedList`, `Daynote.Icon.Geometry.NumberedList`, `Daynote.Icon.Geometry.InlineCode`, `Daynote.Icon.Geometry.ChevronLeft`, `Daynote.Icon.Geometry.ChevronRight`, `Daynote.Icon.Geometry.ChevronUp`, `Daynote.Icon.Geometry.ChevronDown`, `Daynote.Icon.Geometry.Retry`, `Daynote.Icon.Geometry.Checkmark`, `Daynote.Icon.Geometry.Info`, `Daynote.Icon.Geometry.Warning`, `Daynote.Icon.Geometry.Error`, `Daynote.Icon.Geometry.Capture`, `Daynote.Icon.Geometry.Pause`, `Daynote.Icon.Geometry.Resume`, `Daynote.Icon.Geometry.ShowWindow`, `Daynote.Icon.Geometry.Quit`, `Daynote.Icon.Geometry.TextItem`, and `Daynote.Icon.Geometry.ImageItem`. `NoteTabOverflow` is a visible text command and has no glyph. Any additional icon requires a named key in this paragraph before implementation. Path data is normalized to the `Daynote.Icon.Size.Standard` view box. The shared icon presenter sets the registered size, `Stretch="Uniform"`, `Fill` to the owning text brush, `Stroke="{x:Null}"`, and `StrokeThickness` to `Daynote.Icon.Stroke`; product templates cannot override those properties. Informative icons remain in UI Automation through the owning control name; decorative icons are excluded.

`CalendarDay` renders its cue cluster in a bottom-centered horizontal panel using `Daynote.Inset.Calendar.Cues` and `Daynote.Size.Calendar.CueGap`. Today is a filled ellipse with `Daynote.Size.Calendar.TodayMarker`. Has-note is a filled circle and has-clipboard is a hollow square, each using `Daynote.Size.Calendar.ContentMarker` and `Daynote.Border.Thin`. On an unselected cell cues use `Daynote.Brush.Accent.500`; on a selected cell they use `Daynote.Brush.Text.OnAccent`. The distinct shapes remain when color is unavailable.

### Bounded shell and ownership

`AppShell` owns the root window. It is a bounded WPF `Grid` with command, workspace, and status rows. The root window never owns document scroll and is never wrapped in a root `ScrollViewer`. The workspace row and every pane content row use star sizing so scrollable children receive finite bounds. Do not place a scrollable pane inside a vertical `StackPanel`, because infinite measurement breaks scroll ownership.

Named vertical scroll owners:

- Sidebar region: the note-list body owns its pane scroll; the Today shortcut header and the bottom-docked mini month calendar remain fixed.
- Editor region: `MarkdownEditor` owns document scroll; the date header, note tabs, and save status remain outside it.
- Clipboard drawer: when expanded, the clipboard item list owns its pane scroll; drawer heading, capture state, and primary drawer actions remain fixed.
- Search overlay: the result list owns overlay scroll; query, result count, and scope remain fixed.
- Settings and consent: their bounded body may own one scroll only when the minimum supported window cannot fit content. Actions remain fixed.

No list is wrapped in a second `ScrollViewer`. A nested scroll owner is allowed only when its separate job is named in this file and later verified with wheel, touchpad, keyboard, and screen reader input.

`MarkdownEditor` owns readable measure. It computes `availableContent` as the editor region width minus the left and right values of the active pane inset. The content presenter width is the lesser of `availableContent` and `Daynote.Size.EditorMeasure.Max`, then is centered. When `availableContent` is below `Daynote.Size.EditorMeasure.PreferredMin` in Compact or narrow Regular, the presenter uses all `availableContent` and wraps. When it is at or above the preferred minimum, the presenter grows only to `.Max`. The algorithm never introduces a horizontal scroll owner to preserve a preferred measure.

### Content-driven resize states

The states describe available content width, not phones, tablets, monitors, or device brands. State selection uses the effective WPF content width after system scaling and window chrome. `AppShellLayoutState` reads the application-level `Daynote.Layout.*` double resources above; no duplicate view-model constants exist.

| State and key | Effective width | Layout contract |
| --- | ---: | --- |
| Compact, `Daynote.Layout.CompactMax` | below 820 | One bounded content column. `WorkspaceViewSwitch` mounts exactly one of Navigate, Notes, and Clipboard; Notes is the initial view. Navigate mounts the sidebar content (Today shortcut, note list, mini calendar). No horizontal scroll of primary content. |
| Regular, `Daynote.Layout.RegularMin` and `Daynote.Layout.RegularMax` | 820 to 1199 | Two columns: the sidebar initially at `Daynote.Size.Sidebar.Default` (resizable only between `Daynote.Size.Sidebar.Min` and `Daynote.Size.Sidebar.Max`), one `Daynote.Size.Splitter.Visual` divider, and a fluid editor at not less than `Daynote.Size.Editor.Min`. The clipboard drawer is collapsed by default; its command-row toggle mounts a bounded right overlay panel at the fixed `Daynote.Size.ClipboardRail.Default` width that closes through the toggle or Escape without moving editor scroll, caret, or the underlying columns. |
| Wide, `Daynote.Layout.WideMin` | 1200 and above | Sidebar initially at `Daynote.Size.Sidebar.Default` with the same resize bounds, one `Daynote.Size.Splitter.Visual` divider, and a fluid editor. The clipboard drawer is collapsed by default; its toggle mounts an inline right rail initially at `Daynote.Size.ClipboardRail.Default` with a second splitter bounded by `Daynote.Size.ClipboardRail.Min` and `Daynote.Size.ClipboardRail.Max`. The sidebar/editor separator is `Daynote.Border.Thin`. All remaining width always belongs to the editor region; its content presenter caps and centers through the registered measure algorithm. |

`AppShellLayoutState` is the single owner that applies Compact, Regular, and Wide through `VisualStateManager`. It uses the exact thresholds above and retains the current state until effective width crosses the next threshold by `Daynote.Layout.Hysteresis`, preventing oscillation. Pane visibility changes only after autosave-safe navigation logic permits it.

Regular and Wide mount the sidebar and editor for the entire state. The clipboard drawer starts collapsed after launch and after every date change; it never opens on its own. Opening the drawer moves focus to the drawer heading; closing it restores focus to the drawer toggle. The editor width and measure reflow in Wide when the inline rail mounts, but editor scroll offset, caret, and dirty state are unchanged; in Regular the overlay panel covers without reflowing the columns beneath. The drawer retains its own vertical scroll offset while it stays open within one selected date. Drawer focus fallback is the first clipboard item when the list is nonempty and the drawer heading when it is empty.

`PaneSplitter` consumes only `Daynote.Size.Splitter.Visual` in the grid. A transparent `Daynote.Size.Splitter.HitTarget` overlay is centered on that divider without changing column measurement. It uses the SizeWE cursor, and Left/Right adjust the owning rail by `Daynote.Space.2`. The sidebar splitter exists in Regular and Wide: Home sets `Daynote.Size.Sidebar.Min` and End sets `Daynote.Size.Sidebar.Max`. The drawer splitter exists only in Wide while the drawer is expanded: Home sets `Daynote.Size.ClipboardRail.Min` and End sets `Daynote.Size.ClipboardRail.Max`. Keyboard focus draws the standard focus adorner around the full hit target. At minimum or maximum, the unavailable direction is announced and the current width remains unchanged.

Compact mounts only the selected workspace view and preserves a separate scroll offset and remembered focus element for Navigate, Notes, and Clipboard. `WorkspaceViewSwitch` remains fixed in the command region and never scrolls. Switching away from Notes first completes autosave-safe navigation. Pointer activation leaves focus on the selected switch item. Arrow-key selection also leaves focus on that item. `F6` moves into the selected view at its remembered valid element. Navigate fallback is the selected date in the mini calendar; Notes fallback is the editor caret; Clipboard fallback is the first item when nonempty and the pane heading when empty. If a remembered element no longer exists, focus falls back to the selected switch item and is never dropped to the window root.

At 200% scaling, WPF device-independent sizes and system text metrics remain authoritative. The shell recalculates effective content width and may enter Regular or Compact. The sidebar and the clipboard drawer collapse before type or targets shrink. The selected date, editor, primary create/edit actions, save failure recovery, and capture state remain reachable. There is no application-wide zoom transform, bitmap scaling of text, or fixed-pixel screenshot layout.

Pane titles use `TextWrapping="Wrap"`, no trimming, and `Daynote.Size.PaneTitle.MaxHeight`; content exceeding that bound is a contract failure. Note tabs normally use `TextWrapping="NoWrap"` and `TextTrimming="CharacterEllipsis"`; the additive long-title stress state instead uses `TextWrapping="Wrap"`, no trimming, and `Daynote.Size.NoteTab.TitleMaxHeight` for exactly two lines. Both variants expose the full accessible name. Clipboard previews use `TextWrapping="WrapWithOverflow"`, `TextTrimming="CharacterEllipsis"`, and `Daynote.Size.ClipboardPreview.MaxHeight`. Search snippets use the same behavior with `Daynote.Size.SearchSnippet.MaxHeight`. Editor paragraphs and unbroken URLs use `TextWrapping="WrapWithOverflow"` inside the bounded readable measure. These owner-specific rules never widen the shell. Empty, loading, and error states occupy the same bounded region as their content to prevent focus and layout jumps.

## 5. Components

Only the primitives scheduled for the first WPF build are defined here. Product screens compose these primitives and may not introduce an undocumented reusable control.

### Shared state-to-resource recipes

These tables are template requirements. Brush names inside recipe cells omit only the `Daynote.Brush.` prefix; radius, border, icon, and size names omit only the `Daynote.` prefix. Every expanded key is defined in Sections 2 through 4. Product templates do not choose substitute brushes, borders, marker thicknesses, icon sizes, or state geometry.

| Button variant | Default fill / text / border | Hover fill / text / border | Pressed fill / text / border | Disabled fill / text / border |
| --- | --- | --- | --- | --- |
| Primary | `Accent.500` / `Text.OnAccent` / `Accent.500` | `Accent.600` / `Text.OnAccent` / `Accent.600` | `Accent.700` / `Text.OnAccent` / `Accent.700` | `Surface.Disabled` / `Text.Disabled` / `Border.Control` |
| Secondary | `Surface.Primary` / `Text.Primary` / `Border.Control` | `Surface.Hover` / `Text.Primary` / `Border.Control` | `Surface.Pressed` / `Text.Primary` / `Border.Control` | `Surface.Disabled` / `Text.Disabled` / `Border.Control` |
| Ghost | `Transparent` / `Text.Primary` / `Transparent` | `Surface.Hover` / `Text.Primary` / `Transparent` | `Surface.Pressed` / `Text.Primary` / `Transparent` | `Transparent` / `Text.Disabled` / `Transparent` |
| Destructive | `Surface.Primary` / `Status.Error` / `Border.Control` | `Status.Error.Surface` / `Status.Error` / `Border.Control` | `Surface.Pressed` / `Status.Error` / `Border.Control` | `Surface.Disabled` / `Text.Disabled` / `Border.Control` |

`IconButton` inherits the matching Secondary, Ghost, or Destructive recipe. Primary IconButton is prohibited because a primary action requires a text label. Every Button focus state adds an outer `Daynote.Border.Focus` ring in `Daynote.Brush.Focus`, separated from the control by `Daynote.Border.FocusGap` in `Daynote.Brush.Focus.Gap`; focus never replaces the variant border. Busy retains the normal recipe, disables duplicate invocation, and adds a `Daynote.Icon.Size.Small` system progress cue after the label. Disabled state uses no opacity reduction.

| Primitive or part | Resting recipe | Interactive or persistent recipe | Exceptional recipe |
| --- | --- | --- | --- |
| AppShell | Canvas `Canvas`; editor `Surface.Primary`; support rails `Surface.Secondary`; noninteractive dividers `Border.Subtle` | Current region uses the shared focus adorner on its first focusable descendant | Startup and load failure use StatusBanner recipes |
| CalendarDay | `Transparent` / `Text.Primary`; outside-month uses `Text.Muted` | Hover `Surface.Hover`; pressed `Surface.Pressed`; selected `Accent.500` / `Text.OnAccent`; cues use Section 4 geometry | Disabled uses `Surface.Disabled` / `Text.Disabled`; focus uses shared focus adorner |
| DateHeader | `Surface.Primary` / `Text.Primary`; status uses `Text.Muted` | Today cue uses the Calendar today-marker resource beside the text | Load/error text uses matching StatusBanner recipe without changing heading geometry |
| WorkspaceViewSwitch | Secondary Button recipe for unselected items | Selected uses `Surface.Selected` / `Text.Primary` plus bottom `Accent.500` marker at `Border.SelectionMarker`; hover and pressed use Secondary Button recipes | Disabled uses Secondary Button disabled recipe; focus uses shared focus adorner |
| SidebarNoteList | Rows `Transparent` / `Text.Primary` on the sidebar `Surface.Secondary`; metadata uses `Text.Muted` | Hover `Surface.Hover`; pressed `Surface.Pressed`; selected uses `Surface.Selected` plus left `Accent.500` marker at `Border.SelectionMarker` | Dirty uses `Status.Warning` shape plus text; save error uses `Status.Error` shape plus text |
| ClipboardDrawer | Collapsed toggle uses Secondary Button recipe; expanded panel `Surface.Secondary` with inline `Border.Subtle` separator | Regular overlay adds `Border.Control` boundary with `Radius.Panel`; toggle exposes expanded state | Drawer failure content uses StatusBanner recipes |
| PaneSplitter | `Border.Control` visible divider inside `Transparent` hit target | Hover and focus divider use `Accent.500`; pressed uses `Accent.700`; focus uses shared focus adorner | Disabled divider uses `Border.Strong` and remains visible |
| NoteTab | `Transparent` / `Text.Primary`; close command uses Ghost IconButton | Selected uses `Surface.Primary` plus bottom `Accent.500` marker at `Border.SelectionMarker`; hover and pressed use neutral surfaces | Dirty uses `Status.Warning` shape plus text state; save error uses `Status.Error` shape plus text state |
| MarkdownEditor | `Surface.Primary` / `Text.Primary`; caret and selection follow Windows text-control system behavior | Focus uses shared focus adorner without adding a resting card border | Read-only uses `Surface.Disabled` / `Text.Disabled`; save failure is adjacent StatusBanner, not an editor fill |
| EditorToolbar | `Surface.Primary` with top `Border.Subtle`; commands use Ghost Button recipe | SaveStatus uses `Status.Success`, `Status.Warning`, or `Status.Error` text plus registered semantic icon | Busy formatting command uses the shared Button busy recipe |
| StickyNoteWindow | `Daynote.Product.Brush.Sticky` / `Daynote.Product.Brush.Text` / `Daynote.Product.Brush.StickyStroke`, `Daynote.Product.Radius.Card`, and the registered card shadow | Header is the movable `WindowChrome` caption region; screen-top and close commands use the Product Ghost IconButton recipe, with the active topmost icon using `Daynote.Product.Brush.Accent` | The window has no loading, empty, or error state: it displays the supplied title/body snapshot or closes |
| ClipboardItem | `Surface.Primary` / `Text.Primary` with nonessential `Border.Subtle`; metadata uses `Text.Muted` | Hover `Surface.Hover`; pressed `Surface.Pressed`; selected uses `Surface.Selected` plus `Accent.500` marker at `Border.SelectionMarker` | Missing image uses Error pattern; pending action uses disabled Button recipe without fading payload |
| SearchBox | `Surface.Primary` / `Text.Primary` / `Border.Control`; hint uses `Text.Muted` | Hover retains boundary and uses `Surface.Hover`; active input returns `Surface.Primary`; focus uses shared focus adorner | Error retains `Border.Control` and adds adjacent Error pattern |
| SearchOverlay | `Surface.Primary` / `Text.Primary` / `Border.Control` with `Radius.Panel` | SearchResult hover uses `Surface.Hover`; pressed uses `Surface.Pressed`; selected uses `Surface.Selected` plus `Accent.500` marker at `Border.SelectionMarker` | Stale and error results use Error pattern and keep source/date text visible |
| StatusBanner | Info `Accent.100` / `Accent.700`; success, warning, and error use their registered semantic surface/text pairs | Contained action uses the required Button recipe | Busy keeps message text and adds `Icon.Size.Small` system progress cue |
| ConsentPanel | `Surface.Primary` / `Text.Primary` / `Border.Control` with `Radius.Panel` | Contained controls use Button recipes | Policy and initialization failures use StatusBanner recipes |
| SettingsRow | `Surface.Secondary` / `Text.Primary`; description uses `Text.Muted`; nonessential row divider uses `Border.Subtle` | Contained controls use Button recipes and `Border.Control` | OS-policy-disabled value uses `Text.Disabled` plus explicit policy text |
| TrayMenu representation | `Surface.Primary` / `Text.Primary` / `Border.Control` with `Radius.Panel` | Item hover `Surface.Hover`; pressed `Surface.Pressed`; checked state uses text plus `Accent.500` check geometry | Guarded command uses disabled Button recipe; failure uses adjacent StatusBanner |
| Empty / Loading / Error | Empty uses owning surface, `Text.Primary`, and `Text.Muted`; Loading uses owning surface and system progress cue | Recovery action uses required Button recipe | Error uses `Status.Error.Surface` / `Status.Error` plus `Border.Control` when bounded |

Semantic geometry is fixed: info uses `Daynote.Icon.Geometry.Info`; saved, success, and copied confirmation use `Daynote.Icon.Geometry.Checkmark`; warning and dirty save use `Daynote.Icon.Geometry.Warning`; failure uses `Daynote.Icon.Geometry.Error`; capture-enabled uses `Daynote.Icon.Geometry.Capture`; capture-paused uses `Daynote.Icon.Geometry.Pause`; text clipboard source uses `Daynote.Icon.Geometry.TextItem`; image clipboard source uses `Daynote.Icon.Geometry.ImageItem`. Busy and loading use the native WPF progress indicator and no semantic icon. `EditorToolbar` SaveStatus maps dirty to Warning, saving to native progress only, saved to Checkmark, and save failure to Error. `StatusBanner` maps info, success, warning, error, capture-enabled, capture-paused, and busy through the same list. Empty has no icon; Error pattern uses Error; Retry Button uses `Daynote.Icon.Geometry.Retry` before its visible label.

High Contrast replaces every brush in these recipes through Section 2 mappings while preserving borders, marker thickness, shapes, labels, and focus geometry. Loading cues use native WPF progress geometry; custom spinners, opacity-only states, and unregistered adorners are prohibited.

### AppShell

- **Structure**: root `Window` content grid with command row, bounded workspace row, and status row; workspace composes the sidebar navigation region (Today shortcut, note list, bottom-docked mini calendar), the dominant note region, and the collapsed-by-default clipboard drawer.
- **Variants**: Compact, Regular, Wide; drawer collapsed and expanded; standard palette and High Contrast.
- **Spacing**: `Daynote.Inset.Pane.Compact`, `.Regular`, `.Wide`; `Daynote.Space.4`; `Daynote.Border.Thin`; `Daynote.Size.Splitter.Visual`; `Daynote.Size.Splitter.HitTarget`; shell sizes from Section 4.
- **States**: default, loading initial workspace, empty first-use shell, recoverable startup error, disabled transition while a save flush is pending.
- **Accessibility**: logical order is command region, sidebar (note list then mini calendar), note tabs, editor, clipboard drawer when expanded, status. `F6` and `Shift+F6` cycle mounted regions only. Each region has a UI Automation name and landmark-like group heading. Selection never substitutes for focus.
- **Motion**: layout-state changes are immediate. The scoped date handoff may use `Daynote.Motion.Scope` from Section 6 after the header updates. Reduced motion is immediate.
- **Layout and scroll owner**: AppShell bounds the window but never scrolls. It applies the exact Compact, Regular, and Wide compositions and focus restoration from Section 4. Each mounted child pane owns only the named scroll. Editor receives remaining width and remains visually dominant.

### WorkspaceViewSwitch

- **Structure**: a single-selection, tab-semantics strip of visible text Buttons. Compact labels are Navigate, Notes, and Clipboard in that order. The strip exists only in Compact; Regular and Wide reach the same content through the sidebar and the clipboard drawer toggle. No icon-only or overflow representation exists.
- **Variants**: Compact three-view strip; standard palette and High Contrast.
- **Spacing**: `Daynote.Size.CommandRow`, `Daynote.Inset.Control`, `Daynote.Space.1`, `Daynote.Space.2`, `Daynote.Radius.Control`, `Daynote.Border.SelectionMarker`, and `Daynote.Type.Label`.
- **States**: default, hover, active/pressed, focus, selected, disabled during autosave-safe navigation, and guarded-transition error through adjacent StatusBanner. Loading and empty belong to the selected view, not the switch.
- **Accessibility**: exposes Tab and TabItem semantics, selected state, position, and set size. Left/Right move and activate; Home/End select first/last. Focus remains on the selected item after direct selection. `F6` enters the mounted view according to Section 4 focus memory.
- **Motion**: selection and view replacement are immediate. Dependent content may use AppShell scope opacity only after context text updates. Reduced motion remains immediate.
- **Layout and scroll owner**: fixed in the command region and never scrolls. It mounts exactly the views defined for the active layout state and delegates scroll to the selected view.

### SidebarNoteList

- **Structure**: the sidebar's navigation body — a Today shortcut row, then the selected date's ordered note rows (title plus optional dirty or save-state cue), then an add-note command row. The bottom-docked mini month calendar sits below it as a fixed sibling, not inside the list.
- **Variants**: unpersisted Note 1 projection row, persisted rows, selected row, dirty row, save-error row; standard palette and High Contrast.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.1`, `Daynote.Space.2`, `Daynote.Radius.Control`, `Daynote.Border.SelectionMarker`, `Daynote.Size.Target.Primary`, and `Daynote.Type.Label`.
- **States**: default, hover, active/pressed, focus, disabled during autosave-safe navigation, loading notes, empty projection, and save error. The selected row mirrors the selected note tab; selection never substitutes for focus.
- **Accessibility**: list semantics with position and set size; Up/Down move rows, Enter/Space opens the note for editing and moves focus to the editor caret; the row name is the full note title even when trimmed. Selection stays synchronized with the tab strip in both directions.
- **Motion**: selection marker may use `Daynote.Motion.Micro` opacity only. Row insertion and removal are immediate.
- **Layout and scroll owner**: the note-list body is the sidebar's only vertical scroll owner; the Today shortcut and mini calendar remain fixed.

### ClipboardDrawer

- **Structure**: a labeled command-row toggle plus a bounded panel hosting the clipboard heading, capture state, and the clipboard item list. Wide expands it as an inline right rail with a drawer splitter; Regular expands it as a bounded right overlay panel; Compact reaches the same content through the Clipboard workspace view instead of the drawer.
- **Variants**: collapsed, expanded inline (Wide), expanded overlay (Regular); standard palette and High Contrast.
- **Spacing**: `Daynote.Size.ClipboardRail.Min/.Default/.Max`, `Daynote.Inset.Pane.Regular`, `Daynote.Space.2`, `Daynote.Radius.Panel` for the overlay, `Daynote.Border.Control` for the overlay boundary, and `Daynote.Border.Subtle` for the inline rail separator.
- **States**: collapsed, expanded, focus on toggle or drawer content, disabled while a guarded transition is pending, loading items, empty pane pattern, and error. The drawer never opens on its own, including after capture events.
- **Accessibility**: the toggle exposes its expanded/collapsed state; opening moves focus to the drawer heading, closing restores focus to the toggle; Escape closes the Regular overlay. Capture continues to be announced through the command-row capture state, not by auto-opening the drawer.
- **Motion**: expansion and dismissal use `Daynote.Motion.Panel` opacity plus `Daynote.Motion.Offset.Subtle`; reduced motion is immediate. The inline Wide reflow itself is immediate.
- **Layout and scroll owner**: only the hosted clipboard item list scrolls; heading, capture state, and actions remain fixed. The overlay never changes focus order behind it.

### PaneSplitter

- **Structure**: a `Daynote.Size.Splitter.Visual` divider with a centered transparent `Daynote.Size.Splitter.HitTarget` interaction overlay, SizeWE cursor, keyboard focus adorner, and current rail width exposed to UI Automation.
- **Variants**: sidebar/editor splitter (Regular and Wide) and editor/drawer splitter (Wide with the drawer expanded); standard palette and High Contrast.
- **Spacing**: `Daynote.Size.Splitter.Visual` (4-DIP divider), `Daynote.Size.Splitter.HitTarget` (44 by 44 target), `Daynote.Space.2`, `Daynote.Border.Focus`, and `Daynote.Border.FocusGap`.
- **States**: default, hover, active/dragging, focus, disabled while layout is guarded, minimum, and maximum. Loading, empty, and error are not splitter states.
- **Accessibility**: exposes Separator with RangeValue semantics, the current width, minimum, and maximum. Left/Right, Home, and End use the exact Section 4 behavior. Pointer capture releases on completion or Escape, and Escape restores the pre-drag width and focus.
- **Motion**: no animation. Width changes track input immediately and never use a storyboard.
- **Layout and scroll owner**: overlay does not consume grid width and never scrolls. It resizes only the right support rail; editor and pane scroll offsets remain unchanged.

### CalendarDay

- **Structure**: button-derived day cell with day number, optional today cue, optional has-content cue, and AutomationProperties for full date and state.
- **Variants**: normal, outside-month, today, selected, today-selected, has-note, has-clipboard, unavailable.
- **Spacing**: `Daynote.Size.Target.Primary`, `Daynote.Space.1`, `Daynote.Radius.Control`, `Daynote.Border.Thin`, `Daynote.Border.Focus`.
- **States**: default, hover, active/pressed, focus, disabled, loading calendar scope, and error on date-load failure. Selected is persistent and independent of focus.
- **Accessibility**: Left/Right move one local day, Up/Down move seven local days, Page Up/Down change one month while retaining the day when valid, Home moves to today, and Enter/Space selects. Full local date and today/content cues are announced. Cues are not color-only.
- **Motion**: no movement. Brush changes are immediate; scope handoff belongs to AppShell.
- **Layout and scroll owner**: cell never scrolls. Calendar collection is the region scroll owner if needed.

### DateHeader

- **Structure**: selected local date text, optional today label, and concise context/status text.
- **Variants**: regular date, today, historical date, loading, and unavailable date.
- **Spacing**: `Daynote.Space.2`, `Daynote.Space.3`, `Daynote.Type.DateTitle`, `Daynote.Type.Status`.
- **States**: default, loading, empty projection context, and error. It is display content, so hover, active, focus, and disabled are not applicable unless a future command is separately expressed as a Button.
- **Accessibility**: heading semantics through AutomationProperties, full locale-aware date, and polite announcement when the selected date changes. It never steals focus.
- **Motion**: text updates first, then optional `Daynote.Motion.Scope` opacity handoff for dependent panes. Reduced motion updates immediately.
- **Layout and scroll owner**: fixed above note content and never scrolls with the editor.

### NoteTabStrip / NoteTab

- **Structure**: ordered tab collection, each NoteTab containing title and separate close command; add command follows tabs; rename and reorder use explicit commands.
- **Variants**: unpersisted Note 1 projection, persisted note, dirty, saving, saved, selected, and save-error.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.1`, `Daynote.Space.2`, `Daynote.Size.Target.Primary`, `Daynote.Size.NoteTab.TitleMax`, `Daynote.Size.NoteTab.TitleMaxHeight`, `Daynote.Radius.Control`, and `Daynote.Type.Label`.
- **States**: default, hover, active/pressed, focus, disabled during guarded transition, loading note, empty projection, and save error. Selection remains visible without focus.
- **Accessibility**: standard tab semantics, Left/Right navigation, Home/End, Delete only with explicit safe handling, and full title exposed when trimmed. Close is named with the note title. Reorder announces the new position.
- **Motion**: selection marker may use `Daynote.Motion.Micro` opacity only. Reorder is immediate; no sliding layout animation.
- **Layout and scroll owner**: fixed above editor. The strip remains one row and clips tab titles within their bounded items. When all tabs do not fit, the registered text command `NoteTabOverflow` is the final fixed item and opens a bounded menu of every hidden note in document order. The strip never scrolls and never becomes the editor's vertical scroll owner.

### MarkdownEditor

- **Structure**: plain multiline note-body editing surface, caret, selection, and save-state association. Note title rename belongs to `NoteTab`; no second title input, rich-text renderer, or WebView is mounted in the editor.
- **Variants**: unpersisted projection, editable persisted note, dirty, saving, saved, read-only transition, and recoverable save failure.
- **Spacing**: `Daynote.Inset.Pane.Compact`, `.Regular`, `.Wide`; `Daynote.Type.Body`; editor measure resources; `Daynote.Border.Thin`; `Daynote.Border.Focus`.
- **States**: default, hover is not visually required for the document body, active editing, focus, disabled/read-only, loading, empty, and error. IME composition is a distinct transient editing state and must not trigger premature save or search.
- **Accessibility**: useful name includes selected date and note title; supports standard Windows text patterns, caret and selection reporting, Ctrl+S flush, undo/redo conventions, IME, and screen-reader reading. Save announcements are polite and retain caret/focus.
- **Motion**: editor content itself does not animate. Date-scope replacement may use AppShell's opacity handoff only after save-safe navigation succeeds.
- **Layout and scroll owner**: dominant fluid region and sole vertical owner for note document scrolling. No outer editor ScrollViewer.

### EditorToolbar

- **Structure**: fixed row with `SaveStatus` text and registered semantic icon on the left; Bold, Italic, bulleted-list, numbered-list, and inline-code Buttons on the right. Each command edits Markdown syntax in `MarkdownEditor`, preserves selection direction and caret, participates in undo, and is unavailable during read-only or save-guarded states.
- **Variants**: default editing, dirty, saving, saved, recoverable save failure, and read-only. Compact retains the same commands in the same order with visible text labels available through the adjacent command name and UI Automation; it does not collapse into an overflow menu.
- **Spacing**: `Daynote.Size.CommandRow`, `Daynote.Inset.Control`, `Daynote.Space.1`, `Daynote.Space.2`, `Daynote.Border.Thin`, `Daynote.Icon.Size.Standard`, `Daynote.Icon.Stroke`, `Daynote.Type.Label`, and `Daynote.Type.Status`.
- **States**: toolbar default; command hover, active/pressed, focus, disabled, and busy; save dirty, saving, saved, and error. Empty is not applicable because SaveStatus always has a state.
- **Accessibility**: logical order is SaveStatus, Bold, Italic, bulleted list, numbered list, inline code. Each formatting command uses its registered icon, an unambiguous UI Automation name, tooltip, and access key at every scale. Save changes are politely announced without moving editor focus. Formatting state is announced when the current selection has matching Markdown syntax.
- **Motion**: save confirmation may use `Daynote.Motion.Micro` opacity. Formatting commands change brushes immediately and never move. Reduced motion uses `Daynote.Motion.Instant`.
- **Layout and scroll owner**: fixed below `MarkdownEditor` and outside document scroll. It stays visible in all layout states and never owns scroll.

### StickyNoteWindow

- **Structure**: borderless, resizable WPF window with a `WindowChrome` caption header, one title TextBlock, one scrollable body TextBlock, and icon-only screen-top and close commands. It is opened only from a persisted selected note's editor header.
- **Variants**: topmost (default) and not-topmost; standard light and dark palettes.
- **Spacing**: `Daynote.Product.Radius.Card`, existing Product title/editor type roles, and the compact 30-DIP Product GhostButton controls used by the editor header.
- **States**: default, topmost, hovering/focusing either header command, and body overflow. It has no editable, loading, empty, or error state; the normal note editor remains the only editing surface.
- **Accessibility**: the title is exposed as the window's name; screen-top and close commands each have a Korean tooltip and AutomationProperties.Name that changes with the topmost state. The title retains its full accessible value when visually trimmed, and the body wraps and scrolls without creating horizontal scroll.
- **Motion**: no decorative animation. Moving and resizing follow native Windows pointer feedback; topmost changes immediately.
- **Layout and scroll owner**: the window's body ScrollViewer is the only scroll owner. The header remains fixed, and `WindowChrome.ResizeBorderThickness` exposes native edge/corner resizing at every supported size.

### ClipboardItem

- **Structure**: source-kind cue using the fixed TextItem or ImageItem semantic geometry, timestamp, bounded text preview or image preview, explicit Copy and Delete commands, and unavailable-image message when needed.
- **Variants**: text, image, selected, copied confirmation, delete confirmation, unavailable image, and capture-disabled context.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.2`, `Daynote.Space.3`, `Daynote.Radius.Control`, `Daynote.Border.Thin`, and `Daynote.Type.Label`.
- **States**: default, hover, active/pressed, focus, disabled while an item action is pending, loading preview, empty pane handled by Empty pattern, and error/unavailable.
- **Accessibility**: list-item semantics; kind, date/time, and action names are exposed without announcing full private payload by default. Copy and Delete have keyboard paths. Delete requires explicit consequence and safe initial focus.
- **Motion**: copy confirmation uses `Daynote.Motion.Micro` opacity. Item removal is immediate after confirmed success; no collapsing-height animation.
- **Layout and scroll owner**: item does not own scroll. The clipboard list is the pane's only vertical scroll owner; header and capture state stay fixed.

### SearchBox / SearchOverlay / SearchResult

- **Structure**: SearchBox query input with clear command and shortcut hint; SearchOverlay bounded layer with fixed query/status header and scrollable result list; SearchResult with source, local date, title, and bounded snippet.
- **Variants**: idle, query present, searching, populated, selected result, no results, stale result, and search error.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.1`, `Daynote.Space.2`, `Daynote.Space.3`, `Daynote.Radius.Control`, `Daynote.Radius.Panel`, `Daynote.Border.Thin`, `Daynote.Border.Focus`, and type roles from Section 3.
- **States**: SearchBox supports default, hover, active input, focus, disabled, and error. SearchOverlay supports loading, empty, populated, and error. SearchResult supports default, hover, active/pressed, focus, disabled stale state, loading context row, and error/stale state.
- **Accessibility**: Ctrl+F focuses SearchBox. Escape clears first, then closes and restores the invoking control. Up/Down moves results, Enter opens exact source, and result count is politely announced. Literal query contents are never copied into global diagnostics.
- **Motion**: overlay entry/exit uses `Daynote.Motion.Panel` with opacity and `Daynote.Motion.Offset.Subtle`. Reduced motion is immediate. No blur or scaling.
- **Layout and scroll owner**: overlay is bounded within AppShell without changing focus order behind it. Only its result list scrolls; query and state header remain fixed.

### Button / IconButton

- **Structure**: Button contains text and optional registered vector icon; IconButton contains one registered vector icon plus AutomationProperties.Name and tooltip. Busy controls retain their label and add a progress cue.
- **Variants**: primary, secondary, ghost, destructive; IconButton is allowed only for familiar, reversible commands. Destructive and unfamiliar commands require visible text.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.1`, `Daynote.Size.Target.Primary`, `Daynote.Radius.Control`, `Daynote.Border.Thin`, `Daynote.Border.Focus`, and `Daynote.Type.Label`.
- **States**: default, hover, active/pressed, focus, disabled, and loading/busy. Empty and error are not control states; errors are expressed through StatusBanner or Error pattern.
- **Accessibility**: Enter/Space activation, access key where appropriate, visible focus, unique name, and no hover-only information. Busy state prevents duplicate activation and announces progress without replacing the command name.
- **Motion**: active feedback uses the exact brush change in the shared recipe. Busy progress follows the native WPF progress indicator and Windows motion preference; it is never an endless decorative pulse.
- **Layout and scroll owner**: never owns scroll. Primary target is `Daynote.Size.Target.Primary`; dense secondary placement preserves both the centered primary-size hit area and full keyboard path.

### StatusBanner

- **Structure**: fixed semantic geometry from the shared mapping, plain-language message, optional recovery Button, and dismiss action only when dismissal is safe.
- **Variants**: info, success/saved, warning, error, capture-enabled, capture-paused, and busy.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.2`, `Daynote.Space.3`, `Daynote.Radius.Control`, `Daynote.Border.Thin`, `Daynote.Type.Status`, and status brushes from Section 2.
- **States**: default, focus on recovery action, disabled recovery while busy, loading/busy, and error. Hover/active apply only to contained Button controls. Empty means the banner is not mounted.
- **Accessibility**: message and state are announced politely unless immediate action is required. Focus moves only for blocking action. Text never includes note or clipboard payload. Severity is conveyed with text and shape as well as color.
- **Motion**: `Daynote.Motion.Micro` opacity on nonblocking appearance. Blocking failure appears immediately. Reduced motion is immediate.
- **Layout and scroll owner**: fixed within its owning pane or shell status row and never owns scroll. It reserves predictable space where repeated status changes would otherwise jump content.

### ConsentPanel

- **Structure**: heading, plain explanation of future-only local capture, supported formats, storage/privacy facts, primary consent Button, decline Button, and settings link.
- **Variants**: first run, declined, consented confirmation, policy unavailable, and storage initialization error.
- **Spacing**: `Daynote.Inset.Pane.Regular`, `Daynote.Space.3`, `Daynote.Space.4`, `Daynote.Space.6`, `Daynote.Radius.Panel`, and body/title type roles.
- **States**: default, focus, disabled while applying choice, loading, and error. Hover/active belong to contained controls. Empty is not applicable.
- **Accessibility**: clear heading order, no preselected consent, safe predictable tab order, explicit consequences, and screen-reader names. Capture remains off until successful explicit consent.
- **Motion**: panel appearance uses `Daynote.Motion.Panel` opacity only; reduced motion is immediate.
- **Layout and scroll owner**: bounded content region. If it cannot fit at minimum height, its explanation body may scroll while decision actions remain fixed.

### SettingsRow

- **Structure**: label, optional description, current value or Toggle/Button, and optional inline status.
- **Variants**: toggle, command, read-only value, OS-policy-controlled value, and destructive command.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.2`, `Daynote.Space.3`, `Daynote.Space.4`, `Daynote.Border.Thin`, and label/body/status type roles.
- **States**: default, hover, active/pressed on its control, focus, disabled by OS policy, loading, and error. Empty value is represented as explicit unavailable text, never a blank gap.
- **Accessibility**: label is programmatically associated with its control; description does not replace the name; current state, policy restriction, and errors are announced. The whole row is not clickable when that would create ambiguous focus.
- **Motion**: no row movement. Toggle state uses `Daynote.Motion.Micro` only if Windows motion settings permit.
- **Layout and scroll owner**: row never scrolls. Settings body may own one bounded scroll with heading and actions fixed.

### TrayMenu representation

- **Structure**: deterministic in-window showcase representation of the notification-area menu with Show Daynote, Pause/Resume capture, Settings, separator, and Quit. Production uses the Windows notification-area menu with equivalent order and state.
- **Variants**: window shown, window hidden, capture enabled, capture paused, busy quit, and quit failure.
- **Spacing**: `Daynote.Inset.Control`, `Daynote.Space.1`, `Daynote.Space.2`, `Daynote.Radius.Panel`, `Daynote.Border.Thin`, `Daynote.Size.Target.Primary`, and `Daynote.Type.Label`.
- **States**: default, hover, active/pressed, focus in showcase and keyboard-capable system menu, disabled during guarded operations, loading/busy, and error through adjacent StatusBanner. Empty is not applicable.
- **Accessibility**: text labels for every command, state in the Pause/Resume label, access keys where Windows menu conventions support them, and Quit remains explicit. The representation is not evidence for actual tray behavior; later OS-level QA must exercise the real menu.
- **Motion**: follows native system menu behavior. The showcase adds no custom motion.
- **Layout and scroll owner**: menu never scrolls at supported content size. It is outside AppShell in production; the showcase representation is bounded and isolated.

### Empty / Loading / Error patterns

- **Structure**: Empty has specific heading, one explanatory sentence, and optional relevant Button; Loading has stable skeleton-free status text and progress cue; Error has plain message, effect, and recovery action where available.
- **Variants**: first note, no clipboard items, no search results, note loading, clipboard loading, search loading, save failure, load failure, missing image, stale result, startup failure, and policy restriction.
- **Spacing**: `Daynote.Space.2`, `Daynote.Space.3`, `Daynote.Space.4`, `Daynote.Space.8`, `Daynote.Space.10`, pane insets, title/body/status type roles, and `Daynote.Radius.Control` when a bounded banner is required.
- **States**: Empty, Loading, and Error are explicit states. Hover, active, focus, and disabled apply only to contained Button controls. Loading preserves the eventual region's bounds. Error persists until resolved or safely dismissed.
- **Accessibility**: concise Korean-ready copy, no marketing language, no decorative illustration requirement, polite loading completion, assertive announcement only for blocking failure, safe initial focus, and payload-free details.
- **Motion**: Loading uses no decorative shimmer. A determinate or system progress indicator follows Windows reduced-motion settings. Empty and Error appear without spatial animation.
- **Layout and scroll owner**: patterns occupy their eventual content region and do not create a new scroll owner. Long error details wrap within the existing pane.

### Primitive Showcase Gate

Product-screen composition is blocked until a deterministic WPF showcase renders every primitive above against the real resource dictionaries. The showcase must permit programmatic state forcing so hover, active, focus, disabled, loading, empty, and error evidence does not depend on timing or pointer luck.

State applicability matrix:

| Primitive | Default | Hover | Active | Focus | Disabled | Loading | Empty | Error |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| AppShell | Required | N/A | N/A | Region order | Transition guard | Required | Required | Required |
| WorkspaceViewSwitch | Required | Required | Required | Required | Required | Selected-view load | N/A | Adjacent banner |
| SidebarNoteList | Required | Required | Required | Required | Required | Required | Projection | Required |
| ClipboardDrawer | Required | Toggle | Toggle | Required | Required | Item load | Pane pattern | Required |
| PaneSplitter | Required | Required | Required | Required | Required | N/A | N/A | N/A |
| CalendarDay | Required | Required | Required | Required | Required | Scope load | N/A | Date load |
| DateHeader | Required | N/A | N/A | N/A | N/A | Required | Projection context | Required |
| NoteTabStrip / NoteTab | Required | Required | Required | Required | Required | Required | Projection | Required |
| MarkdownEditor | Required | N/A | Editing | Required | Read-only | Required | Required | Required |
| EditorToolbar | Required | Required commands | Required commands | Required commands | Required commands | Save busy | N/A | Save failure |
| StickyNoteWindow | Required | Header commands | Header commands | Header commands | N/A | N/A | N/A | N/A |
| ClipboardItem | Required | Required | Required | Required | Required | Required | Pane pattern | Required |
| SearchBox / SearchOverlay / SearchResult | Required | Required | Required | Required | Required | Required | Required | Required |
| Button / IconButton | Required | Required | Required | Required | Required | Busy | N/A | Via pattern |
| StatusBanner | Required | Contained action | Contained action | Recovery action | Recovery action | Busy | Unmounted | Required |
| ConsentPanel | Required | Contained action | Contained action | Required | Applying/policy | Required | N/A | Required |
| SettingsRow | Required | Required | Required | Required | Required | Required | Unavailable value | Required |
| TrayMenu representation | Required | Required | Required | Required | Required | Busy quit | N/A | Adjacent banner |
| Empty / Loading / Error patterns | N/A | Contained action | Contained action | Recovery action | Contained action | Required | Required | Required |

Every Required or applicable cell above is exercised at Compact, Regular, and Wide. The following stress matrix is additive, not a substitute:

| Showcase dimension | Required evidence |
| --- | --- |
| 200% scaling | All three layout states or their effective-width fallback, with readable text, visible editor and recovery actions, and no clipped primary target. |
| High Contrast | System brush replacement, visible focus separate from selection, semantic state with text/shape cues, and legible disabled controls. |
| Korean IME/CJK | Active composition, caret, candidate interaction, commit/cancel, fallback glyphs, line height, and no premature autosave/search. |
| Long text | Two-line title behavior, wrapped plain-language error, full accessible value, and stable actions. |
| Unbroken text | Bounded URL/token in editor, clipboard preview, and search snippet with no shell widening or primary horizontal scroll. |
| Keyboard | Tab order, arrow/tab semantics, F6 region cycling, Ctrl+F, Escape restoration, Enter/Space, safe dialog focus, and no focus loss after status changes. |
| Screen reader | Name, role/control type, value/state, selection versus focus, live announcements, and payload-free global errors. |
| Scroll ownership | Wheel, touchpad, keyboard, and screen-reader navigation move only the named pane owner while fixed headers remain fixed. |
| Normal motion | Programmatically force each animated primitive and capture rest, `Daynote.Motion.Evidence.Midpoint`, and settled frames plus pointer and keyboard interaction logs. Evidence names the final state, focus owner, and scroll owner. |
| Forced reduced motion | Showcase override forces client-area animation off for every primitive. Capture the same interactions and prove `Daynote.Motion.Instant`, no intermediate opacity or transform, unchanged focus order, and identical settled state. |

The gate passes only after fresh actual-WPF screenshots, motion frame sequences, and pointer, keyboard, focus, and scroll interaction logs show the complete matrix. Each capture records build identity, source modification time, WPF render size, Windows scaling, palette, motion preference, state, and input path. This document defines the gate; it does not claim that the showcase has run.

## 6. Motion & Interaction

### WPF motion resources

| Resource key | Duration | Easing | Usage |
| --- | ---: | --- | --- |
| `Daynote.Motion.Instant` | 0 ms | none | Reduced motion and layout-state changes. |
| `Daynote.Motion.Micro` | 120 ms | `CubicEase` EaseOut | Focused state cue, save/copy confirmation opacity. |
| `Daynote.Motion.Standard` | 180 ms | `CubicEase` EaseInOut | Search overlay or secondary pane orientation. |
| `Daynote.Motion.Scope` | 200 ms | `CubicEase` EaseInOut | Date-context crossfade after header update. |
| `Daynote.Motion.Panel` | 180 ms | `CubicEase` EaseOut | Overlay/consent opacity plus approved transform offset. |
| `Daynote.Motion.Evidence.Midpoint` | 100 ms | none | Deterministic normal-motion capture instant; evidence-only and never used by a production storyboard. |

### Interaction rules

- Animate only `Opacity` and `TranslateTransform`. Never animate Grid length, width, height, margin, padding, border thickness, font size, scroll offset, or content measurement.
- Brush changes for hover, active, selection, validation, and focus are immediate state changes, not color animations.
- Motion must explain scope change, overlay relationship, or completion. There is no scroll-entry reveal, staggered list mounting, ambient movement, hover lift, scale pulse, shimmer, or decorative loop.
- The date header updates before dependent pane crossfade so context is never visually ambiguous.
- Windows animation preferences control motion. When client-area animation is disabled, use `Daynote.Motion.Instant` and preserve every text, focus, and state cue.
- `DaynoteMotionPolicy` is the single motion-duration owner. Production reads `SystemParameters.ClientAreaAnimation`; false maps every storyboard duration to `Daynote.Motion.Instant`. The primitive showcase and interaction tests may inject the policy's explicit reduced-motion value to force false without changing OS settings. The override is unavailable in production composition.
- Each animated interaction produces rest, `Daynote.Motion.Evidence.Midpoint`, and settled frames in normal motion. The corresponding forced-reduced-motion interaction produces rest and settled frames with no render at an intermediate opacity or translate offset. Both paths record the initiating pointer and keyboard input, focus before and after, and the named scroll owner.
- Hover is supplemental. Every pointer action has a keyboard path and persistent focus state.
- Active/pressed controls use `Daynote.Brush.Surface.Pressed` or `Daynote.Brush.Accent.700`; they do not shrink.
- Autosave and async actions expose dirty, busy, success, and failure states. Busy prevents duplicate activation but does not discard the command label.
- Focus never moves for routine success. Blocking errors move focus only to the owning error region or safe recovery action and restore it after resolution.
- Search debounce and autosave debounce are behavior timings defined by the product plan, not visual motion tokens, and must not be reused as animation durations.

## 7. Depth & Surface

### Strategy: borders plus tonal shift

Daynote commits to a borders/tonal-shift strategy. No shadow system exists in the first release. The canvas, support panes, editor, overlay, menu representation, and rows separate through registered surface brushes, `Daynote.Border.Thin`, and whitespace. A region does not become a card merely because it contains content.

| Depth level | Resources | Usage |
| --- | --- | --- |
| Canvas | `Daynote.Brush.Canvas` | Root bounded window. |
| Primary plane | `Daynote.Brush.Surface.Primary` | Editor, overlay body, inputs, and menus. |
| Support plane | `Daynote.Brush.Surface.Secondary` | Sidebar, clipboard drawer backing, settings groups. |
| Interaction plane | Hover, Pressed, Selected surface brushes | Temporary hover/active and persistent selection. |
| Separation | `Daynote.Brush.Border.Subtle` plus `Daynote.Border.Thin` | Nonessential pane boundaries and list dividers. |
| Essential boundary | `Daynote.Brush.Border.Control` plus `Daynote.Border.Thin` | Inputs, outlined buttons, splitter, bounded overlays, menus, and dialogs. Primary Button uses its verified Accent fill edge; Ghost Button is restricted by Section 2. |
| Focus | Focus and FocusGap brushes plus focus thickness resources | Keyboard focus above every surface treatment. |

Rules:

- No `DropShadowEffect`, gradient brush, blur, transparency material, acrylic, glass, grain, glow, photographic backing, or decorative texture.
- The editor is an open primary plane, not a rounded card inside another card.
- The sidebar and the clipboard drawer are support planes, not equal dashboard cards.
- Overlay and consent separation uses `Daynote.Brush.Border.Control` plus tonal contrast, not a shadow.
- Radius is functional and scarce. Use control, panel, and dialog radius keys only for the roles named in Section 4.
- High Contrast replaces all tonal hierarchy with system brushes and preserves structure through borders, headings, and spacing.

## 8. Accessibility Constraints & Accepted Debt

### Constraints

- WCAG 2.2 AA is the design target where its success criteria apply to native desktop software. Normal text targets at least 4.5:1; large text, focus indicators, control boundaries, and meaningful non-text cues target at least 3:1 against adjacent colors.
- All interactive elements have a visible keyboard focus indicator using `Daynote.Brush.Focus`, focus gap, and Section 4 thickness resources. Focus and selection remain distinguishable.
- Keyboard order follows reading and task order. F6 region cycling, Ctrl+F search, Escape restoration, standard text editing, tab semantics, and safe dialog focus are mandatory.
- Every custom control exposes an accurate UI Automation name, control type/role, value, state, selection, and enabled status. Icons that are purely decorative are removed from the accessibility tree.
- High Contrast uses Windows system brushes. Color, opacity, animation, tooltip, or icon shape alone never carries required meaning.
- Reduced motion is forced through the showcase `DaynoteMotionPolicy` override and uses `Daynote.Motion.Instant` while retaining orientation through headings, status, and focus. Release QA also verifies the real Windows client-area animation preference path.
- Primary actions and icon buttons use `Daynote.Size.Target.Primary`. A secondary dense visual box uses `Daynote.Size.Target.Secondary` only inside a centered primary-size hit region and with a complete keyboard path.
- At 200% scaling, primary tasks remain reachable, type never shrinks, support panes collapse before editor or recovery actions, and primary content has no horizontal scrollbar.
- Korean/CJK text is tested in titles, editor, calendar announcements, clipboard preview, search query/results, settings, and statuses. No tofu, baseline clipping, accidental ellipsis, or inaccessible trimmed value is accepted.
- IME composition must preserve candidate/caret behavior and must not trigger premature persistence, navigation, or search. Composition cancellation returns the prior stable value.
- Loading, empty, disabled, validation, and error states keep focus predictable and provide plain-language recovery. Errors and diagnostics never reveal note or clipboard payload.
- Destructive actions state their consequence, initially focus the safe action in modal confirmation, accept Escape cancellation, and restore the invoker.
- Tooltip-only instructions are prohibited. Clipboard and note payload are not automatically announced by global status or error regions.

### Persona acceptance walkthroughs

| Persona | Required walkthrough |
| --- | --- |
| Keyboard-first knowledge worker | Select historical date, create and reorder notes, edit and save, inspect/copy/delete clipboard item, search, open exact result, close overlay, and verify restored focus. |
| Low-vision and high-DPI user | Repeat primary journey at 200% scaling and High Contrast in the minimum window, with every primary action, focus cue, label, and recovery path visible. |
| Distractibility or memory-load user | Switch dates during clean and dirty states, observe save and capture feedback, encounter a save failure, recover through Retry, and confirm date scope never becomes ambiguous. |
| Korean/CJK writer | Compose and cancel Korean IME text, commit mixed Korean/Latin Markdown, use short Korean search, inspect wrapped results and statuses, and verify full accessible names for trimmed tabs. |

Persona failure blocks completion unless the user explicitly accepts a located debt with affected users, severity, remediation, owner, and exit condition. A screenshot similarity score cannot override an accessibility or task-completion failure.

### Accepted Debt

| Item | Location | Why accepted | Owner / Exit |
| --- | --- | --- | --- |
| None | None | No accessibility or design debt is accepted for this contract. | Any future debt requires explicit user acceptance and a recorded exit condition before release. |

### Implementation and handoff checks

- No product XAML or code uses raw unregistered colors, type values, spacing values, radii, borders, target sizes, or visual durations.
- No primitive is used in a product screen before its applicable showcase states pass at Compact, Regular, Wide, 200% scaling, High Contrast, Korean IME/CJK, long text, and unbroken text.
- Later visual claims must cite fresh actual-WPF artifacts for the screenshot set in Section 1. Static images may support fidelity judgment, but keyboard, UI Automation, tray, IME, scroll, and focus claims require exercised runtime evidence.
- Reference fidelity requires the default-state Wide actual-WPF capture defined in Section 1 item 15 and a recorded region-by-region reviewer comparison against the 2026-07-20 user-supplied reference direction. No automated similarity score exists or can supply a pass; the superseded Warm Paper image-diff requirement is retired.
- Motion claims require normal and forced-reduced-motion evidence from the same current build. Every animated primitive has rest, registered midpoint, and settled normal-motion frames, a reduced-motion interaction with no intermediate visual state, and pointer plus keyboard logs that identify focus and scroll ownership.
- Significant implementation closes through independent visual, accessibility, persona, and implementation review. This contract itself does not assert implementation or showcase completion.
