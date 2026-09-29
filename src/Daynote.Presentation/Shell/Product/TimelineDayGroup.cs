namespace Daynote.App.Shell.Product;

/// <summary>
/// One day of the timeline as the desktop design lays it out: the date and its note count in a column
/// on the left, that day's note cards beside it (Daynote Desktop B, <c>tlGroups</c>).
/// </summary>
/// <remarks>
/// Built from <see cref="TimelineViewModel.Rows"/> rather than read separately, so paging, expansion
/// and the localized headings stay the timeline's own; this is only a different grouping of the same
/// rows. The WPF shell keeps drawing the flat list.
/// </remarks>
public sealed class TimelineDayGroup
{
    public TimelineDayGroup(TimelineDateHeaderRow header, IReadOnlyList<TimelineNoteRow> notes)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
    }

    public TimelineDateHeaderRow Header { get; }

    public Core.Domain.LocalDate Date => Header.Date;

    public string Heading => Header.Heading;

    public string CountText => Header.CountText;

    public IReadOnlyList<TimelineNoteRow> Notes { get; }
}

/// <summary>Folds the timeline's flat header-then-cards rows into per-day groups.</summary>
public static class TimelineGrouping
{
    /// <summary>
    /// Each header opens a group and the cards after it join it, in order. Cards before any header —
    /// which the timeline never produces — are dropped rather than invented a date for.
    /// </summary>
    public static IReadOnlyList<TimelineDayGroup> Group(IEnumerable<TimelineRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var groups = new List<TimelineDayGroup>();
        TimelineDateHeaderRow? header = null;
        List<TimelineNoteRow> notes = [];
        foreach (TimelineRow row in rows)
        {
            switch (row)
            {
                case TimelineDateHeaderRow next:
                    if (header is not null)
                    {
                        groups.Add(new TimelineDayGroup(header, notes));
                    }

                    header = next;
                    notes = [];
                    break;
                case TimelineNoteRow note when header is not null:
                    notes.Add(note);
                    break;
            }
        }

        if (header is not null)
        {
            groups.Add(new TimelineDayGroup(header, notes));
        }

        return groups;
    }
}
