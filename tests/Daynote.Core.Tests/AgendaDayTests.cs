using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// A day's to-do list, as both shells' panels read it (docs/TODOS.md §11, design §04).
/// </summary>
[TestClass]
public sealed class AgendaDayTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 7, 5, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Wednesday = new(2026, 10, 7);
    private static readonly Guid Work = new("22222222-2222-4222-8222-222222222222");

    [TestMethod]
    public void Timed_rows_come_first_in_time_order_then_the_undated_ones()
    {
        // The design's order, and the one a morning reading wants: what is pinned to an hour,
        // then what is simply owed.
        AgendaItem[] items =
        [
            Undated(1, "우유 사기"),
            Timed(2, "회의실 예약 확인", 14, 0),
            Undated(3, "릴리즈 노트 작성"),
            Timed(4, "스트레칭", 7, 0),
        ];

        AgendaDayView day = AgendaDay.For(Wednesday, items);

        CollectionAssert.AreEqual(
            new[] { "스트레칭", "회의실 예약 확인", "릴리즈 노트 작성", "우유 사기" },
            day.Open.Select(static row => row.Item.Title).ToArray());
        Assert.IsNull(day.Open[^1].At);
    }

    [TestMethod]
    public void Finished_rows_go_to_the_collapsed_list_rather_than_disappearing()
    {
        AgendaItem[] items =
        [
            Timed(1, "회의실 예약 확인", 14, 0),
            Timed(2, "지난주 액션아이템 정리", 9, 0) with { Status = AgendaStatus.Completed },
        ];

        AgendaDayView day = AgendaDay.For(Wednesday, items);

        Assert.AreEqual("회의실 예약 확인", day.Open.Single().Item.Title);
        Assert.AreEqual("지난주 액션아이템 정리", day.Done.Single().Item.Title);
    }

    [TestMethod]
    public void A_cancelled_item_is_in_neither_list()
    {
        // Called off is not the same as finished, and a to-do nobody is going to do should not sit
        // in a count of what was achieved.
        AgendaItem[] items = [Timed(1, "취소된 미팅", 11, 0) with { Status = AgendaStatus.Cancelled }];

        AgendaDayView day = AgendaDay.For(Wednesday, items);

        Assert.IsEmpty(day.Open);
        Assert.IsEmpty(day.Done);
    }

    [TestMethod]
    public void A_series_contributes_its_occurrence_rather_than_itself()
    {
        AgendaItem series = Timed(1, "스트레칭", 7, 0) with
        {
            Rrule = "FREQ=DAILY",
            StartsAt = new WallClock(new DateTime(2026, 10, 1, 7, 0, 0)),
            DueAt = null,
        };

        AgendaDayRow row = AgendaDay.For(Wednesday, [series]).Open.Single();

        Assert.IsTrue(row.IsOccurrence);
        Assert.AreEqual(new DateTime(2026, 10, 7, 7, 0, 0), row.At!.Value.Value);
        // The occurrence is identified by the day it belongs to, not by the day the rule started.
        // Ticking it writes an override against this, so "I did it today" does not mean "I did it
        // every day".
        Assert.AreEqual(new DateTime(2026, 10, 7, 7, 0, 0), row.RecurrenceId!.Value.Value);
    }

    [TestMethod]
    public void An_override_is_folded_into_its_occurrence_and_not_counted_twice()
    {
        AgendaItem series = Timed(1, "스트레칭", 7, 0) with
        {
            Rrule = "FREQ=DAILY",
            StartsAt = new WallClock(new DateTime(2026, 10, 1, 7, 0, 0)),
            DueAt = null,
        };
        AgendaItem moved = series with
        {
            Id = Id(2),
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 10, 7, 7, 0, 0)),
            StartsAt = new WallClock(new DateTime(2026, 10, 7, 8, 0, 0)),
            Rrule = null,
            Title = "스트레칭",
        };

        AgendaDayRow row = AgendaDay.For(Wednesday, [series, moved]).Open.Single();

        Assert.AreEqual(8, row.At!.Value.Value.Hour);
    }

    [TestMethod]
    public void An_override_whose_series_was_not_loaded_still_shows_on_its_day()
    {
        // Losing it would be a to-do disappearing because of a loading order the user cannot see.
        AgendaItem orphan = Timed(1, "스트레칭", 8, 0) with
        {
            SeriesId = Id(99),
            RecurrenceId = new WallClock(new DateTime(2026, 10, 7, 7, 0, 0)),
        };

        Assert.HasCount(1, AgendaDay.For(Wednesday, [orphan]).Open);
    }

    [TestMethod]
    public void Another_days_items_are_not_here()
    {
        AgendaItem[] items =
        [
            Timed(1, "오늘 것", 9, 0),
            Timed(2, "내일 것", 9, 0) with { DueAt = new WallClock(new DateTime(2026, 10, 8, 9, 0, 0)) },
        ];

        Assert.AreEqual("오늘 것", AgendaDay.For(Wednesday, items).Open.Single().Item.Title);
    }

    [TestMethod]
    public void A_row_is_overdue_once_its_time_has_gone_by_and_nobody_ticked_it()
    {
        var afternoon = new DateTime(2026, 10, 7, 14, 30, 0);
        AgendaDayView day = AgendaDay.For(Wednesday,
        [
            Timed(1, "지난 것", 9, 0),
            Timed(2, "아직 남은 것", 18, 0),
            Timed(3, "이미 한 것", 9, 0) with { Status = AgendaStatus.Completed },
            Undated(4, "날짜만"),
        ]);

        Assert.IsTrue(day.Open.Single(static r => r.Item.Title == "지난 것").IsOverdue(afternoon));
        Assert.IsFalse(day.Open.Single(static r => r.Item.Title == "아직 남은 것").IsOverdue(afternoon));
        Assert.IsFalse(day.Done.Single().IsOverdue(afternoon));
        // No clock, nothing to be late for. An undated to-do is owed, not overdue.
        Assert.IsFalse(day.Open.Single(static r => r.Item.Title == "날짜만").IsOverdue(afternoon));
    }

    [TestMethod]
    public void Where_a_to_do_came_from_makes_no_difference_to_the_list()
    {
        // §11: this is what someone looks at in the morning, so it must not be split by origin.
        AgendaItem captured = Timed(1, "노트에서", 9, 0) with { SourceNoteId = Id(50) };
        AgendaItem madeInList = Timed(2, "목록에서", 10, 0) with { SourceNoteId = null, ListId = Work };

        AgendaDayView day = AgendaDay.For(Wednesday, [captured, madeInList]);

        Assert.HasCount(2, day.Open);
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    private static AgendaItem Timed(int suffix, string title, int hour, int minute) =>
        Undated(suffix, title) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 7, hour, minute, 0)),
            HasDueTime = true,
        };

    private static AgendaItem Undated(int suffix, string title) => new(
        Id(suffix),
        AgendaList.DefaultId,
        AgendaKind.Task,
        title,
        string.Empty,
        "Asia/Seoul",
        StartsAt: null,
        EndsAt: null,
        DueAt: new WallClock(new DateTime(2026, 10, 7, 0, 0, 0)),
        HasDueTime: false,
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
