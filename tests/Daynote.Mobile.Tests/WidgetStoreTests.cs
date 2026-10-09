using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Settings;
using Daynote.Mobile.Reminders;
using Daynote.Mobile.Widgets;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The widgets and the reminder receiver against a real profile folder with the app not running:
/// a tick is a set rather than a flip, a finished row takes its reminders with it, a reminder asks
/// the store before it is shown, and a database the app has not migrated yet is left alone.
/// </summary>
[TestClass]
public sealed class WidgetStoreTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(9));

    /// <summary>Tomorrow at 14:00, so the reminder is ahead whenever the suite runs.</summary>
    private static readonly DateTime Due = DateTime.Today.AddDays(1).AddHours(14);

    private string _root = string.Empty;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "daynote-widget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteRoot()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public async Task Finishing_a_row_takes_its_reminder_out_and_a_second_tap_does_not_reopen_it()
    {
        AgendaItem item = Task(1, "보고서") with { DueAt = new WallClock(Due), HasDueTime = true };
        await SeedAsync(item);
        Reminder planned = PlanAndRecord([item]).Single();
        var key = new WidgetRowKey(item.Id, null);
        DateOnly day = DateOnly.FromDateTime(Due);

        WidgetTick tick = await WidgetData.SetDoneAsync(_root, null, key, day, complete: true);

        Assert.IsTrue(tick.Changed);
        CollectionAssert.AreEqual(new[] { planned.Id }, tick.CancelledReminders.ToArray());
        Assert.AreEqual(0, ReminderStateStore.InFolder(_root).Load().Scheduled.Count);
        Assert.AreEqual(AgendaStatus.Completed, (await ReadAsync(item.Id))?.Status);

        // A widget drawn before the tick still offers "finish": it must not flip the row back.
        WidgetTick stale = await WidgetData.SetDoneAsync(_root, null, key, day, complete: true);

        Assert.IsFalse(stale.Changed);
        Assert.AreEqual(AgendaStatus.Completed, (await ReadAsync(item.Id))?.Status);

        WidgetTick reopened = await WidgetData.SetDoneAsync(_root, null, key, day, complete: false);

        Assert.IsTrue(reopened.Changed);
        Assert.AreEqual(0, reopened.CancelledReminders.Count);
        Assert.AreEqual(AgendaStatus.NeedsAction, (await ReadAsync(item.Id))?.Status);
    }

    [TestMethod]
    public async Task Finishing_an_occurrence_takes_out_that_occurrences_reminder_only()
    {
        AgendaItem daily = Task(1, "물 마시기") with
        {
            Rrule = "FREQ=DAILY",
            // A repeating to-do keeps its clock in both: the rule anchors on DTSTART, the schema
            // ties a time to DUE.
            StartsAt = new WallClock(Due),
            DueAt = new WallClock(Due),
            HasDueTime = true,
        };
        await SeedAsync(daily);
        IReadOnlyList<Reminder> planned = PlanAndRecord([daily]);
        var occurrence = new WallClock(Due);

        WidgetTick tick = await WidgetData.SetDoneAsync(
            _root, null, new WidgetRowKey(daily.Id, occurrence), DateOnly.FromDateTime(Due), complete: true);

        Assert.AreEqual(1, tick.CancelledReminders.Count);
        Assert.AreEqual(ReminderPlanner.IdFor(daily.Id, occurrence, 0), tick.CancelledReminders[0]);
        Assert.AreEqual(planned.Count - 1, ReminderStateStore.InFolder(_root).Load().Scheduled.Count);
    }

    [TestMethod]
    public async Task A_reminder_is_shown_only_while_planning_the_store_would_still_make_it()
    {
        AgendaItem item = Task(1, "보고서") with { DueAt = new WallClock(Due), HasDueTime = true };
        await SeedAsync(item);
        Reminder planned = PlanAndRecord([item]).Single();

        Assert.IsTrue(await ReminderGate.ShouldNotifyAsync(_root, null, planned.Id, Due));

        // Finished somewhere that did not reconcile: the alarm is still armed and still recorded.
        await using (SqliteDatabase database = Open())
        {
            await new SqliteAgendaRepository(database).SaveAsync(
                item with { Status = AgendaStatus.Completed, CompletedUtc = Stamp });
        }

        Assert.IsFalse(await ReminderGate.ShouldNotifyAsync(_root, null, planned.Id, Due));
    }

    [TestMethod]
    public async Task Reminders_switched_off_are_not_shown_and_an_unreadable_store_lets_them_through()
    {
        Assert.IsTrue(
            await ReminderGate.ShouldNotifyAsync(_root, null, "todo-0000000000000000", Due),
            "No database: a reminder is better than none.");

        AgendaItem item = Task(1, "보고서") with { DueAt = new WallClock(Due), HasDueTime = true };
        await SeedAsync(item);
        Reminder planned = PlanAndRecord([item]).Single();
        await using (SqliteDatabase database = Open())
        {
            await new SqliteSettingsStore(database, new SystemClock()).SetAsync(ReminderCoordinator.EnabledKey, "off");
        }

        Assert.IsFalse(await ReminderGate.ShouldNotifyAsync(_root, null, planned.Id, Due));
    }

    [TestMethod]
    public async Task A_database_the_app_has_not_migrated_yet_is_not_migrated_by_the_widget()
    {
        AgendaItem item = Task(1, "보고서") with { DueAt = new WallClock(Due), HasDueTime = true };
        await SeedAsync(item);
        await using (SqliteDatabase database = Open())
        {
            await database.WriteAsync(static (c, t, _) =>
            {
                using SqliteCommand command = c.CreateCommand();
                command.Transaction = t;
                command.CommandText = "DELETE FROM schema_versions WHERE version = (SELECT MAX(version) FROM schema_versions);";
                return command.ExecuteNonQuery();
            });
        }

        WidgetSnapshot snapshot = await WidgetData.ReadAsync(_root, null, Due.AddHours(-1), []);
        WidgetTick tick = await WidgetData.SetDoneAsync(
            _root, null, new WidgetRowKey(item.Id, null), DateOnly.FromDateTime(Due), complete: true);

        Assert.AreEqual(WidgetState.Outdated, snapshot.State);
        Assert.AreEqual(0, snapshot.Todos.Count);
        Assert.IsFalse(tick.Changed);
        Assert.IsFalse(new SqliteDatabase(new SqliteDatabaseOptions(DatabasePath)).TryOpenCurrent(), "Still behind.");
    }

    [TestMethod]
    public async Task A_current_database_reads_in_its_own_language()
    {
        AgendaItem item = Task(1, "보고서") with { DueAt = new WallClock(Due), HasDueTime = true };
        await SeedAsync(item);

        WidgetSnapshot snapshot = await WidgetData.ReadAsync(_root, null, Due.AddHours(-1), []);

        Assert.AreEqual(WidgetState.Ready, snapshot.State);
        Assert.AreEqual(AppLanguage.Korean, snapshot.Language);
        Assert.AreEqual("보고서", snapshot.Todos.Single().Title);
    }

    private string DatabasePath => Path.Combine(_root, "daynote.db");

    private SqliteDatabase Open()
    {
        var database = new SqliteDatabase(new SqliteDatabaseOptions(DatabasePath));
        database.Initialize();
        return database;
    }

    private async Task SeedAsync(AgendaItem item)
    {
        await using SqliteDatabase database = Open();
        await new SqliteAgendaRepository(database).SaveAsync(item);
        await new SqliteSettingsStore(database, new SystemClock()).SetAsync("ui.language", "ko");
    }

    private async Task<AgendaItem?> ReadAsync(Guid id)
    {
        await using SqliteDatabase database = Open();
        return await new SqliteAgendaRepository(database).GetAsync(id);
    }

    /// <summary>Plans as the app would and writes the result where the app keeps it.</summary>
    private IReadOnlyList<Reminder> PlanAndRecord(IReadOnlyList<AgendaItem> items)
    {
        IReadOnlyList<Reminder> planned = ReminderPlanner.Plan(
            items, DateTimeOffset.Now, 64, ReminderPlanner.DefaultDateOnlyTime);
        ReminderStateStore.InFolder(_root).Save(ReminderState.Empty with { Scheduled = planned });
        return planned;
    }

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
        AlarmLeadMinutes: AgendaAlert.Default,
        Stamp,
        Stamp);
}
