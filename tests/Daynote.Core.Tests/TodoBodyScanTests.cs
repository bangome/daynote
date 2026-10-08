using Daynote.Core.Agenda;
using Daynote.Core.Domain;

namespace Daynote.Core.Tests;

/// <summary>
/// The <c>-[ ]</c> grammar and the derived ids the one-time migration depends on
/// (docs/TODOS.md §8).
/// </summary>
[TestClass]
public sealed class TodoBodyScanTests
{
    private static readonly Guid Note = new("11111111-1111-4111-8111-111111111111");
    private static readonly LocalDate Date = LocalDate.Parse("2026-08-20").Value;

    [TestMethod]
    public void Every_shape_the_panel_has_been_showing_is_found()
    {
        IReadOnlyList<ScannedTodo> found = TodoBodyScan.Scan(
            Note,
            Date,
            """
            오늘 회의 메모
            - [ ] 예산안 초안
            -[x] 영수증 정리
              - [X]  들여쓴 것도 (8/25)
            - [ ] 시간까지 (8/25 14:30)
            그냥 문장 - [ ] 줄 가운데는 아님
            """);

        CollectionAssert.AreEqual(
            new[] { "예산안 초안", "영수증 정리", "들여쓴 것도", "시간까지" },
            found.Select(static todo => todo.Text).ToArray());

        Assert.IsFalse(found[0].Completed);
        Assert.IsTrue(found[1].Completed);
        Assert.IsTrue(found[2].Completed);
    }

    [TestMethod]
    public void A_due_stamp_takes_its_year_from_the_note_it_was_written_in()
    {
        // "(8/25)" carries no year, and the note is far better evidence of which one was meant
        // than today is — these are historical lines by the time the migration reads them.
        ScannedTodo todo = TodoBodyScan.Scan(Note, Date, "- [ ] 보고서 (8/25)").Single();

        Assert.AreEqual(new WallClock(new DateTime(2026, 8, 25, 23, 59, 0)), todo.DueAt);
        Assert.IsFalse(todo.HasDueTime);

        ScannedTodo timed = TodoBodyScan.Scan(Note, Date, "- [ ] 보고서 (8/25 14:30)").Single();
        Assert.AreEqual(new WallClock(new DateTime(2026, 8, 25, 14, 30, 0)), timed.DueAt);
        Assert.IsTrue(timed.HasDueTime);
    }

    [TestMethod]
    public void Something_that_only_looks_like_a_due_stamp_stays_in_the_title()
    {
        // The user wrote it. Dropping "(13/40)" because it failed to parse as a date would be the
        // app deciding part of their sentence was a mistake.
        ScannedTodo todo = TodoBodyScan.Scan(Note, Date, "- [ ] 비율 확인 (13/40)").Single();

        Assert.AreEqual("비율 확인 (13/40)", todo.Text);
        Assert.IsNull(todo.DueAt);
    }

    [TestMethod]
    public void A_bare_checkbox_is_formatting_not_a_task()
    {
        Assert.AreEqual(0, TodoBodyScan.Scan(Note, Date, "- []\n- [ ]   \n").Count);
    }

    [TestMethod]
    public void The_same_line_in_the_same_note_derives_the_same_id_on_every_device()
    {
        // The whole reason ids are derived. Two devices run this migration offline against bodies
        // that already synced; random ids would give the user every task twice.
        Guid first = TodoBodyScan.Scan(Note, Date, "- [ ] 예산안 초안").Single().Id;
        Guid second = TodoBodyScan.Scan(Note, Date, "- [ ] 예산안 초안").Single().Id;

        Assert.AreEqual(first, second);

        // Pinned, not just self-consistent: a change to the derivation would re-migrate every
        // task on the next device to upgrade, and this fails first. The value is also what
        // Python's uuid.uuid5 produces for the same namespace and name, which is the check that
        // the big-endian handling is right rather than merely consistent with itself.
        Assert.AreEqual("4f74ab1e-c80f-5adc-8e06-f6583c2e9fbe", first.ToString());
    }

    [TestMethod]
    public void Two_identical_lines_in_one_note_stay_two_tasks()
    {
        IReadOnlyList<ScannedTodo> found = TodoBodyScan.Scan(Note, Date, "- [ ] 전화\n- [ ] 전화");

        Assert.AreEqual(2, found.Count);
        // Keyed by how many identical texts precede it, so genuine repeats survive while a line
        // moving up or down the note does not change its id.
        Assert.AreNotEqual(found[0].Id, found[1].Id);
    }

    [TestMethod]
    public void Moving_a_line_within_the_note_does_not_change_its_id()
    {
        Guid before = TodoBodyScan.Scan(Note, Date, "- [ ] 첫째\n- [ ] 둘째")[1].Id;
        Guid after = TodoBodyScan.Scan(Note, Date, "머리말\n- [ ] 둘째\n- [ ] 첫째")[0].Id;

        Assert.AreEqual(before, after);
    }

    [TestMethod]
    public void A_different_note_with_the_same_line_is_a_different_task()
    {
        Guid mine = TodoBodyScan.Scan(Note, Date, "- [ ] 전화").Single().Id;
        Guid theirs = TodoBodyScan
            .Scan(new Guid("22222222-2222-4222-8222-222222222222"), Date, "- [ ] 전화")
            .Single().Id;

        Assert.AreNotEqual(mine, theirs);
    }
}
