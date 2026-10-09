using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Mobile.Widgets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// What the Android home-screen widgets draw (<see cref="WidgetSnapshot"/>).
/// </summary>
/// <remarks>Fixed "now": Wednesday 7 October 2026, 12:00, the design's own day.</remarks>
[TestClass]
public sealed class WidgetSnapshotTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0);
    private static readonly DateTimeOffset Stamp = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly Guid Work = new("22222222-2222-4222-8222-222222222222");

    private static readonly IReadOnlyList<AgendaList> Lists =
    [
        new(AgendaList.DefaultId, string.Empty, 0, true, Stamp, Stamp),
        new(Work, "업무", 1, false, Stamp, Stamp),
    ];

    [TestMethod]
    public void Rows_run_timed_first_in_time_order_then_the_merely_owed()
    {
        AgendaItem release = Task(1, "릴리즈 노트 작성") with { DueAt = Day(7) };
        AgendaItem logs = Task(2, "퇴근 전 로그 확인") with { DueAt = At(7, 18), HasDueTime = true, ListId = Work };
        AgendaItem room = Task(3, "회의실 예약 확인") with { DueAt = At(7, 14), HasDueTime = true };
        AgendaItem later = Task(4, "내일 할 일") with { DueAt = Day(8) };

        WidgetSnapshot snapshot = WidgetSnapshot.Build(Now, [release, logs, room, later], Lists, AppLanguage.Korean);

        CollectionAssert.AreEqual(
            new[] { "회의실 예약 확인", "퇴근 전 로그 확인", "릴리즈 노트 작성" },
            snapshot.Todos.Select(t => t.Title).ToArray());
        CollectionAssert.AreEqual(new[] { "14:00", "18:00", string.Empty }, snapshot.Todos.Select(t => t.When).ToArray());
        Assert.AreEqual(3, snapshot.Remaining);

        // The ring is the list's: the built-in list first, then the rest in order.
        CollectionAssert.AreEqual(new[] { 0, 1, 0 }, snapshot.Todos.Select(t => t.ListColor).ToArray());
    }

    [TestMethod]
    public void A_day_already_gone_or_a_time_already_past_is_overdue_and_listed_first()
    {
        AgendaItem yesterday = Task(1, "어제 것") with { DueAt = Day(6) };
        AgendaItem morning = Task(2, "아침 것") with { DueAt = At(7, 9), HasDueTime = true };
        AgendaItem evening = Task(3, "저녁 것") with { DueAt = At(7, 19), HasDueTime = true };

        WidgetSnapshot snapshot = WidgetSnapshot.Build(Now, [evening, morning, yesterday], Lists, AppLanguage.Korean);

        CollectionAssert.AreEqual(new[] { "어제 것", "아침 것", "저녁 것" }, snapshot.Todos.Select(t => t.Title).ToArray());
        CollectionAssert.AreEqual(new[] { true, true, false }, snapshot.Todos.Select(t => t.IsOverdue).ToArray());

        // A day gone shows the day, not a clock it may not have had.
        Assert.AreEqual("10/6", snapshot.Todos[0].When);
    }

    [TestMethod]
    public void A_repeating_to_do_is_one_row_marked_as_repeating()
    {
        AgendaItem daily = Task(1, "물 마시기") with
        {
            Rrule = "FREQ=DAILY",
            StartsAt = At(1, 15),
            HasDueTime = true,
        };

        WidgetSnapshot snapshot = WidgetSnapshot.Build(Now, [daily], Lists, AppLanguage.Korean);

        Assert.AreEqual(1, snapshot.Todos.Count);
        Assert.IsTrue(snapshot.Todos[0].IsRepeat);
        Assert.AreEqual("15:00", snapshot.Todos[0].When);
        Assert.AreEqual(new WidgetRowKey(daily.Id, At(7, 15)), snapshot.Todos[0].Key);
    }

    [TestMethod]
    public void A_settling_row_stays_drawn_done_in_its_place_and_does_not_count()
    {
        AgendaItem room = Task(1, "회의실 예약 확인") with { DueAt = At(7, 14), HasDueTime = true };
        AgendaItem logs = Task(2, "퇴근 전 로그 확인") with { DueAt = At(7, 18), HasDueTime = true };
        AgendaItem ticked = room with { Status = AgendaStatus.Completed, CompletedUtc = Stamp };
        var key = new WidgetRowKey(room.Id, null);

        WidgetSnapshot settling = WidgetSnapshot.Build(
            Now, [ticked, logs], Lists, AppLanguage.Korean, [new WidgetSettling(key, new DateOnly(2026, 10, 7))]);
        WidgetSnapshot settled = WidgetSnapshot.Build(Now, [ticked, logs], Lists, AppLanguage.Korean);

        CollectionAssert.AreEqual(new[] { "회의실 예약 확인", "퇴근 전 로그 확인" }, settling.Todos.Select(t => t.Title).ToArray());
        Assert.IsTrue(settling.Todos[0].IsDone);
        Assert.IsFalse(settling.Todos[0].IsOverdue, "A finished row is not overdue, whatever its time.");
        Assert.AreEqual(1, settling.Remaining);

        CollectionAssert.AreEqual(new[] { "퇴근 전 로그 확인" }, settled.Todos.Select(t => t.Title).ToArray());
    }

    [TestMethod]
    public void A_ticked_occurrence_is_found_again_by_its_series_and_start()
    {
        // Ticking an occurrence writes an override with a new id; the key has to survive that.
        AgendaItem daily = Task(1, "물 마시기") with { Rrule = "FREQ=DAILY", StartsAt = At(1, 15), HasDueTime = true };
        AgendaItem done = daily with
        {
            Id = Guid.NewGuid(),
            SeriesId = daily.Id,
            RecurrenceId = At(7, 15),
            Rrule = null,
            StartsAt = At(7, 15),
            DueAt = At(7, 15),
            Status = AgendaStatus.Completed,
            CompletedUtc = Stamp,
        };
        var key = new WidgetRowKey(daily.Id, At(7, 15));

        WidgetSnapshot snapshot = WidgetSnapshot.Build(
            Now, [daily, done], Lists, AppLanguage.Korean, [new WidgetSettling(key, new DateOnly(2026, 10, 7))]);

        Assert.AreEqual(1, snapshot.Todos.Count);
        Assert.IsTrue(snapshot.Todos[0].IsDone);
        Assert.AreEqual(key, snapshot.Todos[0].Key);
        Assert.AreEqual(key, WidgetRowKey.Parse(key.ToString()));
        Assert.AreEqual(new WidgetRowKey(daily.Id, null), WidgetRowKey.Parse(daily.Id.ToString("N")));
        Assert.IsNull(WidgetRowKey.Parse("not-a-key"));
    }

    [TestMethod]
    public void Nothing_owed_is_an_empty_list_and_events_are_never_rows()
    {
        AgendaItem review = Event(1, "디자인 리뷰", At(7, 16), At(7, 17, 30));
        AgendaItem done = Task(2, "끝난 것") with { DueAt = Day(7), Status = AgendaStatus.Completed, CompletedUtc = Stamp };

        WidgetSnapshot snapshot = WidgetSnapshot.Build(Now, [review, done], Lists, AppLanguage.Korean);

        Assert.AreEqual(0, snapshot.Todos.Count);
        Assert.AreEqual(0, snapshot.Remaining);
        Assert.AreEqual(WidgetState.Ready, snapshot.State);
        Assert.AreEqual("디자인 리뷰", snapshot.NextEvent?.Title);
    }

    [TestMethod]
    public void The_next_event_is_the_first_not_yet_over_and_says_its_day_when_not_today()
    {
        AgendaItem over = Event(1, "끝난 회의", At(7, 9), At(7, 10));
        AgendaItem running = Event(2, "진행 중", At(7, 11), At(7, 13));
        AgendaItem tomorrow = Event(3, "내일 회의", At(8, 10), At(8, 11));

        WidgetSnapshot now = WidgetSnapshot.Build(Now, [over, running, tomorrow], Lists, AppLanguage.Korean);
        WidgetSnapshot later = WidgetSnapshot.Build(Now, [over, tomorrow], Lists, AppLanguage.English);

        Assert.AreEqual("진행 중", now.NextEvent?.Title);
        Assert.AreEqual("11:00", now.NextEvent?.When);
        Assert.AreEqual("11:00–13:00", now.NextEvent?.Span);

        // When the card would change on its own: the running event ends at one.
        Assert.AreEqual(new DateTime(2026, 10, 7, 13, 0, 0), now.NextRefresh);

        Assert.AreEqual("Tomorrow · 10:00–11:00", later.NextEvent?.Span);
        Assert.AreEqual(new DateTime(2026, 10, 8), later.NextRefresh);
    }

    [TestMethod]
    public void Locked_shows_the_state_and_nothing_of_the_content()
    {
        WidgetSnapshot snapshot = WidgetSnapshot.Locked(Now, AppLanguage.Korean);

        Assert.AreEqual(WidgetState.Locked, snapshot.State);
        Assert.AreEqual(0, snapshot.Todos.Count);
        Assert.AreEqual(0, snapshot.Remaining);
        Assert.IsNull(snapshot.NextEvent);
    }

    [TestMethod]
    public void The_header_and_week_read_in_the_apps_language()
    {
        WidgetSnapshot korean = WidgetSnapshot.Build(Now, [], Lists, AppLanguage.Korean);
        WidgetSnapshot english = WidgetSnapshot.Build(Now, [], Lists, AppLanguage.English);

        Assert.AreEqual("10월 7일 수요일", korean.DateHeader);
        Assert.AreEqual("Wednesday, Oct 7", english.DateHeader);
        Assert.AreEqual("남은 4", korean.Format("WidgetRemainingFormat", 4));
        Assert.AreEqual("4 left", english.Format("WidgetRemainingFormat", 4));

        CollectionAssert.AreEqual(new[] { 4, 5, 6, 7, 8, 9, 10 }, korean.Week.Select(d => d.Number).ToArray());
        Assert.AreEqual("수", korean.Week[3].Name);
        Assert.IsTrue(korean.Week[3].IsToday);
        Assert.AreEqual("Sun", english.Week[0].Name);
    }

    private static WallClock Day(int day) => new(new DateTime(2026, 10, day));

    private static WallClock At(int day, int hour, int minute = 0) => new(new DateTime(2026, 10, day, hour, minute, 0));

    private static AgendaItem Event(int suffix, string title, WallClock starts, WallClock ends) => Task(suffix, title) with
    {
        Kind = AgendaKind.Event,
        StartsAt = starts,
        EndsAt = ends,
    };

    private static AgendaItem Task(int suffix, string title) => new(
        Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}"),
        AgendaList.DefaultId,
        AgendaKind.Task,
        title,
        string.Empty,
        "Asia/Seoul",
        StartsAt: null,
        EndsAt: null,
        DueAt: null,
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
        AlarmLeadMinutes: [],
        Stamp,
        Stamp);
}
