namespace Daynote.Core.Agenda;

/// <summary>
/// Reads and writes to-dos, events and the lists that hold them.
/// </summary>
/// <remarks>
/// Nothing calls this yet. The panels still parse <c>-[ ]</c> out of note bodies; docs/TODOS.md §12
/// requires the readers on desktop and phone to cut over in one release, so the store lands first
/// and unused.
/// <para>
/// Occurrences of a recurring rule are <b>not</b> stored and are not returned here: expanding a
/// rule over a window is the caller's job, and only exceptions and per-occurrence overrides are
/// persisted. A query for a date therefore returns the rows anchored to it, not everything a rule
/// would produce on it.
/// </para>
/// </remarks>
public interface IAgendaRepository
{
    /// <summary>Every list, default first, then by sort order.</summary>
    ValueTask<IReadOnlyList<AgendaList>> GetListsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a list. The caller never supplies <c>is_default</c>: there is exactly one default and
    /// it is created by the migration.
    /// </summary>
    ValueTask<AgendaList> CreateListAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Renames a list. Renaming the default stops its name being translated.</summary>
    ValueTask<AgendaList?> RenameListAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a list and moves its items to the default, which is itself undeletable. Returns how
    /// many items moved, or null when the list does not exist.
    /// </summary>
    ValueTask<int?> DeleteListAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One item with its exception dates and alarms, or null.</summary>
    ValueTask<AgendaItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The items anchored to a local date: tasks due or starting on it, and events starting on it.
    /// Overrides are included; the series rows they belong to are not.
    /// </summary>
    ValueTask<IReadOnlyList<AgendaItem>> GetForDateAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every row carrying a recurrence rule, so a caller can expand them over whatever window it
    /// is drawing. There is no date filter because a rule is not anchored to one.
    /// </summary>
    ValueTask<IReadOnlyList<AgendaItem>> GetSeriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Everything in one list, for the cross-date view.</summary>
    ValueTask<IReadOnlyList<AgendaItem>> GetForListAsync(
        Guid listId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or replaces an item together with its exception dates and alarms, in one
    /// transaction. Replacing is how an edit is applied: the row is small and a partial update
    /// would need a separate path for each field.
    /// </summary>
    ValueTask SaveAsync(AgendaItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an item. Deleting a series takes its overrides with it, which is what the user means
    /// by deleting a repeating task.
    /// </summary>
    ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
