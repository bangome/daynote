using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Mobile.Reminders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Reminders planned from to-do entities instead of <c>-[ ]</c> lines (docs/TODOS.md §12 step 4).
/// </summary>
/// <remarks>
/// Nothing calls this path yet — the coordinator still reads note bodies — so these tests are the
/// only thing holding it to its contract until step 3 switches the readers over.
/// <para>
/// Fixed "now": 2 October 2026 10:00 in Seoul, as <see cref="ReminderTests"/> uses, so a due date
/// written as 10/3 is tomorrow whenever the suite runs.
/// </para>
/// </remarks>
[TestClass]
public sealed class AgendaReminderTests
{
    private static readonly TimeSpan Seoul = TimeSpan.FromHours(9);
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 10, 0, 0, Seoul);
    private static readonly TimeSpan Nine = new(9, 0, 0);
    private static readonly Guid Work = new("22222222-2222-4222-8222-222222222222");

    [TestMethod]
    public void A_due_date_with_a_time_reminds_at_that_time_and_a_date_alone_at_nine()
    {
        // The two rules FireTime has always had, unchanged by the move to entities.
        AgendaItem timed = Task(1) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };
        AgendaItem dated = Task(2) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 4, 0, 0, 0)),
            HasDueTime = false,
        };

        Assert.AreEqual(new DateTime(2026, 10, 3, 14, 0, 0), ReminderPlanner.FireTime(timed, Nine));
        Assert.AreEqual(new DateTime(2026, 10, 4, 9, 0, 0), ReminderPlanner.FireTime(dated, Nine));
    }

    [TestMethod]
    public void A_tasks_start_is_a_day_not_a_clock_reading()
    {
        // What the §8 migration writes: DTSTART at midnight of the note's date and no DUE. Taking
        // that midnight literally would remind everyone at 00:00.
        AgendaItem migrated = Task(1) with
        {
            StartsAt = new WallClock(new DateTime(2026, 10, 3, 0, 0, 0)),
            DueAt = null,
        };

        Assert.AreEqual(new DateTime(2026, 10, 3, 9, 0, 0), ReminderPlanner.FireTime(migrated, Nine));
    }

    [TestMethod]
    public void An_events_start_is_taken_literally()
    {
        AgendaItem meeting = Task(1) with
        {
            Kind = AgendaKind.Event,
            StartsAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            DueAt = null,
        };

        Assert.AreEqual(new DateTime(2026, 10, 3, 14, 0, 0), ReminderPlanner.FireTime(meeting, Nine));
    }

    [TestMethod]
    public void An_alarm_lead_is_subtracted_which_is_what_the_old_comment_promised()
    {
        AgendaItem item = Task(1) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };

        Assert.AreEqual(
            new DateTime(2026, 10, 3, 13, 30, 0),
            ReminderPlanner.FireTime(item, Nine, leadMinutes: 30));
    }

    [TestMethod]
    public void Two_alarms_on_one_item_are_two_notifications()
    {
        AgendaItem item = Task(1) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
            AlarmLeadMinutes = [0, 30],
        };

        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan([item], Now, 10, Nine);

        Assert.HasCount(2, plan);
        Assert.AreEqual(new DateTime(2026, 10, 3, 13, 30, 0), plan[0].At);
        Assert.AreEqual(new DateTime(2026, 10, 3, 14, 0, 0), plan[1].At);
        // Distinct ids, or the second would replace the first on both platforms.
        Assert.AreNotEqual(plan[0].Id, plan[1].Id);
        // The same thing is happening at 14:00 either way: a reminder that comes early still has
        // to say when the thing actually is.
        Assert.AreEqual(plan[0].Body, plan[1].Body);
    }

    [TestMethod]
    public void An_item_with_no_alert_is_silent()
    {
        // This used to be the opposite for a to-do: an empty list meant "the usual single alert",
        // because the product had nowhere to say otherwise. Mobile §06 gives it somewhere — a
        // dated item is created carrying one alert and the user can remove it — so the planner
        // reads what the item says instead of guessing what it meant.
        AgendaItem silenced = Dated(1) with { AlarmLeadMinutes = AgendaAlert.None };

        Assert.IsEmpty(ReminderPlanner.Plan([silenced], Now, 10, Nine));
        Assert.HasCount(1, ReminderPlanner.Plan([Dated(1)], Now, 10, Nine));
    }

    [TestMethod]
    public void A_completed_or_past_item_is_not_planned()
    {
        AgendaItem done = Task(1) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
            Status = AgendaStatus.Completed,
        };
        AgendaItem past = Task(2) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 1, 14, 0, 0)),
            HasDueTime = true,
        };

        Assert.IsEmpty(ReminderPlanner.Plan([done, past], Now, 10, Nine));
    }

    [TestMethod]
    public void A_repeating_task_reminds_on_every_occurrence_in_the_horizon()
    {
        AgendaItem series = Repeating("FREQ=WEEKLY;BYDAY=FR", new DateTime(2026, 10, 3, 14, 0, 0));

        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan([series], Now, 10, Nine);

        // Friday 3 October is behind "now"; the rule carries on weekly from there.
        CollectionAssert.AreEqual(
            new[]
            {
                new DateTime(2026, 10, 9, 14, 0, 0),
                new DateTime(2026, 10, 16, 14, 0, 0),
                new DateTime(2026, 10, 23, 14, 0, 0),
                new DateTime(2026, 10, 30, 14, 0, 0),
            },
            plan.Take(4).Select(static r => r.At).ToArray());
    }

    [TestMethod]
    public void A_repeating_task_rings_at_its_own_hour_and_not_the_default()
    {
        // The reading that was wrong until the expander was turned on. A repeating to-do keeps
        // its clock in DTSTART, because that is what an RRULE anchors on; looking for it in DUE
        // found none and quietly moved every one of them to nine in the morning.
        AgendaItem weekly = Repeating("FREQ=WEEKLY;BYDAY=MO", new DateTime(2026, 10, 5, 7, 0, 0));

        Assert.AreEqual(new DateTime(2026, 10, 5, 7, 0, 0), ReminderPlanner.FireTime(weekly, Nine));
    }

    [TestMethod]
    public void A_repeating_task_with_no_clock_still_reminds_at_the_default_hour()
    {
        AgendaItem weekly = Repeating("FREQ=WEEKLY;BYDAY=MO", new DateTime(2026, 10, 5, 0, 0, 0))
            with
            { HasDueTime = false };

        Assert.AreEqual(new DateTime(2026, 10, 5, 9, 0, 0), ReminderPlanner.FireTime(weekly, Nine));
    }

    [TestMethod]
    public void An_override_replaces_its_occurrence_rather_than_adding_one()
    {
        AgendaItem series = Repeating("FREQ=WEEKLY;BYDAY=FR", new DateTime(2026, 10, 3, 14, 0, 0));
        AgendaItem moved = series with
        {
            Id = Id(2),
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 10, 9, 14, 0, 0)),
            StartsAt = new WallClock(new DateTime(2026, 10, 9, 16, 0, 0)),
            Rrule = null,
        };

        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan([series, moved], Now, 10, Nine);

        // One reminder for that Friday, at the moved hour — not two, and not the original.
        Assert.AreEqual(new DateTime(2026, 10, 9, 16, 0, 0), plan[0].At);
        Assert.AreEqual(1, plan.Count(static r => r.At.Day == 9));
        // Keyed on the series and the original start, so moving an occurrence replaces its
        // notification rather than leaving the old one scheduled beside it.
        Assert.AreEqual(
            ReminderPlanner.IdFor(series.Id, new WallClock(new DateTime(2026, 10, 9, 14, 0, 0)), 0),
            plan[0].Id);
    }

    [TestMethod]
    public void A_skipped_occurrence_is_silent()
    {
        AgendaItem series = Repeating("FREQ=WEEKLY;BYDAY=FR", new DateTime(2026, 10, 3, 14, 0, 0)) with
        {
            ExceptionDates = [new WallClock(new DateTime(2026, 10, 9, 14, 0, 0))],
        };

        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan([series], Now, 10, Nine);

        Assert.IsEmpty(plan.Where(static r => r.At.Day == 9));
    }

    [TestMethod]
    public void A_rule_this_build_cannot_read_is_silent_rather_than_wrong()
    {
        // AgendaRecurrence refuses a monthly rule that names its day rather than guessing. A to-do that silently
        // reminds on the wrong day is worse than one that visibly does not remind at all.
        AgendaItem monthly = Repeating("FREQ=MONTHLY;BYMONTHDAY=3", new DateTime(2026, 10, 3, 14, 0, 0));

        Assert.IsEmpty(ReminderPlanner.Plan([monthly], Now, 10, Nine));
    }

    [TestMethod]
    public void An_override_whose_series_was_not_loaded_still_reminds()
    {
        // An orphan is a data problem, not a reason to stop telling the user about their to-do.
        AgendaItem orphan = Dated(1) with
        {
            SeriesId = Id(99),
            RecurrenceId = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
        };

        Assert.HasCount(1, ReminderPlanner.Plan([orphan], Now, 10, Nine));
    }

    [TestMethod]
    public void The_id_follows_the_item_not_its_text()
    {
        // The whole reason for the move. Renaming a to-do used to cancel its reminder and schedule
        // a different one, because the id was a hash of the text.
        AgendaItem item = Task(1) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };

        string before = ReminderPlanner.Plan([item], Now, 10, Nine)[0].Id;
        string after = ReminderPlanner.Plan([item with { Title = "다른 제목" }], Now, 10, Nine)[0].Id;

        Assert.AreEqual(before, after);
        // Still the shape the heads parse: RequestCodeFor reads the last eight hex digits.
        StringAssert.StartsWith(before, "todo-");
        Assert.AreEqual(21, before.Length);
    }

    [TestMethod]
    public void The_cutover_cancels_every_old_reminder_and_schedules_the_new_ones()
    {
        // Ids change from a hash of the note and the line's text to a hash of the item's id. Diff
        // needs no special case for that: the old ids are simply absent from the new set.
        var old = new Reminder(
            // What the retired note-line planner named one: "todo-" and a hash of note and text.
            "todo-0123456789abcdef",
            new DateTime(2026, 10, 3, 14, 0, 0),
            "회의자료 공유",
            "주간회의 · 10/3 14:00",
            LocalDate.Parse("2026-10-03").Value,
            Guid.NewGuid());

        AgendaItem item = Task(1) with
        {
            Title = "회의자료 공유",
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };
        IReadOnlyList<Reminder> desired = ReminderPlanner.Plan([item], Now, 10, Nine);

        ReminderChanges changes = ReminderPlanner.Diff([old], desired, "채널", "설명");

        CollectionAssert.AreEqual(new[] { old.Id }, changes.Cancel.ToArray());
        Assert.HasCount(1, changes.Schedule);
        Assert.AreNotEqual(old.Id, changes.Schedule[0].Id);
    }

    [TestMethod]
    public void A_non_default_list_names_itself_in_the_body_and_the_built_in_one_does_not()
    {
        AgendaItem filed = Task(1) with
        {
            ListId = Work,
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };
        AgendaItem loose = Task(2) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };
        var names = new Dictionary<Guid, string> { [Work] = "업무", [AgendaList.DefaultId] = string.Empty };

        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan([filed, loose], Now, 10, Nine, names);

        Assert.AreEqual("업무 · 10/3 14:00", plan.Single(r => r.Title == "할 일 1").Body);
        Assert.AreEqual("10/3 14:00", plan.Single(r => r.Title == "할 일 2").Body);
    }

    [TestMethod]
    public void A_to_dos_reminder_names_no_note_even_when_it_was_captured_from_one()
    {
        // A migrated to-do still stores the note its line was in, but a to-do is not linked to a
        // note any more: the tap selects the day and opens no note editor.
        AgendaItem item = Task(1) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };

        Reminder planned = ReminderPlanner.Plan([item], Now, 10, Nine).Single();

        Assert.AreEqual(Guid.Empty, planned.NoteId);
        Assert.AreEqual(LocalDate.Parse("2026-10-03").Value, planned.Date);
    }

    [TestMethod]
    public void The_nearest_ones_win_when_the_platform_cannot_hold_them_all()
    {
        AgendaItem[] items =
        [
            Task(1) with { DueAt = new WallClock(new DateTime(2026, 10, 5, 9, 0, 0)), HasDueTime = true },
            Task(2) with { DueAt = new WallClock(new DateTime(2026, 10, 3, 9, 0, 0)), HasDueTime = true },
            Task(3) with { DueAt = new WallClock(new DateTime(2026, 10, 4, 9, 0, 0)), HasDueTime = true },
        ];

        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan(items, Now, 2, Nine);

        Assert.HasCount(2, plan);
        Assert.AreEqual(new DateTime(2026, 10, 3, 9, 0, 0), plan[0].At);
        Assert.AreEqual(new DateTime(2026, 10, 4, 9, 0, 0), plan[1].At);
    }

    [TestMethod]
    public void A_to_do_s_reminder_names_the_row_its_done_action_completes()
    {
        // Apple Watch design §05: 완료 on the notification completes the to-do without the app, so
        // the reminder carries the item, and for a rule the occurrence, the way a widget row does.
        Reminder single = ReminderPlanner.Plan([Dated(1)], Now, 10, Nine).Single();
        Assert.AreEqual(Id(1), single.ItemId);
        Assert.IsNull(single.Occurrence);

        Reminder occurrence = ReminderPlanner.Plan(
            [Repeating("FREQ=DAILY", new DateTime(2026, 10, 3, 9, 0, 0))], Now, 1, Nine).Single();
        Assert.AreEqual(Id(1), occurrence.ItemId, "An occurrence names its series.");
        Assert.AreEqual("2026-10-03T09:00", occurrence.Occurrence);
    }

    [TestMethod]
    public void A_snoozed_reminder_stays_only_while_its_to_do_is_open()
    {
        // The notification's 30분 뒤 다시 is a copy the planner never made, so the planner's diff
        // cannot withdraw it; this is the question the head's sweep asks instead (review M4).
        AgendaItem single = Dated(1);
        Assert.IsTrue(ReminderPlanner.IsStillOpen([single], single.Id, null));
        Assert.IsFalse(ReminderPlanner.IsStillOpen([single with { Status = AgendaStatus.Completed }], single.Id, null));
        Assert.IsFalse(ReminderPlanner.IsStillOpen([], single.Id, null), "Deleted.");

        AgendaItem rule = Repeating("FREQ=DAILY", new DateTime(2026, 10, 3, 9, 0, 0));
        const string Monday = "2026-10-05T09:00";
        Assert.IsTrue(ReminderPlanner.IsStillOpen([rule], rule.Id, Monday));
        Assert.IsFalse(ReminderPlanner.IsStillOpen(
            [rule with { ExceptionDates = [WallClock.Parse(Monday)] }], rule.Id, Monday), "Skipped.");
        AgendaItem ticked = rule with
        {
            Id = Id(9), Rrule = null, SeriesId = rule.Id, RecurrenceId = WallClock.Parse(Monday),
            Status = AgendaStatus.Completed,
        };
        Assert.IsFalse(ReminderPlanner.IsStillOpen([rule, ticked], rule.Id, Monday), "That occurrence was ticked.");
        Assert.IsTrue(ReminderPlanner.IsStillOpen([rule, ticked], rule.Id, "2026-10-06T09:00"), "The next one was not.");
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    /// <summary>A repeating to-do, anchored on DTSTART the way a VTODO with an RRULE is.</summary>
    private static AgendaItem Repeating(string rrule, DateTime anchor) => Task(1) with
    {
        Rrule = rrule,
        StartsAt = new WallClock(anchor),
        DueAt = null,
        HasDueTime = true,
        AlarmLeadMinutes = AgendaAlert.Default,
    };

    /// <summary>A to-do that is due and carries the one alert a new one is created with.</summary>
    private static AgendaItem Dated(int suffix) => Task(suffix) with
    {
        DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
        HasDueTime = true,
        AlarmLeadMinutes = AgendaAlert.Default,
    };

    private static AgendaItem Task(int suffix) => new(
        Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}"),
        AgendaList.DefaultId,
        AgendaKind.Task,
        $"할 일 {suffix}",
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
        SourceNoteId: Guid.Parse("33333333-3333-4333-8333-333333333333"),
        ExceptionDates: [],
        AgendaAlert.Default,
        Now,
        Now);
}
