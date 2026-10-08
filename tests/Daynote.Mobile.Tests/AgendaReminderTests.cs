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
    public void A_task_with_no_alarm_set_still_reminds_and_an_event_does_not()
    {
        // Parity with the behaviour every dated to-do has had since reminders shipped, and with
        // every row the §8 migration writes — it sets no alarms. An event is the other way round:
        // a block of time is not something to be nagged about unless the user asked.
        AgendaItem task = Task(1) with
        {
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };
        AgendaItem meeting = Task(2) with
        {
            Kind = AgendaKind.Event,
            StartsAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            DueAt = null,
        };

        Assert.HasCount(1, ReminderPlanner.Plan([task, meeting], Now, 10, Nine));
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
    public void A_repeating_task_does_not_remind_yet_but_an_override_of_one_does()
    {
        // Expanding RRULE into occurrences is its own piece of work (§5) and is not built. Said
        // out loud here rather than left to be discovered: a weekly to-do is silent until it is.
        AgendaItem series = Task(1) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=FR",
            DueAt = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            HasDueTime = true,
        };
        AgendaItem moved = Task(2) with
        {
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 10, 3, 14, 0, 0)),
            DueAt = new WallClock(new DateTime(2026, 10, 3, 16, 0, 0)),
            HasDueTime = true,
        };

        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan([series, moved], Now, 10, Nine);

        Assert.HasCount(1, plan);
        Assert.AreEqual(new DateTime(2026, 10, 3, 16, 0, 0), plan[0].At);
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
            ReminderPlanner.IdFor(Guid.NewGuid(), "회의자료 공유", 0),
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
    public void A_to_do_that_was_never_captured_from_a_note_still_reminds()
    {
        // One made in the list view has no source note. The tap then selects the day and opens no
        // editor, which is the honest outcome — there is nothing behind it to open.
        AgendaItem item = Task(1) with
        {
            SourceNoteId = null,
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
        AlarmLeadMinutes: [],
        Now,
        Now);
}
