using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// The 할 일 tab's cross-date list (design §04, 4a).
/// </summary>
[TestClass]
public sealed class AgendaOutstandingTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 7, 5, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 7);

    [TestMethod]
    public void Owed_today_comes_first_then_what_is_ahead_then_what_has_no_day()
    {
        AgendaItem[] items =
        [
            Dated(1, "온보딩 문서 정리", null),
            Dated(2, "스테이징 회귀 테스트", new DateTime(2026, 10, 9, 14, 0, 0)),
            Dated(3, "회의자료 초안 공유", new DateTime(2026, 10, 7, 9, 0, 0)),
        ];

        AgendaOutstandingView view = AgendaOutstanding.For(Today, items);

        Assert.AreEqual("회의자료 초안 공유", view.Today.Single().Item.Title);
        Assert.AreEqual("스테이징 회귀 테스트", view.Later.Single().Item.Title);
        Assert.AreEqual("온보딩 문서 정리", view.Undated.Single().Item.Title);
        Assert.AreEqual(3, view.Count);
    }

    [TestMethod]
    public void Something_owed_from_last_week_is_still_owed_today()
    {
        // Not a fourth group: a to-do that slipped is the first thing you want at the top, not a
        // separate shelf to ignore.
        AgendaItem late = Dated(1, "속도 개선 이슈 티켓 생성", new DateTime(2026, 10, 6, 9, 0, 0));

        Assert.AreEqual("속도 개선 이슈 티켓 생성", AgendaOutstanding.For(Today, [late]).Today.Single().Item.Title);
    }

    [TestMethod]
    public void A_finished_item_is_absent_rather_than_collapsed()
    {
        // The day panel keeps finished rows behind a count because the day is a record of itself.
        // This list is a queue, and a queue of things already done is not a queue.
        AgendaItem done = Dated(1, "끝난 것", new DateTime(2026, 10, 7, 9, 0, 0))
            with { Status = AgendaStatus.Completed };

        Assert.AreEqual(0, AgendaOutstanding.For(Today, [done]).Count);
    }

    [TestMethod]
    public void A_repeating_to_do_appears_once_as_its_next_occurrence()
    {
        // The design's "주간 보고 작성 · 10/12 07:00". Listing every occurrence would bury
        // everything else under one daily rule, and a rule owes you one thing at a time.
        AgendaItem series = Dated(1, "주간 보고 작성", new DateTime(2026, 10, 5, 7, 0, 0)) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO",
            StartsAt = new WallClock(new DateTime(2026, 10, 5, 7, 0, 0)),
            DueAt = null,
        };

        AgendaOutstandingView view = AgendaOutstanding.For(Today, [series]);

        AgendaDayRow row = view.Later.Single();
        Assert.AreEqual(new DateTime(2026, 10, 12, 7, 0, 0), row.At!.Value.Value);
        Assert.AreEqual(1, view.Count);
    }

    [TestMethod]
    public void Ticking_this_weeks_occurrence_moves_the_row_to_next_week()
    {
        // The reason the search skips completed occurrences rather than taking the first one:
        // being ahead on a routine should move the list on, not leave it showing a day you have
        // already dealt with.
        AgendaItem series = Dated(1, "주간 보고 작성", new DateTime(2026, 10, 5, 7, 0, 0)) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO",
            StartsAt = new WallClock(new DateTime(2026, 10, 5, 7, 0, 0)),
            DueAt = null,
        };
        AgendaItem ticked = series with
        {
            Id = Id(2),
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 10, 12, 7, 0, 0)),
            Rrule = null,
            Status = AgendaStatus.Completed,
            CompletedUtc = Stamp,
        };

        AgendaDayRow row = AgendaOutstanding.For(Today, [series, ticked]).Later.Single();

        Assert.AreEqual(new DateTime(2026, 10, 19, 7, 0, 0), row.At!.Value.Value);
    }

    [TestMethod]
    public void A_rule_that_has_run_out_contributes_nothing()
    {
        AgendaItem finished = Dated(1, "끝난 반복", new DateTime(2026, 10, 5, 7, 0, 0)) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO;UNTIL=20261006T000000Z",
            StartsAt = new WallClock(new DateTime(2026, 10, 5, 7, 0, 0)),
            DueAt = null,
        };

        Assert.AreEqual(0, AgendaOutstanding.For(Today, [finished]).Count);
    }

    [TestMethod]
    public void Within_a_day_the_timed_rows_come_before_the_merely_owed()
    {
        AgendaItem[] items =
        [
            Dated(1, "그날 안에", new DateTime(2026, 10, 9, 0, 0, 0)) with { HasDueTime = false },
            Dated(2, "9일 14시", new DateTime(2026, 10, 9, 14, 0, 0)),
            Dated(3, "10일", new DateTime(2026, 10, 10, 9, 0, 0)),
        ];

        CollectionAssert.AreEqual(
            new[] { "9일 14시", "그날 안에", "10일" },
            AgendaOutstanding.For(Today, items).Later.Select(static r => r.Item.Title).ToArray());
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    private static AgendaItem Dated(int suffix, string title, DateTime? due) => new(
        Id(suffix),
        AgendaList.DefaultId,
        AgendaKind.Task,
        title,
        string.Empty,
        "Asia/Seoul",
        StartsAt: null,
        EndsAt: null,
        DueAt: due is { } at ? new WallClock(at) : null,
        HasDueTime: due is not null,
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
