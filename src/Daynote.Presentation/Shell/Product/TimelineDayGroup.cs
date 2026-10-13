using System.Globalization;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One day of the timeline as the desktop design lays it out: the date and its counts in a column
/// on the left, that day's note cards beside it, and its events and to-dos (Daynote Desktop B v2,
/// <c>tlGroups</c>).
/// </summary>
/// <remarks>
/// Built from <see cref="TimelineViewModel.Rows"/> rather than read separately, so paging, expansion
/// and the localized headings stay the timeline's own; this is only a different grouping of the same
/// rows, with the agenda folded in when the shell has one to give. The WPF shell keeps drawing the
/// flat list.
/// </remarks>
public sealed class TimelineDayGroup
{
    public TimelineDayGroup(TimelineDateHeaderRow header, IReadOnlyList<TimelineNoteRow> notes)
        : this(header, notes, [], [])
    {
    }

    public TimelineDayGroup(
        TimelineDateHeaderRow header,
        IReadOnlyList<TimelineNoteRow> notes,
        IReadOnlyList<TodoItemViewModel> untimed,
        IReadOnlyList<TimelineSlot> timed)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        Untimed = untimed ?? throw new ArgumentNullException(nameof(untimed));
        Timed = timed ?? throw new ArgumentNullException(nameof(timed));

        int events = untimed.Count(static row => row.IsEvent) + timed.Count(static slot => slot.IsEvent);
        int todos = untimed.Count(static row => row.IsTask) + timed.Count(static slot => slot.IsTodo);
        CultureInfo culture = CultureInfo.CurrentCulture;
        CountText = string.Join(
            " · ",
            new[]
            {
                header.CountText,
                events > 0 ? string.Format(culture, AppStrings.DeskTimelineEventsFormat, events) : string.Empty,
                todos > 0 ? string.Format(culture, AppStrings.DeskTimelineTodosFormat, todos) : string.Empty,
            }.Where(static part => part.Length > 0));
    }

    public TimelineDateHeaderRow Header { get; }

    public LocalDate Date => Header.Date;

    public string Heading => Header.Heading;

    /// <summary>"노트 2개 · 일정 1 · 할 일 3": the notes always, the rest when there are any.</summary>
    public string CountText { get; }

    public IReadOnlyList<TimelineNoteRow> Notes { get; }

    /// <summary>The day's all-day events and to-dos with no clock, in the "시각 없음" box above the hours.</summary>
    public IReadOnlyList<TodoItemViewModel> Untimed { get; }

    public bool HasUntimed => Untimed.Count > 0;

    /// <summary>What is pinned to an hour, in hour order, with today's "now" line where it falls.</summary>
    public IReadOnlyList<TimelineSlot> Timed { get; }
}

/// <summary>
/// One line in a day's run of hours: an event or a timed to-do, or the red line at the current time.
/// </summary>
public sealed class TimelineSlot
{
    public TimelineSlot(string time, TodoItemViewModel? row)
    {
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Row = row;
    }

    /// <summary>"14:00" in the hour column.</summary>
    public string Time { get; }

    /// <summary>The event or to-do, or null for the now line.</summary>
    public TodoItemViewModel? Row { get; }

    public bool IsNow => Row is null;

    public bool IsEvent => Row is { IsEvent: true };

    public bool IsTodo => Row is { IsTask: true };
}

/// <summary>What the timeline needs to fold the agenda into its days.</summary>
/// <param name="Items">Everything loaded: one-offs, series and overrides.</param>
/// <param name="Now">The local wall clock, which says which day is today and where its now line goes.</param>
/// <param name="HasMoreNotes">
/// True while older notes are still to be paged in. A day the notes have not reached yet is not
/// drawn for its agenda alone, or the page would arrive under it out of order.
/// </param>
/// <param name="Row">Builds the row a day's item shows as, so a tick and an edit go where the panels' do.</param>
public sealed record TimelineAgenda(
    IReadOnlyList<AgendaItem> Items,
    DateTime Now,
    bool HasMoreNotes,
    Func<AgendaDayRow, TodoItemViewModel> Row);

/// <summary>Folds the timeline's flat header-then-cards rows into per-day groups.</summary>
public static class TimelineGrouping
{
    /// <summary>
    /// Each header opens a group and the cards after it join it, in order. Cards before any header —
    /// which the timeline never produces — are dropped rather than invented a date for.
    /// </summary>
    public static IReadOnlyList<TimelineDayGroup> Group(IEnumerable<TimelineRow> rows) => Group(rows, agenda: null);

    /// <summary>
    /// The same, with the agenda folded in (docs/TODOS.md §11): events, and to-dos whose own
    /// <see cref="AgendaItem.ShowsInTimeline"/> says so, on the days they fall. A day with no
    /// notes still gets a group when a one-off item falls on it, and today always does, so what is
    /// booked can be read against what happened.
    /// </summary>
    /// <remarks>
    /// A repeating item never makes a day appear by itself: a daily rule would otherwise turn every
    /// date into a group. It shows on the days that are there for some other reason.
    /// </remarks>
    public static IReadOnlyList<TimelineDayGroup> Group(IEnumerable<TimelineRow> rows, TimelineAgenda? agenda)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var notesByDate = new Dictionary<LocalDate, (TimelineDateHeaderRow Header, List<TimelineNoteRow> Notes)>();
        var order = new List<LocalDate>();
        TimelineDateHeaderRow? header = null;
        foreach (TimelineRow row in rows)
        {
            switch (row)
            {
                case TimelineDateHeaderRow next:
                    header = next;
                    if (!notesByDate.ContainsKey(next.Date))
                    {
                        notesByDate[next.Date] = (next, []);
                        order.Add(next.Date);
                    }

                    break;
                case TimelineNoteRow note when header is not null:
                    notesByDate[header.Date].Notes.Add(note);
                    break;
            }
        }

        if (agenda is null)
        {
            return [.. order.Select(date => new TimelineDayGroup(notesByDate[date].Header, notesByDate[date].Notes))];
        }

        DateOnly today = DateOnly.FromDateTime(agenda.Now);
        var dates = new HashSet<DateOnly>(order.Select(LocalDates.ToDateOnly)) { today };
        foreach (AgendaItem item in agenda.Items)
        {
            if (!item.IsSeries && item.Status != AgendaStatus.Cancelled && item.ShowsInTimeline &&
                item.Anchor is { } anchor)
            {
                dates.Add(DateOnly.FromDateTime(anchor.Value));
            }
        }

        if (agenda.HasMoreNotes && order.Count > 0)
        {
            DateOnly oldest = order.Select(LocalDates.ToDateOnly).Min();
            dates.RemoveWhere(date => date < oldest);
        }

        var groups = new List<TimelineDayGroup>();
        foreach (DateOnly date in dates.OrderDescending())
        {
            LocalDate local = LocalDates.FromDateOnly(date);
            AgendaDayView day = AgendaDay.For(date, agenda.Items);
            AgendaDayRow[] shown = [.. day.Open.Concat(day.Done).Where(static row => row.Item.ShowsInTimeline)];

            var untimed = new List<TodoItemViewModel>();
            var timed = new List<(DateTime At, TimelineSlot Slot)>();
            foreach (AgendaDayRow row in shown)
            {
                TodoItemViewModel model = agenda.Row(row);
                if (model.IsTimed && row.At is { } at)
                {
                    timed.Add((at.Value, new TimelineSlot(model.TimeLabel, model)));
                }
                else
                {
                    untimed.Add(model);
                }
            }

            if (date == today)
            {
                // After anything at the same minute: the line is where the clock is now.
                timed.Add((agenda.Now.AddTicks(1),
                    new TimelineSlot(agenda.Now.ToString(AppStrings.TodoRowTimeFormat, CultureInfo.CurrentCulture), null)));
            }

            bool hasNotes = notesByDate.TryGetValue(local, out var notes);
            if (!hasNotes && untimed.Count == 0 && timed.All(static entry => entry.Slot.IsNow))
            {
                // Today with nothing on it at all: no group for a line on its own.
                continue;
            }

            groups.Add(new TimelineDayGroup(
                hasNotes ? notes.Header : new TimelineDateHeaderRow(local, 0),
                hasNotes ? notes.Notes : [],
                untimed,
                [.. timed.OrderBy(static entry => entry.At).Select(static entry => entry.Slot)]));
        }

        return groups;
    }
}
