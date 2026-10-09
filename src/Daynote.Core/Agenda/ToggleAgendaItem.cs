namespace Daynote.Core.Agenda;

/// <summary>
/// Ticks a to-do, or unticks it (docs/TODOS.md §5).
/// </summary>
/// <remarks>
/// <b>Completing one occurrence of a rule writes an override, not a change to the rule.</b> That
/// is what RFC 5545 intends and what §5 settles: a second row carrying the same series plus the
/// occurrence's original start, holding <c>STATUS:COMPLETED</c> and its <c>COMPLETED</c> stamp.
/// Setting the status on the series itself would mean "I did this every Monday forever", which is
/// the one reading nobody wants.
/// <para>
/// Unticking an occurrence leaves the override in place with its status cleared rather than
/// deleting the row. It may be carrying a moved time as well, and throwing that away because
/// somebody unticked a box would be destroying an edit they did not mention.
/// </para>
/// </remarks>
public sealed class ToggleAgendaItem(IAgendaRepository repository, Func<DateTimeOffset>? utcNow = null)
{
    private readonly IAgendaRepository repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// Flips <paramref name="row"/> and returns the row as it now stands.
    /// </summary>
    public async ValueTask<AgendaItem> ToggleAsync(
        AgendaDayRow row,
        CancellationToken cancellationToken = default)
    {
        bool complete = !row.IsDone;
        DateTimeOffset now = utcNow();

        // A row that already has a row of its own — a one-off, or an occurrence somebody has
        // already touched — is edited where it stands.
        if (!row.Item.IsSeries)
        {
            AgendaItem updated = WithStatus(row.Item, complete, now);
            await repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }

        if (row.RecurrenceId is not { } occurrence)
        {
            // A series with no occurrence behind it should not be reachable from a day panel, but
            // if it is, ticking the rule is still the wrong answer.
            throw new InvalidOperationException("A recurring to-do is completed one occurrence at a time.");
        }

        AgendaItem created = WithStatus(
            row.Item with
            {
                Id = Guid.NewGuid(),
                SeriesId = row.Item.Id,
                RecurrenceId = occurrence,
                // The override carries no rule. It is one occurrence, not a second series.
                Rrule = null,
                ExceptionDates = [],
                // Both halves move with the occurrence. A to-do keeps its clock in DUE — the
                // schema requires it of anything claiming a time — while DTSTART is what the
                // rule anchored on, and an override that disagreed with itself would show one
                // time and remind at another.
                // A to-do with a day and no clock has no At and falls back to the occurrence: the
                // series' own start is its first day, and an override anchored there would leave
                // the day it was ticked on for that one.
                StartsAt = row.At ?? (row.Item.StartsAt is null ? null : occurrence),
                DueAt = row.Item.Kind == AgendaKind.Task
                    ? row.At ?? (row.Item.DueAt is null ? null : occurrence)
                    : row.Item.DueAt,
                CreatedUtc = now,
            },
            complete,
            now);

        await repository.SaveAsync(created, cancellationToken).ConfigureAwait(false);
        return created;
    }

    private static AgendaItem WithStatus(AgendaItem item, bool complete, DateTimeOffset now) => item with
    {
        Status = complete ? AgendaStatus.Completed : AgendaStatus.NeedsAction,
        // Cleared on the way back, so an item ticked, unticked and ticked again carries the stamp
        // of when it was actually finished rather than the first time anyone tried.
        CompletedUtc = complete ? now : null,
        UpdatedUtc = now,
    };
}
