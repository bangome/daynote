using System.Globalization;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Widgets;

/// <summary>
/// Which to-do a widget row stands for, in a form that survives ticking it.
/// </summary>
/// <remarks>
/// Not the row's own id. Ticking an occurrence of a rule writes an override with a new id
/// (<see cref="ToggleAgendaItem"/>), so the row a tap left behind would not be found again by the
/// id it was drawn with; the series plus the occurrence's original start is the same before and
/// after. A one-off is its own root and has no occurrence.
/// </remarks>
public readonly record struct WidgetRowKey(Guid Root, WallClock? Occurrence)
{
    public static WidgetRowKey Of(AgendaDayRow row) => new(row.Item.SeriesId ?? row.Item.Id, row.RecurrenceId);

    /// <summary><c>root</c>, or <c>root@2026-10-08T07:00</c> for an occurrence; what a tap's intent carries.</summary>
    public override string ToString() =>
        Occurrence is { } occurrence ? $"{Root:N}@{occurrence}" : Root.ToString("N");

    public static WidgetRowKey? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        int at = text.IndexOf('@', StringComparison.Ordinal);
        if (!Guid.TryParseExact(at < 0 ? text : text[..at], "N", out Guid root))
        {
            return null;
        }

        if (at < 0)
        {
            return new WidgetRowKey(root, null);
        }

        try
        {
            return new WidgetRowKey(root, WallClock.Parse(text[(at + 1)..]));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>A row the widget just ticked, kept on screen dimmed until the next update drops it.</summary>
/// <param name="Day">The day the row is owed on, which is where its finished copy is looked up.</param>
public readonly record struct WidgetSettling(WidgetRowKey Key, DateOnly Day);

/// <summary>One to-do line on a widget.</summary>
/// <param name="When">"14:00" for today, "10/6" for a day already gone, empty for no clock.</param>
/// <param name="ListColor">An index into <see cref="WidgetSnapshot.ListColors"/>.</param>
public sealed record WidgetTodo(
    WidgetRowKey Key,
    DateOnly Day,
    string Title,
    string When,
    bool IsOverdue,
    bool IsRepeat,
    bool IsDone,
    int ListColor);

/// <summary>The next event, for the 다음 일정 card.</summary>
/// <param name="When">The large "16:00", or 종일.</param>
/// <param name="Span">"16:00–17:00", led by the day when it is not today: "내일 · 16:00–17:00".</param>
public sealed record WidgetEvent(string Title, string When, string Span);

/// <summary>A cell of the 4×4 widget's week strip.</summary>
public sealed record WidgetWeekday(string Name, int Number, bool IsToday);

public enum WidgetState
{
    Ready,

    /// <summary>The profile's account has the lock on and this device has not been unlocked.</summary>
    Locked,

    /// <summary>
    /// The app was updated and has not yet brought its database up to the new schema; the widget
    /// does not migrate it, so it asks for the app to be opened.
    /// </summary>
    Outdated,
}

/// <summary>
/// Everything the home-screen widgets draw, decided here so the head only lays it out.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rows are the 할 일 tab's 오늘 band</b> (<see cref="AgendaOutstanding"/>): due today or
/// already past it, a rule as its next open occurrence. A second definition of "what is owed today"
/// is how a to-do ends up on the widget and not in the app. Events are not rows; the next one gets
/// its own card.
/// </para>
/// <para>
/// <b>A tick dims, then drops.</b> A row the widget has just ticked is no longer owed, so it is
/// fetched back from its day and drawn done in the place it had, until the head stops passing it as
/// settling. There is no animation between the two (motion M8's Android rule): each is a state.
/// </para>
/// <para>
/// <b>Locked shows nothing.</b> Not a title, not a count: the state alone.
/// </para>
/// </remarks>
public sealed record WidgetSnapshot(
    WidgetState State,
    DateOnly Today,
    string DateHeader,
    int Remaining,
    IReadOnlyList<WidgetTodo> Todos,
    WidgetEvent? NextEvent,
    IReadOnlyList<WidgetWeekday> Week,
    DateTime NextRefresh,
    AppLanguage Language)
{
    /// <summary>
    /// Check-ring colours, as ARGB. A list's colour is its place in the list order, the built-in
    /// list first: the schema has no colour column yet, and the design keeps the ring in the list's
    /// colour on every surface, so the order is the one thing every surface can agree on.
    /// </summary>
    /// <remarks>
    /// No red: on a widget red means overdue.
    /// </remarks>
    public static IReadOnlyList<uint> ListColors { get; } =
        [0xFFEE7F35, 0xFF5A64C4, 0xFF2FB574, 0xFF1F9AA8, 0xFF9B5AC4, 0xFFC9A227];

    public string Text(string key) =>
        MobileCatalog.For(Language).TryGetValue(key, out string? value) ? value : key;

    public string Format(string key, params object[] args) => string.Format(Culture(Language), Text(key), args);

    public static CultureInfo Culture(AppLanguage language) =>
        CultureInfo.GetCultureInfo(language == AppLanguage.English ? "en-US" : "ko-KR");

    /// <summary>How far ahead the next event is looked for.</summary>
    public const int EventDays = 7;

    public static WidgetSnapshot Locked(DateTime now, AppLanguage language) =>
        Empty(WidgetState.Locked, now, language);

    public static WidgetSnapshot Outdated(DateTime now, AppLanguage language) =>
        Empty(WidgetState.Outdated, now, language);

    /// <summary>A state with nothing of the content: no rows, no count, no event.</summary>
    private static WidgetSnapshot Empty(WidgetState state, DateTime now, AppLanguage language)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        return new WidgetSnapshot(
            state, today, FormatDateHeader(today, language), 0, [], null, WeekOf(today, language),
            now.Date.AddDays(1), language);
    }

    /// <param name="lists">Every list, default first, as the repository returns them.</param>
    /// <param name="settling">Rows ticked from the widget that should stay drawn, done.</param>
    public static WidgetSnapshot Build(
        DateTime now,
        IReadOnlyList<AgendaItem> items,
        IReadOnlyList<AgendaList> lists,
        AppLanguage language,
        IReadOnlyCollection<WidgetSettling>? settling = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lists);

        DateOnly today = DateOnly.FromDateTime(now);
        AgendaDayRow[] owed = [.. AgendaOutstanding.For(today, items).Today
            .Where(static row => row.Item.Kind == AgendaKind.Task)];

        var rows = new List<(AgendaDayRow Row, DateOnly Day)>();
        foreach (AgendaDayRow row in owed)
        {
            rows.Add((row, DayOf(row)));
        }

        foreach (WidgetSettling settled in settling ?? [])
        {
            if (rows.Any(entry => WidgetRowKey.Of(entry.Row) == settled.Key))
            {
                continue;
            }

            // Its finished copy, from the day it was owed on. Unticked elsewhere in the meantime it
            // is simply owed again and already listed above.
            AgendaDayRow? done = AgendaDay.For(settled.Day, items).Done
                .Cast<AgendaDayRow?>()
                .FirstOrDefault(row => row is { } r && WidgetRowKey.Of(r) == settled.Key);
            if (done is { } finished && finished.Item.Kind == AgendaKind.Task)
            {
                rows.Add((finished, settled.Day));
            }
        }

        // The 할 일 tab's order: soonest first, a timed row before the merely owed on its day.
        // Stable, so the settling rows land where they stood.
        WidgetTodo[] todos = [.. rows
            .OrderBy(static entry => entry.Row.At?.Value ?? entry.Day.ToDateTime(TimeOnly.MaxValue))
            .Select(entry => Todo(entry.Row, entry.Day, today, now, lists))];

        (WidgetEvent? next, DateTime? nextEnds) = FindNextEvent(now, items, language);

        return new WidgetSnapshot(
            WidgetState.Ready,
            today,
            FormatDateHeader(today, language),
            owed.Length,
            todos,
            next,
            WeekOf(today, language),
            NextRefreshAfter(now, owed, nextEnds),
            language);
    }

    /// <summary>"10월 7일 수요일" / "Wednesday, Oct 7".</summary>
    public static string FormatDateHeader(DateOnly day, AppLanguage language) =>
        day.ToString(language == AppLanguage.English ? "dddd, MMM d" : "M월 d일 dddd", Culture(language));

    private static WidgetTodo Todo(
        AgendaDayRow row, DateOnly day, DateOnly today, DateTime now, IReadOnlyList<AgendaList> lists)
    {
        string when = day < today
            ? day.ToString("M/d", CultureInfo.InvariantCulture)
            : row.At is { } at ? at.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;

        int index = 0;
        for (int i = 0; i < lists.Count; i++)
        {
            if (lists[i].Id == row.Item.ListId)
            {
                index = i;
                break;
            }
        }

        return new WidgetTodo(
            WidgetRowKey.Of(row),
            day,
            row.Item.Title,
            when,
            !row.IsDone && (day < today || row.IsOverdue(now)),
            row.IsOccurrence,
            row.IsDone,
            index % ListColors.Count);
    }

    /// <summary>The day a row is owed on: its occurrence's, else its anchor's.</summary>
    private static DateOnly DayOf(AgendaDayRow row) =>
        DateOnly.FromDateTime(row.Falls?.Value ?? DateTime.MinValue);

    /// <summary>
    /// The first event that has not ended, today or in the week ahead, and when it ends.
    /// </summary>
    /// <remarks>
    /// An event with no end is an instant, unless it starts at midnight: all-day is modelled as a
    /// start with no end (<see cref="AgendaItem.HasClockTime"/>), and that one lasts the day.
    /// </remarks>
    private static (WidgetEvent? Event, DateTime? Ends) FindNextEvent(
        DateTime now, IReadOnlyList<AgendaItem> items, AppLanguage language)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        for (int offset = 0; offset < EventDays; offset++)
        {
            DateOnly day = today.AddDays(offset);
            foreach (AgendaDayRow row in AgendaDay.For(day, items).Open)
            {
                if (row.Item.Kind != AgendaKind.Event || row.At is not { } at)
                {
                    continue;
                }

                DateTime start = at.Value;
                DateTime? end = row.Item is { StartsAt: { } from, EndsAt: { } to } && to.Value > from.Value
                    ? start + (to.Value - from.Value)
                    : null;
                bool allDay = end is null && start.TimeOfDay == TimeSpan.Zero;
                DateTime ends = end ?? (allDay ? start.Date.AddDays(1) : start);
                if (ends <= now)
                {
                    continue;
                }

                CultureInfo culture = Culture(language);
                string clock = start.ToString("HH:mm", CultureInfo.InvariantCulture);
                string range = allDay
                    ? Text(language, "WidgetAllDay")
                    : end is { } finish
                        ? $"{clock}–{finish.ToString("HH:mm", CultureInfo.InvariantCulture)}"
                        : clock;
                string span = offset switch
                {
                    0 => range,
                    1 => $"{Text(language, "WidgetTomorrow")} · {range}",
                    _ => $"{day.ToString(language == AppLanguage.English ? "ddd, MMM d" : "M월 d일 (ddd)", culture)} · {range}",
                };

                return (new WidgetEvent(row.Item.Title, allDay ? Text(language, "WidgetAllDay") : clock, span), ends);
            }
        }

        return (null, null);
    }

    /// <summary>Sunday to Saturday around today, as the phone's own week strip runs.</summary>
    private static IReadOnlyList<WidgetWeekday> WeekOf(DateOnly today, AppLanguage language)
    {
        CultureInfo culture = Culture(language);
        DateOnly sunday = today.AddDays(-(int)today.DayOfWeek);
        return [.. Enumerable.Range(0, 7).Select(i =>
        {
            DateOnly day = sunday.AddDays(i);
            return new WidgetWeekday(
                culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek), day.Day, day == today);
        })];
    }

    /// <summary>
    /// The next moment the widget would draw differently with nothing changed in the store: a
    /// timed to-do turning overdue, the event on the card ending, or midnight.
    /// </summary>
    private static DateTime NextRefreshAfter(DateTime now, IReadOnlyList<AgendaDayRow> owed, DateTime? eventEnds)
    {
        DateTime next = now.Date.AddDays(1);
        foreach (AgendaDayRow row in owed)
        {
            if (row.At is { } at && at.Value > now && at.Value < next)
            {
                next = at.Value;
            }
        }

        return eventEnds is { } ends && ends > now && ends < next ? ends : next;
    }

    private static string Text(AppLanguage language, string key) => MobileCatalog.For(language)[key];
}
