namespace Daynote.Core.Agenda;

/// <summary>Whether an item is something to finish or something that occupies a span of time.</summary>
/// <remarks>
/// One type rather than two, because the difference is the presence of a time range and every
/// query the day panel and the Timeline make wants both kinds at once. The product calls them
/// 할 일 and 일정; <c>Agenda</c> is the umbrella here only because <c>Tasks</c> as a namespace
/// collides with <see cref="System.Threading.Tasks"/> in every file that touches one.
/// </remarks>
public enum AgendaKind
{
    Task,
    Event,
}

/// <summary>iCalendar's <c>STATUS</c>, reduced to the three values this product can produce.</summary>
public enum AgendaStatus
{
    NeedsAction,
    Completed,
    Cancelled,
}

/// <summary>Whether an item appears in the Timeline.</summary>
/// <remarks>
/// <see cref="Auto"/> means events and one-off tasks yes, recurring tasks no: a daily rule would
/// otherwise be on every day forever and the Timeline would stop being a record of anything. The
/// two overrides exist because the useful answer differs per rule rather than per user, which is
/// why this is not a setting. Not exported — a receiving app has its own view preferences.
/// </remarks>
public enum TimelineVisibility
{
    Auto,
    Always,
    Never,
}

/// <summary>
/// A local wall-clock instant: a date and time with no offset, read against <see cref="AgendaItem.Zone"/>.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="DateTimeOffset"/>. "Every Monday 07:00" repeats a wall-clock time,
/// so across a DST boundary the absolute time has to move for the alarm to stay at 07:00; an
/// instant cannot be turned back into the rule that produced it. This is the same split iCal makes
/// when it writes <c>DTSTART;TZID=Asia/Seoul:20261008T070000</c>. See docs/TODOS.md §6.
/// </remarks>
public readonly record struct WallClock(DateTime Value)
{
    /// <summary>The database and iCal form, to the minute: <c>2026-10-08T07:00</c>.</summary>
    public override string ToString() =>
        Value.ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public static WallClock Parse(string text) =>
        new(DateTime.ParseExact(
            text,
            "yyyy-MM-dd'T'HH:mm",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None));
}

/// <summary>A container for to-dos: a Reminders list, a Todoist project, a CalDAV collection.</summary>
/// <remarks>
/// Not the existing note tags: a tag is per note and a container is per to-do, so they could share
/// the vocabulary but never the table. The default list cannot be deleted, and deleting any other
/// moves its items here rather than taking them along — a container is not a reason to lose a task.
/// </remarks>
public sealed record AgendaList(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsDefault,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    /// <summary>
    /// The id every device gives its built-in list, so two of them created offline merge on sync
    /// instead of leaving the user with two defaults.
    /// </summary>
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-00000000da7e");

    /// <summary>
    /// True when the name is the built-in one and the UI should render it in the current language.
    /// A rename writes a real name and it stops being translated, which is what a rename means.
    /// </summary>
    public bool HasBuiltInName => Name.Length == 0;
}

/// <summary>A to-do or an event. See docs/TODOS.md §3 for the field-by-field interop mapping.</summary>
/// <param name="Zone">An IANA zone id, against which every wall-clock field is read.</param>
/// <param name="Rrule">The recurrence rule as RRULE, or null. Null on an override.</param>
/// <param name="SeriesId">The series this overrides one occurrence of, or null.</param>
/// <param name="RecurrenceId">The original start of the occurrence overridden, or null.</param>
/// <param name="SourceNoteId">
/// Where it was captured, for a one-way jump back. Allowed to dangle: a deleted note must not take
/// tasks with it, so nothing here is a foreign key.
/// </param>
public sealed record AgendaItem(
    Guid Id,
    Guid ListId,
    AgendaKind Kind,
    string Title,
    string Description,
    string Zone,
    WallClock? StartsAt,
    WallClock? EndsAt,
    WallClock? DueAt,
    bool HasDueTime,
    string? Rrule,
    Guid? SeriesId,
    WallClock? RecurrenceId,
    AgendaStatus Status,
    DateTimeOffset? CompletedUtc,
    int Priority,
    TimelineVisibility TimelineVisibility,
    Guid? SourceNoteId,
    IReadOnlyList<WallClock> ExceptionDates,
    IReadOnlyList<int> AlarmLeadMinutes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    /// <summary>True when this row carries a recurrence rule rather than being a single item.</summary>
    public bool IsSeries => Rrule is not null;

    /// <summary>True when this row replaces one occurrence of <see cref="SeriesId"/>.</summary>
    public bool IsOverride => SeriesId is not null;

    /// <summary>
    /// The wall clock everything about this item hangs off: its start, or its deadline when it has
    /// no start. Null when it has neither, which a to-do captured without a date can be.
    /// </summary>
    /// <remarks>
    /// A recurrence rule is read from here, as <c>DTSTART</c> — a task created by the @ command
    /// carries its rule's anchor in <see cref="StartsAt"/> and nothing in <see cref="DueAt"/>, the
    /// way a VTODO with an RRULE does, while a one-off task is the other way round.
    /// </remarks>
    public WallClock? Anchor => StartsAt ?? DueAt;

    /// <summary>
    /// True when a clock was given, rather than only a day.
    /// </summary>
    /// <remarks>
    /// For a to-do the clock always lives in <see cref="DueAt"/>, which is what
    /// <see cref="HasDueTime"/> reports on and what the schema ties it to; a repeating one carries
    /// the same instant in <see cref="StartsAt"/> as well, because that is what its rule anchors
    /// on. An event is the other way round and always has one: a block of time with no time is a
    /// contradiction, and all-day is modelled as a start with no end.
    /// </remarks>
    public bool HasClockTime => Kind == AgendaKind.Event || HasDueTime;

    /// <summary>
    /// Whether this shows in the Timeline, resolving <see cref="TimelineVisibility.Auto"/>.
    /// </summary>
    public bool ShowsInTimeline => TimelineVisibility switch
    {
        TimelineVisibility.Always => true,
        TimelineVisibility.Never => false,
        _ => Kind == AgendaKind.Event || !IsSeries,
    };
}
