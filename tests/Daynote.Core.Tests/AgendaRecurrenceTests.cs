using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// Turning a rule into dates (docs/TODOS.md §5).
/// </summary>
[TestClass]
public sealed class AgendaRecurrenceTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 7, 5, 30, 0, TimeSpan.Zero);
    private static readonly Guid SeriesId = new("11111111-1111-4111-8111-111111111111");

    [TestMethod]
    public void A_daily_rule_lands_on_every_day_of_the_window()
    {
        // 7 October 2026 is a Wednesday, which the whole design set is drawn against.
        AgendaItem series = Series("FREQ=DAILY", new DateTime(2026, 10, 7, 7, 0, 0));

        IReadOnlyList<AgendaOccurrence> found = Expand(series, 7, 10);

        CollectionAssert.AreEqual(
            new[] { 7, 8, 9, 10 },
            found.Select(static o => o.Start.Value.Day).ToArray());
        // The clock comes from DTSTART and stays put: a wall clock repeats, an instant does not.
        Assert.IsTrue(found.All(static o => o.Start.Value.Hour == 7));
    }

    [TestMethod]
    public void A_weekly_rule_with_a_named_day_lands_only_on_it()
    {
        AgendaItem series = Series("FREQ=WEEKLY;BYDAY=MO", new DateTime(2026, 10, 12, 7, 0, 0));

        IReadOnlyList<AgendaOccurrence> found = Expand(series, 7, 31);

        CollectionAssert.AreEqual(
            new[] { 12, 19, 26 },
            found.Select(static o => o.Start.Value.Day).ToArray());
    }

    [TestMethod]
    public void A_weekly_rule_with_no_named_day_repeats_on_the_one_it_started()
    {
        // What both iCalendar and the @ command's bare "매주" mean.
        AgendaItem series = Series("FREQ=WEEKLY", new DateTime(2026, 10, 7, 7, 0, 0));

        CollectionAssert.AreEqual(
            new[] { 7, 14, 21, 28 },
            Expand(series, 1, 31).Select(static o => o.Start.Value.Day).ToArray());
    }

    [TestMethod]
    public void Several_named_days_come_back_in_date_order()
    {
        AgendaItem series = Series("FREQ=WEEKLY;BYDAY=MO,WE,FR", new DateTime(2026, 10, 7, 7, 0, 0));

        CollectionAssert.AreEqual(
            new[] { 7, 9, 12, 14, 16 },
            Expand(series, 7, 16).Select(static o => o.Start.Value.Day).ToArray());
    }

    [TestMethod]
    public void Interval_count_and_until_each_stop_it()
    {
        AgendaItem every = Series("FREQ=DAILY;INTERVAL=3", new DateTime(2026, 10, 7, 7, 0, 0));
        CollectionAssert.AreEqual(
            new[] { 7, 10, 13 },
            Expand(every, 7, 14).Select(static o => o.Start.Value.Day).ToArray());

        AgendaItem thrice = Series("FREQ=DAILY;COUNT=3", new DateTime(2026, 10, 7, 7, 0, 0));
        Assert.HasCount(3, Expand(thrice, 7, 31));

        AgendaItem bounded = Series("FREQ=DAILY;UNTIL=20261009T000000Z", new DateTime(2026, 10, 7, 7, 0, 0));
        Assert.HasCount(3, Expand(bounded, 7, 31));
    }

    [TestMethod]
    public void An_exception_date_removes_the_occurrence_outright()
    {
        // "skip this week" — EXDATE, not a cancelled override.
        AgendaItem series = Series("FREQ=DAILY", new DateTime(2026, 10, 7, 7, 0, 0)) with
        {
            ExceptionDates = [new WallClock(new DateTime(2026, 10, 8, 7, 0, 0))],
        };

        CollectionAssert.AreEqual(
            new[] { 7, 9 },
            Expand(series, 7, 9).Select(static o => o.Start.Value.Day).ToArray());
    }

    [TestMethod]
    public void An_override_replaces_its_occurrence_and_keeps_answering_to_the_original_start()
    {
        // The design's "이번 회차만 변경됨 · 이번만 08:00".
        AgendaItem series = Series("FREQ=DAILY", new DateTime(2026, 10, 7, 7, 0, 0));
        AgendaItem moved = Override(series, new DateTime(2026, 10, 8, 7, 0, 0), new DateTime(2026, 10, 8, 8, 0, 0));

        IReadOnlyList<AgendaOccurrence> found = AgendaRecurrence.Expand(
            series, [moved], new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 8));

        AgendaOccurrence only = found.Single();
        Assert.IsTrue(only.IsOverride);
        Assert.AreEqual(8, only.Start.Value.Hour);
        // RECURRENCE-ID stays the original start for the rest of the occurrence's life; it is how
        // the two devices agree which one was moved.
        Assert.AreEqual(7, only.RecurrenceId.Value.Hour);
    }

    [TestMethod]
    public void An_occurrence_moved_out_of_the_window_leaves_it_and_one_moved_in_appears()
    {
        // Weekly on Wednesdays — 7, 14, 21, 28 — so the 20th is a day the rule leaves empty and
        // anything found there arrived by being moved.
        AgendaItem series = Series("FREQ=WEEKLY", new DateTime(2026, 10, 7, 7, 0, 0));
        AgendaItem pushed = Override(series, new DateTime(2026, 10, 14, 7, 0, 0), new DateTime(2026, 10, 20, 7, 0, 0));

        // Asked for the 14th: the occurrence that was there has been dragged away.
        Assert.IsEmpty(AgendaRecurrence.Expand(
            series, [pushed], new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14)));

        // Asked for the 20th: it is there, even though the rule never put one on that day.
        Assert.HasCount(1, AgendaRecurrence.Expand(
            series, [pushed], new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 20)));
    }

    [TestMethod]
    public void A_rule_this_build_cannot_read_expands_to_nothing_and_says_so()
    {
        // Half-understanding a rule puts occurrences on the wrong days, and a to-do that silently
        // appears on the wrong day is worse than one that visibly does not appear.
        foreach (string unreadable in new[]
        {
            "FREQ=MONTHLY;BYMONTHDAY=1",
            "FREQ=MONTHLY;BYDAY=2MO",
            "FREQ=YEARLY;BYDAY=MO",
            "FREQ=WEEKLY;BYDAY=2MO",
            "FREQ=WEEKLY;BYSETPOS=-1",
            "nonsense",
        })
        {
            Assert.IsFalse(AgendaRecurrence.CanExpand(unreadable), unreadable);
            Assert.IsEmpty(Expand(Series(unreadable, new DateTime(2026, 10, 7, 7, 0, 0)), 1, 31), unreadable);
        }

        Assert.IsTrue(AgendaRecurrence.CanExpand("FREQ=WEEKLY;BYDAY=MO;INTERVAL=2;COUNT=5"));
    }

    [TestMethod]
    public void A_plain_monthly_rule_lands_on_the_anchor_s_day_and_skips_months_without_it()
    {
        // The 31st: November, February, April and June have none, and are skipped rather than
        // moved to their last day.
        AgendaItem series = Series("FREQ=MONTHLY", new DateTime(2026, 10, 31, 9, 0, 0));

        IReadOnlyList<AgendaOccurrence> found = AgendaRecurrence.Expand(
            series, [], new DateOnly(2026, 10, 1), new DateOnly(2027, 7, 31));

        CollectionAssert.AreEqual(
            new[] { new DateTime(2026, 10, 31, 9, 0, 0), new DateTime(2026, 12, 31, 9, 0, 0), new DateTime(2027, 1, 31, 9, 0, 0),
                new DateTime(2027, 3, 31, 9, 0, 0), new DateTime(2027, 5, 31, 9, 0, 0), new DateTime(2027, 7, 31, 9, 0, 0) },
            found.Select(static o => o.Start.Value).ToArray());
        Assert.IsTrue(AgendaRecurrence.CanExpand("FREQ=MONTHLY;INTERVAL=2;COUNT=3"));
    }

    [TestMethod]
    public void A_plain_yearly_rule_lands_once_a_year_and_a_leap_day_only_in_leap_years()
    {
        AgendaItem birthday = Series("FREQ=YEARLY", new DateTime(2026, 10, 7, 0, 0, 0));
        Assert.AreEqual(
            3,
            AgendaRecurrence.Expand(birthday, [], new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31))
                .Count(static o => o.Start.Value is { Month: 10, Day: 7 }));

        AgendaItem leap = Series("FREQ=YEARLY", new DateTime(2028, 2, 29, 0, 0, 0));
        CollectionAssert.AreEqual(
            new[] { 2028, 2032 },
            AgendaRecurrence.Expand(leap, [], new DateOnly(2028, 1, 1), new DateOnly(2033, 12, 31))
                .Select(static o => o.Start.Value.Year).ToArray());
    }

    [TestMethod]
    public void The_next_occurrence_is_what_a_readback_and_an_export_want()
    {
        AgendaItem series = Series("FREQ=WEEKLY;BYDAY=MO", new DateTime(2026, 10, 12, 7, 0, 0));

        AgendaOccurrence? next = AgendaRecurrence.Next(series, [], new DateOnly(2026, 10, 7));

        Assert.IsNotNull(next);
        Assert.AreEqual(new DateTime(2026, 10, 12, 7, 0, 0), next.Value.Start.Value);
    }

    [TestMethod]
    public void An_event_keeps_its_length_on_every_occurrence()
    {
        AgendaItem series = Series("FREQ=DAILY", new DateTime(2026, 10, 7, 7, 0, 0)) with
        {
            Kind = AgendaKind.Event,
            EndsAt = new WallClock(new DateTime(2026, 10, 7, 8, 30, 0)),
        };

        AgendaOccurrence second = Expand(series, 8, 8).Single();

        Assert.AreEqual(new DateTime(2026, 10, 8, 8, 30, 0), second.End!.Value.Value);
    }

    private static IReadOnlyList<AgendaOccurrence> Expand(AgendaItem series, int fromDay, int toDay) =>
        AgendaRecurrence.Expand(
            series, [], new DateOnly(2026, 10, fromDay), new DateOnly(2026, 10, toDay));

    private static AgendaItem Series(string rrule, DateTime start) => new(
        SeriesId,
        AgendaList.DefaultId,
        AgendaKind.Task,
        "스트레칭",
        string.Empty,
        "Asia/Seoul",
        StartsAt: new WallClock(start),
        EndsAt: null,
        DueAt: null,
        HasDueTime: false,
        rrule,
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

    private static AgendaItem Override(AgendaItem series, DateTime occurrence, DateTime moved) =>
        series with
        {
            Id = Guid.NewGuid(),
            SeriesId = series.Id,
            RecurrenceId = new WallClock(occurrence),
            Rrule = null,
            StartsAt = new WallClock(moved),
        };
}
