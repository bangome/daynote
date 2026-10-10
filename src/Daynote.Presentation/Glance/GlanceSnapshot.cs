using System.Text.Json.Serialization;

namespace Daynote.App.Glance;

/// <summary>
/// Everything a widget, a watch or a menu bar needs to draw a day without opening the database
/// (docs/APPLE_EXTENSIONS.md §3).
/// </summary>
/// <remarks>
/// Written by the app into a folder another process can read — an App Group container on Apple
/// platforms — and read by native code that cannot run any of this. So it is plain data, already
/// decided: which rows a day has and in what order is <see cref="Daynote.Core.Agenda.AgendaDay"/>'s
/// answer, taken here once, and the reader only lays it out. A widget that worked out "what is due
/// today" for itself is how a to-do ends up on the widget and not in the app.
/// <para>
/// Every date and time is a local wall clock read against <see cref="Zone"/>, as the agenda keeps
/// them (docs/TODOS.md §6). A widget redraws on its own timeline long after the app has gone, so
/// it is handed seven days rather than one and picks the one that is today when it draws.
/// </para>
/// </remarks>
/// <param name="Schema">Bumped on a change a reader written for the old shape would misread.</param>
/// <param name="GeneratedUtc">When the app wrote it, ISO 8601.</param>
/// <param name="Zone">The IANA zone the wall clocks are in.</param>
/// <param name="Language"><c>ko</c> or <c>en</c>: the app's own language, which the widgets follow rather than the phone's.</param>
/// <param name="Today">The local date it was written on, <c>yyyy-MM-dd</c>.</param>
/// <param name="Locked">
/// True while the account's lock has the notes sealed. Everything below is then empty and a reader
/// says so rather than drawing an empty day as if it were one.
/// </param>
public sealed record GlanceSnapshot(
    int Schema,
    string GeneratedUtc,
    string Zone,
    string Language,
    string Today,
    bool Locked,
    IReadOnlyList<GlanceList> Lists,
    IReadOnlyList<GlanceDay> Days,
    IReadOnlyList<GlanceWeekDay> Week,
    IReadOnlyList<GlanceFavorite> Favorites)
{
    /// <summary>The schema this build writes.</summary>
    public const int CurrentSchema = 1;
}

/// <summary>A to-do list, for its name and its colour — the colour of every check ring in it.</summary>
/// <param name="Color">The ring on a light background, <c>#rrggbb</c>.</param>
/// <param name="ColorDark">The ring on a dark one: lighter, so it holds the same contrast.</param>
public sealed record GlanceList(string Id, string Name, string Color, string ColorDark);

/// <summary>One date's to-dos and events, in the order the app's own day panel shows them.</summary>
public sealed record GlanceDay(string Date, IReadOnlyList<GlanceTodo> Todos, IReadOnlyList<GlanceEvent> Events);

/// <summary>
/// One to-do on one date. For a repeating one this is the occurrence, and completing it completes
/// that day only.
/// </summary>
/// <param name="Id">The row to complete: the item, or the override already standing for this occurrence.</param>
/// <param name="SeriesId">The repeating rule this is an occurrence of, or null.</param>
/// <param name="Occurrence">Which occurrence (<c>RECURRENCE-ID</c>, <c>yyyy-MM-ddTHH:mm</c>), or null.</param>
/// <param name="Time"><c>HH:mm</c>, or null for a to-do with a day and no clock.</param>
/// <param name="Repeats">Draws the ↻.</param>
/// <param name="NoteId">Always null: to-dos are not linked to notes. Kept so older readers still decode.</param>
public sealed record GlanceTodo(
    string Id,
    string? SeriesId,
    string? Occurrence,
    string Title,
    string ListId,
    string? Time,
    bool Repeats,
    bool Done,
    string? NoteId,
    string? NoteDate);

/// <summary>One event on one date.</summary>
/// <param name="Start"><c>HH:mm</c>, or null for an all-day event.</param>
/// <param name="End"><c>HH:mm</c>, or null when it has none (all day).</param>
public sealed record GlanceEvent(
    string Id,
    string Title,
    string ListId,
    string? Start,
    string? End,
    bool Repeats,
    string? NoteId,
    string? NoteDate);

/// <summary>A day of the week strip: Sunday to Saturday around today, as the phone's strip runs.</summary>
/// <param name="Titles">Its notes' titles, for the line under the strip.</param>
public sealed record GlanceWeekDay(string Date, int NoteCount, IReadOnlyList<string> Titles);

/// <summary>A favourite note, newest first.</summary>
/// <param name="Preview">The first lines of the body, joined with " · ".</param>
public sealed record GlanceFavorite(string Id, string Date, string Title, string Preview);

/// <summary>
/// Something done outside the app — a check on a widget, a capture on the watch — waiting for the
/// app to carry it out through its own store, so it syncs like anything else (§4).
/// </summary>
/// <param name="Id">A fresh uuid. Also the id of the item a capture makes, so applying it twice makes one.</param>
/// <param name="Type">One of <see cref="GlanceActionTypes"/>.</param>
/// <param name="Date">For an action on a row (complete, uncomplete, delete): the date the row was shown on.</param>
/// <param name="Text">For a capture: what was said, whole.</param>
/// <param name="Kind">For a capture: <c>task</c>, <c>event</c> or <c>note</c>.</param>
/// <param name="CapturedLocal">For a capture: the local wall clock it was said at, which the date in it is read against.</param>
public sealed record GlanceAction(
    int Schema,
    string Id,
    string Type,
    string CreatedUtc,
    string? ItemId = null,
    string? SeriesId = null,
    string? Occurrence = null,
    string? Date = null,
    string? Text = null,
    string? Kind = null,
    string? CapturedLocal = null);

/// <summary>The values <see cref="GlanceAction.Type"/> takes.</summary>
public static class GlanceActionTypes
{
    public const string Complete = "complete";

    /// <summary>The watch's 완료 취소: a done row made open again. Never ticks.</summary>
    public const string Uncomplete = "uncomplete";

    /// <summary>The watch's 삭제. On an occurrence, that occurrence only (an EXDATE).</summary>
    public const string Delete = "delete";

    public const string Capture = "capture";
}

/// <summary>The one serializer for both files, camelCase, nulls left out.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GlanceSnapshot))]
[JsonSerializable(typeof(GlanceAction))]
public sealed partial class GlanceJson : JsonSerializerContext;
