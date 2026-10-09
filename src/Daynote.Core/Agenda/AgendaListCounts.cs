namespace Daynote.Core.Agenda;

/// <summary>One list, and how much is still owed in it (design §04 4e, phone §03).</summary>
public readonly record struct AgendaListRow(AgendaList List, int Count)
{
    public bool IsDefault => List.IsDefault;
}

/// <summary>
/// The lists as the sidebar and the phone's chip row show them.
/// </summary>
/// <remarks>
/// <b>Counted from the outstanding view, not from the raw rows.</b> The phone's §03 says the
/// number in a chip is the same number the desktop sidebar shows, and the only way to be sure of
/// that is to count the same thing: one entry per to-do that is still owed, a repeating one
/// counted once as its next occurrence. Counting rows instead would make a daily rule worth
/// nothing on one screen and everything on the other.
/// <para>
/// The default list comes first and is never absent, even at zero: it is where everything lands
/// when another list is deleted, so a user who has never used lists still has one.
/// </para>
/// </remarks>
public static class AgendaListCounts
{
    public static IReadOnlyList<AgendaListRow> For(
        IReadOnlyList<AgendaList> lists,
        AgendaOutstandingView owed)
    {
        ArgumentNullException.ThrowIfNull(lists);

        Dictionary<Guid, int> counts = owed.Today
            .Concat(owed.Later)
            .Concat(owed.Undated)
            .GroupBy(static row => row.Item.ListId)
            .ToDictionary(static group => group.Key, static group => group.Count());

        return
        [
            .. lists
                .OrderByDescending(static list => list.IsDefault)
                .ThenBy(static list => list.SortOrder)
                .ThenBy(static list => list.Name, StringComparer.CurrentCulture)
                .Select(list => new AgendaListRow(list, counts.GetValueOrDefault(list.Id))),
        ];
    }

    /// <summary>
    /// Everything owed, whichever list it is in — the sidebar's "할 일 17" and the phone's 전체
    /// chip.
    /// </summary>
    /// <remarks>
    /// Not the sum of the rows above. A to-do whose list this device has not heard of yet still
    /// counts, because it is still owed; the sum would quietly drop it and the two numbers on
    /// screen would stop adding up.
    /// </remarks>
    public static int Total(AgendaOutstandingView owed) => owed.Count;
}
