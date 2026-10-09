namespace Daynote.Core.Agenda;

/// <summary>
/// Everything still owed, across dates (design §04, 4a).
/// </summary>
/// <param name="Today">Due today or already past it. What the panel puts at the top.</param>
/// <param name="Later">Dated, and still ahead.</param>
/// <param name="Undated">Owed with no day attached. Last, under its own heading.</param>
public readonly record struct AgendaOutstandingView(
    IReadOnlyList<AgendaDayRow> Today,
    IReadOnlyList<AgendaDayRow> Later,
    IReadOnlyList<AgendaDayRow> Undated)
{
    public int Count => Today.Count + Later.Count + Undated.Count;
}

/// <summary>
/// The 할 일 tab's cross-date list (docs/TODOS.md §11, design §04).
/// </summary>
/// <remarks>
/// <b>A repeating to-do appears once, as its next outstanding occurrence.</b> Listing every
/// occurrence would bury everything else under one daily rule, and the list is a list of what is
/// owed — a rule owes you one thing at a time. §5 reaches the same answer from the other end: an
/// export emits the next outstanding occurrence only, because no other client can hold our
/// completion history anyway.
/// <para>
/// Finished items are absent rather than collapsed. The day panel keeps them behind a "완료 N"
/// row because the day is a record of itself; this list is a queue, and a queue of things already
/// done is not a queue.
/// </para>
/// </remarks>
public static class AgendaOutstanding
{
    /// <summary>How far ahead a series is searched for its next occurrence.</summary>
    /// <remarks>
    /// A year. Past that the rule has either ended or is one nobody is waiting on, and the search
    /// is what stops a `COUNT` that ran out from being hunted for forever.
    /// </remarks>
    public const int SearchDays = 400;

    public static AgendaOutstandingView For(DateOnly today, IReadOnlyList<AgendaItem> items)
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

            // From today, not from now: something due at nine this morning is still owed at ten.
            if (NextOutstanding(series, overrides, today) is { } next)
            {
                rows.Add(new AgendaDayRow(next.Item, next.RecurrenceId, Clock(next.Item, next.Start)));
            }
        }

        foreach (AgendaItem item in items)
        {
            if (item.IsSeries || foldedIn.Contains(item.Id) || item.Status != AgendaStatus.NeedsAction)
            {
                continue;
            }

            rows.Add(new AgendaDayRow(item, item.RecurrenceId, item.Anchor is { } at ? Clock(item, at) : null));
        }

        return new AgendaOutstandingView(
            [.. rows.Where(row => Day(row) is { } day && day <= today).OrderBy(Ordering)],
            [.. rows.Where(row => Day(row) is { } day && day > today).OrderBy(Ordering)],
            [.. rows.Where(static row => Day(row) is null)
                .OrderBy(static row => row.Item.Title, StringComparer.CurrentCulture)]);
    }

    /// <summary>
    /// The first occurrence from <paramref name="from"/> that nobody has ticked.
    /// </summary>
    /// <remarks>
    /// Skipping the completed ones matters on a rule somebody is ahead on: ticking this Monday
    /// should move the list to next Monday, not leave it showing a Monday that is already done.
    /// </remarks>
    private static AgendaOccurrence? NextOutstanding(
        AgendaItem series,
        IReadOnlyList<AgendaItem> overrides,
        DateOnly from)
    {
        foreach (AgendaOccurrence occurrence in
            AgendaRecurrence.Expand(series, overrides, from, from.AddDays(SearchDays)))
        {
            if (occurrence.Item.Status == AgendaStatus.NeedsAction)
            {
                return occurrence;
            }
        }

        return null;
    }

    /// <summary>The day a row is owed on, or null when it carries none.</summary>
    private static DateOnly? Day(AgendaDayRow row) => row.Falls is { } at
        ? DateOnly.FromDateTime(at.Value)
        : null;

    /// <summary>Soonest first, and within a day the timed ones before the merely owed.</summary>
    private static DateTime Ordering(AgendaDayRow row) =>
        row.At?.Value ?? (row.Falls?.Value.Date ?? DateTime.MaxValue).AddDays(1).AddTicks(-1);

    private static WallClock? Clock(AgendaItem item, WallClock anchor) =>
        item.HasClockTime ? anchor : null;
}
