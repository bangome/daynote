using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;

namespace Daynote.App.Glance;

/// <summary>
/// Builds the <see cref="GlanceSnapshot"/> from what the app has already read.
/// </summary>
/// <remarks>
/// Pure, so the phone, the Mac and a test all get the same file from the same rows. The caller
/// passes everything in; nothing here touches a repository, a clock or the language service.
/// </remarks>
public static class GlanceSnapshotBuilder
{
    /// <summary>How many days ahead the snapshot carries, today included.</summary>
    public const int DaysAhead = 7;

    /// <summary>
    /// Per day. The large widget shows three to-dos and two events and the watch scrolls; past a
    /// dozen nobody is reading a wrist, and the watch's application context has a size limit.
    /// </summary>
    private const int MaxTodosPerDay = 12;

    private const int MaxEventsPerDay = 8;

    /// <summary>The medium favourites widget shows two; the rest are for a reader with more room.</summary>
    private const int MaxFavorites = 4;

    /// <param name="items">Every agenda row, series and overrides included, as the day panel reads them.</param>
    /// <param name="lists">Every list, in the sidebar's order (default first).</param>
    /// <param name="notes">Every note, for the week's counts, today's titles and the favourites.</param>
    /// <param name="now">The local wall clock now.</param>
    /// <param name="utcNow">The same instant, for the stamp.</param>
    /// <param name="zone">The IANA zone <paramref name="now"/> is in.</param>
    /// <param name="language">The app's language.</param>
    /// <param name="locked">The account's lock has the notes sealed; nothing is written but that fact.</param>
    public static GlanceSnapshot Build(
        IReadOnlyList<AgendaItem> items,
        IReadOnlyList<AgendaList> lists,
        IReadOnlyList<NoteSummary> notes,
        DateTime now,
        DateTimeOffset utcNow,
        string zone,
        AppLanguage language,
        bool locked)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(notes);

        DateOnly today = DateOnly.FromDateTime(now);
        string stamp = utcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string code = language == AppLanguage.English ? "en" : "ko";

        if (locked)
        {
            return new GlanceSnapshot(GlanceSnapshot.CurrentSchema, stamp, zone, code, Day(today), true, [], [], [], []);
        }

        Dictionary<Guid, LocalDate> noteDates = notes.ToDictionary(static note => note.Id, static note => note.LocalDate);

        GlanceList[] glanceLists = [.. lists.Select((list, position) =>
        {
            (string light, string dark) = AgendaListPalette.ForPosition(position);
            return new GlanceList(Id(list.Id), list.HasBuiltInName ? AppStrings.AgendaListDefaultName : list.Name, light, dark);
        })];

        var days = new List<GlanceDay>(DaysAhead);
        for (int offset = 0; offset < DaysAhead; offset++)
        {
            DateOnly date = today.AddDays(offset);
            AgendaDayView view = AgendaDay.For(date, items);
            AgendaDayRow[] rows = [.. view.Open, .. view.Done];

            days.Add(new GlanceDay(
                Day(date),
                [.. rows.Where(static row => row.Item.Kind == AgendaKind.Task).Take(MaxTodosPerDay)
                    .Select(row => Todo(row, noteDates))],
                [.. rows.Where(static row => row.Item.Kind == AgendaKind.Event)
                    .OrderBy(static row => row.At?.Value ?? DateTime.MinValue)
                    .Take(MaxEventsPerDay)
                    .Select(row => Event(row, noteDates))]));
        }

        // Sunday to Saturday, the phone's strip, so the widget's week is the week the app shows.
        DateOnly weekStart = today.AddDays(-(int)today.DayOfWeek);
        ILookup<LocalDate, NoteSummary> byDate = notes.ToLookup(static note => note.LocalDate);
        GlanceWeekDay[] week = [.. Enumerable.Range(0, 7).Select(offset =>
        {
            DateOnly date = weekStart.AddDays(offset);
            NoteSummary[] onDay = [.. byDate[LocalDates.FromDateOnly(date)].OrderBy(static note => note.SortOrder)];
            return new GlanceWeekDay(Day(date), onDay.Length, [.. onDay.Select(static note => note.Title)]);
        })];

        GlanceFavorite[] favorites = [.. notes
            .Where(static note => note.IsFavorite)
            .OrderByDescending(static note => (note.LocalDate.Year, note.LocalDate.Month, note.LocalDate.Day))
            .ThenBy(static note => note.SortOrder)
            .Take(MaxFavorites)
            .Select(static note => new GlanceFavorite(
                Id(note.Id),
                Day(LocalDates.ToDateOnly(note.LocalDate)),
                note.Title,
                Preview(note.Body)))];

        return new GlanceSnapshot(
            GlanceSnapshot.CurrentSchema, stamp, zone, code, Day(today), false, glanceLists, days, week, favorites);
    }

    /// <summary>The snapshot as the file holds it: UTF-8 JSON with Hangul left as Hangul, which halves it.</summary>
    public static string Serialize(GlanceSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, typeof(GlanceSnapshot), Options);

    /// <summary>Hangul written as itself rather than <c>\uXXXX</c>: the watch's context has a size limit.</summary>
    internal static readonly JsonSerializerOptions Options = new(GlanceJson.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = GlanceJson.Default,
    };

    private static GlanceTodo Todo(AgendaDayRow row, Dictionary<Guid, LocalDate> noteDates)
    {
        AgendaItem item = row.Item;
        bool repeats = item.IsSeries || item.IsOverride;
        return new GlanceTodo(
            Id(item.Id),
            item.IsSeries ? Id(item.Id) : item.SeriesId is { } series ? Id(series) : null,
            row.RecurrenceId?.ToString(),
            item.Title,
            Id(item.ListId),
            row.At is { } at ? Clock(at.Value) : null,
            repeats,
            row.IsDone,
            item.SourceNoteId is { } note ? Id(note) : null,
            NoteDate(item.SourceNoteId, noteDates));
    }

    private static GlanceEvent Event(AgendaDayRow row, Dictionary<Guid, LocalDate> noteDates)
    {
        AgendaItem item = row.Item;

        // An occurrence carries the series row, whose own start and end are the first
        // occurrence's; the length is what carries over to this one.
        DateTime? start = row.At?.Value;
        DateTime? end = start is { } begins && item.StartsAt is { } first && item.EndsAt is { } last
            ? begins + (last.Value - first.Value)
            : null;
        bool allDay = item.EndsAt is null;

        return new GlanceEvent(
            Id(item.Id),
            item.Title,
            Id(item.ListId),
            allDay || start is null ? null : Clock(start.Value),
            allDay || end is null ? null : Clock(end.Value),
            item.IsSeries || item.IsOverride,
            item.SourceNoteId is { } note ? Id(note) : null,
            NoteDate(item.SourceNoteId, noteDates));
    }

    private static string? NoteDate(Guid? noteId, Dictionary<Guid, LocalDate> noteDates) =>
        noteId is { } id && noteDates.TryGetValue(id, out LocalDate date) ? Day(LocalDates.ToDateOnly(date)) : null;

    /// <summary>"1. 2분기 지표 리뷰 · 2. 신규 기능 우선순위": the first two lines, as the favourites card reads.</summary>
    private static string Preview(string body)
    {
        (string summary, _) = NoteBodySummary.Summarize(body, maxChars: 90, maxLines: 2);
        return string.Join(" · ", summary.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string Id(Guid id) => id.ToString("D");

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Clock(DateTime at) => at.ToString("HH:mm", CultureInfo.InvariantCulture);
}
