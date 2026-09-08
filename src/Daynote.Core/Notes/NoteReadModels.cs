using Daynote.Core.Domain;

namespace Daynote.Core.Notes;

/// <summary>
/// Cross-date rollup for a single local date, aggregated from notes, clipboard items, and day files in
/// one query. Feeds the calendar day cells (note-count badge and content dots). Only dates that hold at
/// least one note, clipboard item, or file are returned; callers treat an absent date as empty.
/// </summary>
public readonly record struct DateContentSummary(
    LocalDate Date,
    int NoteCount,
    bool HasClipboard,
    bool HasFiles);

/// <summary>
/// Flat, tag-free projection of a persisted note used by cross-date consumers such as the todo panel,
/// which parses <c>-[]</c> items from every note body regardless of the selected date. <see cref="Title"/>
/// is the display title (the custom title when set, otherwise the default "노트 N"), matching
/// <see cref="Daynote.Core.Domain.Notes.Note.Title"/>.
/// </summary>
public readonly record struct NoteSummary(
    Guid Id,
    LocalDate LocalDate,
    string Title,
    string Body,
    int SortOrder,
    bool IsFavorite);

/// <summary>
/// One tag on one note, as stored in <c>note_tags</c>.
/// </summary>
/// <remarks>
/// Deliberately narrow: it carries the link and nothing about the note, so a consumer that wants both
/// joins these against <see cref="NoteSummary"/> rather than the read model growing a wide shape for
/// one caller. The 태그 panel is that consumer.
/// </remarks>
public readonly record struct NoteTagLink(Guid NoteId, string Tag, int SortOrder);
