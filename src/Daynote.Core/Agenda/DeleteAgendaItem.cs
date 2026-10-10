namespace Daynote.Core.Agenda;

/// <summary>How much of a repeating to-do a delete or an edit takes.</summary>
public enum AgendaRepeatScope
{
    /// <summary>The one occurrence: an EXDATE on the rule, and its override if it had one.</summary>
    Occurrence,

    /// <summary>The rule and every override of it.</summary>
    Series,
}

/// <summary>
/// What a delete took away, exactly enough to put back (<see cref="DeleteAgendaItem.RestoreAsync"/>).
/// </summary>
/// <param name="Removed">The rows deleted, a series before its overrides.</param>
/// <param name="SeriesId">The series an EXDATE was added to, or null when none was.</param>
/// <param name="ExceptionDate">The EXDATE added, or null.</param>
public sealed record AgendaDeletion(
    IReadOnlyList<AgendaItem> Removed,
    Guid? SeriesId,
    WallClock? ExceptionDate);

/// <summary>
/// Deletes a to-do, or one occurrence of a repeating one, and undoes it (docs/TODOS.md §5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deleting one occurrence writes an EXDATE, not a delete.</b> The occurrence was never a row;
/// it is what the rule generates, so the only way to make the rule stop generating it is to tell
/// the rule. An override the occurrence had — a tick, a moved time — goes with it.
/// </para>
/// <para>
/// <b>Undo stamps what it puts back with now.</b> A delete leaves a tombstone, and sync orders a
/// tombstone against an edit by time alone (last write wins, on the Worker and on every device).
/// A row restored with the <c>updated_utc</c> it had before the delete is older than its own
/// tombstone, and a device that had already pushed the delete would refuse the restore.
/// </para>
/// </remarks>
public sealed class DeleteAgendaItem(IAgendaRepository repository, Func<DateTimeOffset>? utcNow = null)
{
    private readonly IAgendaRepository repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// Deletes <paramref name="row"/>. <paramref name="scope"/> is read only for an occurrence of a
    /// rule; a one-off is simply deleted.
    /// </summary>
    public async ValueTask<AgendaDeletion> DeleteAsync(
        AgendaDayRow row,
        AgendaRepeatScope scope = AgendaRepeatScope.Occurrence,
        CancellationToken cancellationToken = default)
    {
        if (!row.IsOccurrence && !row.Item.IsSeries)
        {
            await repository.DeleteAsync(row.Item.Id, cancellationToken).ConfigureAwait(false);
            return new AgendaDeletion([row.Item], SeriesId: null, ExceptionDate: null);
        }

        Guid seriesId = row.Item.SeriesId ?? row.Item.Id;
        AgendaItem? series = row.Item.IsSeries
            ? row.Item
            : await repository.GetAsync(seriesId, cancellationToken).ConfigureAwait(false);

        if (scope == AgendaRepeatScope.Series || row.RecurrenceId is not { } occurrence || series is null)
        {
            // The overrides go with the series (the cascade, and a tombstone each); they are read
            // first so undo can put them back.
            IReadOnlyList<AgendaItem> all = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            AgendaItem[] overrides = [.. all.Where(item => item.SeriesId == seriesId)];
            await repository.DeleteAsync(series?.Id ?? row.Item.Id, cancellationToken).ConfigureAwait(false);
            return new AgendaDeletion(
                series is null ? [row.Item] : [series, .. overrides],
                SeriesId: null,
                ExceptionDate: null);
        }

        await repository.SaveAsync(
            series with
            {
                ExceptionDates = [.. series.ExceptionDates.Where(date => date != occurrence), occurrence],
                UpdatedUtc = utcNow(),
            },
            cancellationToken).ConfigureAwait(false);

        if (row.Item.IsOverride)
        {
            await repository.DeleteAsync(row.Item.Id, cancellationToken).ConfigureAwait(false);
            return new AgendaDeletion([row.Item], series.Id, occurrence);
        }

        return new AgendaDeletion([], series.Id, occurrence);
    }

    /// <summary>Puts back what <paramref name="deletion"/> took, stamped now so it wins over the tombstones.</summary>
    public async ValueTask RestoreAsync(AgendaDeletion deletion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        DateTimeOffset now = utcNow();

        if (deletion is { SeriesId: { } seriesId, ExceptionDate: { } date } &&
            await repository.GetAsync(seriesId, cancellationToken).ConfigureAwait(false) is { } series)
        {
            await repository.SaveAsync(
                series with
                {
                    ExceptionDates = [.. series.ExceptionDates.Where(other => other != date)],
                    UpdatedUtc = now,
                },
                cancellationToken).ConfigureAwait(false);
        }

        // In the order they were read: a series before the overrides that point at it.
        foreach (AgendaItem item in deletion.Removed)
        {
            await repository.SaveAsync(item with { UpdatedUtc = now }, cancellationToken).ConfigureAwait(false);
        }
    }
}
