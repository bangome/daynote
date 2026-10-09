using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// The lists and their counts (design §04 4e, phone §03).
/// </summary>
[TestClass]
public sealed class AgendaListCountsTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 7, 5, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly Guid Work = new("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Personal = new("33333333-3333-4333-8333-333333333333");

    [TestMethod]
    public void The_default_list_is_first_and_is_there_even_at_zero()
    {
        // It is where everything lands when another list is deleted, so a user who has never
        // touched lists still has one.
        IReadOnlyList<AgendaListRow> rows = Rows(
            [List(Work, "업무", 1), Default(), List(Personal, "개인", 2)],
            []);

        CollectionAssert.AreEqual(
            new[] { string.Empty, "업무", "개인" },
            rows.Select(static row => row.List.Name).ToArray());
        Assert.IsTrue(rows[0].IsDefault);
        Assert.AreEqual(0, rows[0].Count);
    }

    [TestMethod]
    public void A_list_counts_what_is_owed_in_it()
    {
        IReadOnlyList<AgendaListRow> rows = Rows(
            [Default(), List(Work, "업무", 1)],
            [
                Todo(1, "내 것", AgendaList.DefaultId),
                Todo(2, "업무 하나", Work),
                Todo(3, "업무 둘", Work),
                Todo(4, "끝난 것", Work) with { Status = AgendaStatus.Completed },
            ]);

        Assert.AreEqual(1, rows.Single(static r => r.IsDefault).Count);
        // Finished to-dos are not owed, so they are in neither the list's count nor the total.
        Assert.AreEqual(2, rows.Single(static r => r.List.Name == "업무").Count);
    }

    [TestMethod]
    public void A_repeating_to_do_counts_once()
    {
        // The same thing the 할 일 tab shows: a rule owes you one thing at a time. Counting rows
        // instead would make a daily rule worth nothing here and everything there.
        AgendaItem series = Todo(1, "주간 보고", Work) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO",
            StartsAt = new WallClock(new DateTime(2026, 10, 12, 7, 0, 0)),
            DueAt = new WallClock(new DateTime(2026, 10, 12, 7, 0, 0)),
        };

        Assert.AreEqual(1, Rows([Default(), List(Work, "업무", 1)], [series])
            .Single(static r => r.List.Name == "업무").Count);
    }

    [TestMethod]
    public void The_total_is_everything_owed_even_in_a_list_this_device_has_not_seen()
    {
        // A to-do whose list is still on its way down from another device is still owed. Summing
        // the rows would quietly drop it, and the two numbers on screen would stop adding up.
        AgendaOutstandingView owed = AgendaOutstanding.For(Today,
        [
            Todo(1, "내 것", AgendaList.DefaultId),
            Todo(2, "모르는 리스트의 것", new Guid("44444444-4444-4444-8444-444444444444")),
        ]);

        IReadOnlyList<AgendaListRow> rows = AgendaListCounts.For([Default()], owed);

        Assert.AreEqual(1, rows.Single().Count);
        Assert.AreEqual(2, AgendaListCounts.Total(owed));
    }

    private static IReadOnlyList<AgendaListRow> Rows(
        IReadOnlyList<AgendaList> lists,
        IReadOnlyList<AgendaItem> items) =>
        AgendaListCounts.For(lists, AgendaOutstanding.For(Today, items));

    private static AgendaList Default() =>
        new(AgendaList.DefaultId, string.Empty, 0, true, Stamp, Stamp);

    private static AgendaList List(Guid id, string name, int order) =>
        new(id, name, order, false, Stamp, Stamp);

    private static AgendaItem Todo(int suffix, string title, Guid list) => new(
        Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}"),
        list,
        AgendaKind.Task,
        title,
        string.Empty,
        "Asia/Seoul",
        StartsAt: null,
        EndsAt: null,
        DueAt: new WallClock(new DateTime(2026, 10, 7, 9, 0, 0)),
        HasDueTime: true,
        Rrule: null,
        SeriesId: null,
        RecurrenceId: null,
        AgendaStatus.NeedsAction,
        CompletedUtc: null,
        Priority: 0,
        TimelineVisibility.Auto,
        SourceNoteId: null,
        ExceptionDates: [],
        AgendaAlert.Default,
        Stamp,
        Stamp);
}
