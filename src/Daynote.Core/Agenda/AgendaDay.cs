namespace Daynote.Core.Agenda;

/// <summary>One line in a day's to-do list (design §04, 4c/4d).</summary>
/// <param name="Item">The row to show and edit. For an occurrence, the override if there is one.</param>
/// <param name="RecurrenceId">
/// Which occurrence this is, or null for a one-off. Completing an occurrence writes an override
/// against this, not against the series, which is what keeps "I did it this Monday" from meaning
/// "I did it every Monday".
/// </param>
/// <param name="At">Where it falls. Null for a to-do with a day and no clock.</param>
public readonly record struct AgendaDayRow(
    AgendaItem Item,
    WallClock? RecurrenceId,
    WallClock? At)
{
    public bool IsOccurrence => RecurrenceId is not null;

    public bool IsDone => Item.Status == AgendaStatus.Completed;

    /// <summary>True when the clock has gone past it and nobody ticked it.</summary>
    public bool IsOverdue(DateTime now) => !IsDone && At is { } at && at.Value < now;
}

/// <summary>A day's to-dos, as both shells' panels read them.</summary>
/// <param name="Open">What is left, timed first and then undated.</param>
/// <param name="Done">What was finished, which both designs collapse behind a count.</param>
public readonly record struct AgendaDayView(
    IReadOnlyList<AgendaDayRow> Open,
    IReadOnlyList<AgendaDayRow> Done);

/// <summary>
/// The day panel's contents (docs/TODOS.md §11, design §04).
/// </summary>
/// <remarks>
/// Pure, and shared by the desktop's right-hand panel and the phone's Day screen. They lay the
/// rows out differently — §99 calls that an intended difference — but which rows there are, and in
/// what order, is one decision and is made here. Two implementations of "what is due today" is
/// how the same to-do ends up on one screen and not the other.
/// <para>
/// <b>One list regardless of where it came from.</b> §11 is explicit: this is what someone looks
/// at in the morning, so it must not be split by origin. A to-do captured with <c>@</c>, one made
/// in the list view and one carried over by the §8 migration are the same kind of thing by the
/// time they reach here.
/// </para>
/// </remarks>
public static class AgendaDay
{
    /// <summary>
    /// The rows for <paramref name="date"/>, from everything loaded.
    /// </summary>
    /// <param name="items">
    /// One-off items, series and overrides together. A series contributes its occurrences on this
    /// day rather than itself, and an override is folded into the occurrence it replaces.
    /// </param>
    public static AgendaDayView For(DateOnly date, IReadOnlyList<AgendaItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var rows = new List<AgendaDayRow>();
        var foldedIn = new HashSet<Guid>();

        foreach (AgendaItem series in items.Where(static item => item.IsSeries))
        {
            AgendaItem[] overrides = [.. items.Where(other => other.SeriesId == series.Id)];
            foreach (AgendaItem part in overrides)
            {
                foldedIn.Add(part.Id);
            }

            foreach (AgendaOccurrence occurrence in AgendaRecurrence.Expand(series, overrides, date, date))
            {
                rows.Add(new AgendaDayRow(
                    occurrence.Item,
                    occurrence.RecurrenceId,
                    Clock(occurrence.Item, occurrence.Start)));
            }
        }

        foreach (AgendaItem item in items)
        {
            if (item.IsSeries || foldedIn.Contains(item.Id))
            {
                continue;
            }

            // An override whose series was not loaded still belongs to the day it falls on. Losing
            // it would be a to-do disappearing because of a loading order the user cannot see.
            if (item.Anchor is { } anchor && DateOnly.FromDateTime(anchor.Value) == date)
            {
                rows.Add(new AgendaDayRow(item, item.RecurrenceId, Clock(item, anchor)));
            }
        }

        // Timed first and in time order, then the undated ones by title — the design's order, and
        // the one a morning reading wants: what is pinned to an hour, then what is simply owed.
        // Cancelled items are in neither list; they are not finished, they are called off.
        AgendaDayRow[] live = [.. rows.Where(static row => row.Item.Status != AgendaStatus.Cancelled)];

        return new AgendaDayView(
            [.. live.Where(static row => !row.IsDone).OrderBy(static row => row.At?.Value ?? DateTime.MaxValue)
                .ThenBy(static row => row.Item.Title, StringComparer.CurrentCulture)],
            [.. live.Where(static row => row.IsDone).OrderBy(static row => row.At?.Value ?? DateTime.MaxValue)
                .ThenBy(static row => row.Item.Title, StringComparer.CurrentCulture)]);
    }

    /// <summary>
    /// The clock to show, or null when the item carries only a day.
    /// </summary>
    /// <remarks>
    /// <c>has_due_time</c> answers for whichever field holds the wall clock, the same way the
    /// reminder planner reads it. An event always has one: a block of time with no time is a
    /// contradiction, and an all-day event is modelled as a start with no end rather than as a
    /// start with no clock.
    /// </remarks>
    private static WallClock? Clock(AgendaItem item, WallClock anchor) =>
        item.Kind == AgendaKind.Event || item.HasDueTime ? anchor : null;
}
