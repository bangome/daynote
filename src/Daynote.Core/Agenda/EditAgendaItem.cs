namespace Daynote.Core.Agenda;

/// <summary>
/// Writes an edited to-do back over the one it came from (docs/TODOS.md §5).
/// </summary>
/// <remarks>
/// <para>
/// The caller composes <c>edited</c> from its form as if it were new. What makes it an edit rather
/// than a second item is decided here: the id, where it was captured, when it was made, whether it
/// is done and its priority all come from the original, and only what the form shows is taken
/// from the draft.
/// </para>
/// <para>
/// An occurrence of a rule is edited the way it is ticked (<see cref="ToggleAgendaItem"/>): "this
/// one" writes an override, or rewrites the one it already has, and the rule is untouched. "All"
/// rewrites the rule, with the form's day read relative to the occurrence it was opened on, so
/// that opening next Tuesday's occurrence and changing only the title does not move the rule's
/// first day to next Tuesday.
/// </para>
/// </remarks>
public sealed class EditAgendaItem(IAgendaRepository repository, Func<DateTimeOffset>? utcNow = null)
{
    private readonly IAgendaRepository repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Writes <paramref name="edited"/> over <paramref name="row"/> and returns the row as saved.</summary>
    /// <param name="scope">Read only for an occurrence of a rule.</param>
    public async ValueTask<AgendaItem> SaveAsync(
        AgendaDayRow row,
        AgendaItem edited,
        AgendaRepeatScope scope = AgendaRepeatScope.Occurrence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edited);
        DateTimeOffset now = utcNow();

        if (!row.IsOccurrence && !row.Item.IsSeries)
        {
            AgendaItem saved = Keep(row.Item, edited, now);
            await repository.SaveAsync(saved, cancellationToken).ConfigureAwait(false);
            return saved;
        }

        Guid seriesId = row.Item.SeriesId ?? row.Item.Id;
        AgendaItem series = (row.Item.IsSeries
            ? row.Item
            : await repository.GetAsync(seriesId, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidOperationException("The occurrence's series is gone.");

        if (scope == AgendaRepeatScope.Series)
        {
            // The form showed the occurrence's day; the rule keeps counting from its own.
            TimeSpan shift = row.RecurrenceId is { } occurrence && series.Anchor is { } anchor
                ? anchor.Value.Date - occurrence.Value.Date
                : TimeSpan.Zero;
            AgendaItem rule = Keep(series, edited, now) with
            {
                StartsAt = Shift(edited.StartsAt, shift),
                EndsAt = Shift(edited.EndsAt, shift),
                DueAt = Shift(edited.DueAt, shift),
                ExceptionDates = series.ExceptionDates,
            };
            await repository.SaveAsync(rule, cancellationToken).ConfigureAwait(false);
            return rule;
        }

        if (row.RecurrenceId is not { } recurrenceId)
        {
            throw new InvalidOperationException("A repeating to-do is edited one occurrence at a time.");
        }

        AgendaItem original = row.Item.IsOverride ? row.Item : series;
        AgendaItem moved = Keep(original, edited, now) with
        {
            Id = row.Item.IsOverride ? row.Item.Id : Guid.NewGuid(),
            SeriesId = series.Id,
            RecurrenceId = recurrenceId,
            // One occurrence, not a second series.
            Rrule = null,
            ExceptionDates = [],
            // A to-do keeps its clock in DUE and, when the rule anchored on DTSTART, there as
            // well — the same two halves a tick writes (ToggleAgendaItem).
            StartsAt = edited.StartsAt ?? (edited.Kind == AgendaKind.Task && series.StartsAt is not null ? edited.DueAt : null),
            CreatedUtc = row.Item.IsOverride ? row.Item.CreatedUtc : now,
        };
        await repository.SaveAsync(moved, cancellationToken).ConfigureAwait(false);
        return moved;
    }

    /// <summary>The draft's fields on the original's identity.</summary>
    private static AgendaItem Keep(AgendaItem original, AgendaItem edited, DateTimeOffset now) => edited with
    {
        Id = original.Id,
        SeriesId = original.SeriesId,
        RecurrenceId = original.RecurrenceId,
        Status = original.Status,
        CompletedUtc = original.CompletedUtc,
        Priority = original.Priority,
        TimelineVisibility = original.TimelineVisibility,
        SourceNoteId = original.SourceNoteId,
        Zone = original.Zone,
        ExceptionDates = original.ExceptionDates,
        // The alerts someone set stay set; a to-do that gained a day gains the one alert a new
        // dated one starts with, and one that lost its day has nothing left to ring at.
        AlarmLeadMinutes = edited.Anchor is null
            ? AgendaAlert.None
            : original.Anchor is null ? edited.AlarmLeadMinutes : original.AlarmLeadMinutes,
        CreatedUtc = original.CreatedUtc,
        UpdatedUtc = now,
    };

    private static WallClock? Shift(WallClock? clock, TimeSpan by) =>
        clock is { } value ? new WallClock(value.Value + by) : null;
}
