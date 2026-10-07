using Daynote.Core.Agenda;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Agenda;

/// <summary>
/// The to-do and event store (docs/TODOS.md). Nothing in the app reads it yet, so these tests are
/// the only thing holding the schema's promises: the single default list, the constraints that say
/// what a series and an override may be, and that a wall-clock time survives a round trip unchanged.
/// </summary>
[TestClass]
public sealed class SqliteAgendaRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task The_migration_leaves_exactly_one_default_list()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        IReadOnlyList<AgendaList> lists = await repository.GetListsAsync();

        Assert.AreEqual(1, lists.Count);
        Assert.IsTrue(lists[0].IsDefault);
        Assert.AreEqual(AgendaList.DefaultId, lists[0].Id);

        // Empty rather than "할 일": the UI renders the built-in name in the current language, and
        // a rename is what makes it stop being translated.
        Assert.IsTrue(lists[0].HasBuiltInName);
    }

    [TestMethod]
    public async Task A_second_default_list_is_refused_by_the_schema()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();

        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO agenda_lists(id,name,sort_order,is_default,created_utc,updated_utc) " +
            "VALUES ('11111111-1111-1111-1111-111111111111','Second',1,1,'x','x');";

        // The constraint is in the schema rather than in whichever writer remembers it: the MCP
        // server shares this database and will not remember.
        Assert.ThrowsExactly<SqliteException>(() => command.ExecuteNonQuery());
    }

    [TestMethod]
    public async Task Deleting_a_list_moves_its_items_to_the_default()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        AgendaList work = await repository.CreateListAsync(Guid.NewGuid(), "업무");
        await repository.SaveAsync(Task("보고서", work.Id));

        int? moved = await repository.DeleteListAsync(work.Id);

        Assert.AreEqual(1, moved);
        Assert.AreEqual(1, (await repository.GetListsAsync()).Count);

        IReadOnlyList<AgendaItem> remaining = await repository.GetForListAsync(AgendaList.DefaultId);
        Assert.AreEqual(1, remaining.Count, "The task was deleted with its list.");
        Assert.AreEqual("보고서", remaining[0].Title);
    }

    [TestMethod]
    public async Task The_default_list_cannot_be_deleted()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        // It is where everything else lands, so there would be nowhere to put its contents.
        Assert.IsNull(await repository.DeleteListAsync(AgendaList.DefaultId));
        Assert.AreEqual(1, (await repository.GetListsAsync()).Count);
    }

    [TestMethod]
    public async Task A_wall_clock_time_round_trips_without_acquiring_an_offset()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        AgendaItem saved = Task("운동", AgendaList.DefaultId) with
        {
            Zone = "Asia/Seoul",
            DueAt = WallClock.Parse("2026-10-08T07:00"),
            HasDueTime = true,
        };
        await repository.SaveAsync(saved);

        AgendaItem? read = await repository.GetAsync(saved.Id);

        Assert.IsNotNull(read);
        Assert.AreEqual("2026-10-08T07:00", read.DueAt?.ToString());
        Assert.AreEqual("Asia/Seoul", read.Zone);

        // The point of the type: 07:00 is 07:00 whatever the machine's clock is set to, so a
        // weekly rule stays at 07:00 across a DST boundary.
        Assert.AreEqual(DateTimeKind.Unspecified, read.DueAt!.Value.Value.Kind);
    }

    [TestMethod]
    public async Task Exception_dates_and_alarms_are_replaced_rather_than_accumulated()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        AgendaItem series = Task("운동", AgendaList.DefaultId) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO",
            DueAt = WallClock.Parse("2026-10-12T07:00"),
            HasDueTime = true,
            ExceptionDates = [WallClock.Parse("2026-10-19T07:00")],
            AlarmLeadMinutes = [15, 60],
        };
        await repository.SaveAsync(series);

        await repository.SaveAsync(series with
        {
            ExceptionDates = [WallClock.Parse("2026-10-26T07:00")],
            AlarmLeadMinutes = [0],
        });

        AgendaItem? read = await repository.GetAsync(series.Id);

        Assert.IsNotNull(read);
        CollectionAssert.AreEqual(
            new[] { "2026-10-26T07:00" },
            read.ExceptionDates.Select(static date => date.ToString()).ToArray());
        CollectionAssert.AreEqual(new[] { 0 }, read.AlarmLeadMinutes.ToArray());
    }

    [TestMethod]
    public async Task Deleting_a_series_takes_its_overrides_with_it()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        AgendaItem series = Task("운동", AgendaList.DefaultId) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO",
            DueAt = WallClock.Parse("2026-10-12T07:00"),
            HasDueTime = true,
        };
        await repository.SaveAsync(series);

        // One occurrence completed: the override is how per-occurrence state is recorded, so that
        // "this Monday done, next Monday not" has somewhere to live.
        AgendaItem done = Task("운동", AgendaList.DefaultId) with
        {
            SeriesId = series.Id,
            RecurrenceId = WallClock.Parse("2026-10-12T07:00"),
            Status = AgendaStatus.Completed,
            CompletedUtc = Now,
        };
        await repository.SaveAsync(done);

        Assert.IsTrue(await repository.DeleteAsync(series.Id));
        Assert.IsNull(await repository.GetAsync(done.Id), "The override outlived its series.");
    }

    [TestMethod]
    public async Task One_override_per_occurrence()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        AgendaItem series = Task("운동", AgendaList.DefaultId) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO",
            DueAt = WallClock.Parse("2026-10-12T07:00"),
            HasDueTime = true,
        };
        await repository.SaveAsync(series);

        AgendaItem first = Task("운동", AgendaList.DefaultId) with
        {
            SeriesId = series.Id,
            RecurrenceId = WallClock.Parse("2026-10-12T07:00"),
        };
        await repository.SaveAsync(first);

        AgendaItem clash = first with { Id = Guid.NewGuid() };

        await Assert.ThrowsExactlyAsync<SqliteException>(
            async () => await repository.SaveAsync(clash));
    }

    [TestMethod]
    public async Task A_series_cannot_also_be_an_override()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        AgendaItem series = Task("운동", AgendaList.DefaultId) with { Rrule = "FREQ=DAILY" };
        await repository.SaveAsync(series);

        AgendaItem confused = Task("운동", AgendaList.DefaultId) with
        {
            Rrule = "FREQ=DAILY",
            SeriesId = series.Id,
            RecurrenceId = WallClock.Parse("2026-10-12T07:00"),
        };

        await Assert.ThrowsExactlyAsync<SqliteException>(
            async () => await repository.SaveAsync(confused));
    }

    [TestMethod]
    public async Task An_event_must_have_a_start()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        // An event is the kind with a span; without a start there is nothing to draw.
        AgendaItem bad = Task("회의", AgendaList.DefaultId) with { Kind = AgendaKind.Event };

        await Assert.ThrowsExactlyAsync<SqliteException>(async () => await repository.SaveAsync(bad));
    }

    [TestMethod]
    public async Task A_date_query_returns_what_is_anchored_to_it()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        await repository.SaveAsync(Task("오늘 할 일", AgendaList.DefaultId) with
        {
            DueAt = WallClock.Parse("2026-10-08T23:59"),
        });
        await repository.SaveAsync(Task("회의", AgendaList.DefaultId) with
        {
            Kind = AgendaKind.Event,
            StartsAt = WallClock.Parse("2026-10-08T14:00"),
            EndsAt = WallClock.Parse("2026-10-08T15:00"),
        });
        await repository.SaveAsync(Task("다른 날", AgendaList.DefaultId) with
        {
            DueAt = WallClock.Parse("2026-10-09T10:00"),
        });

        IReadOnlyList<AgendaItem> day = await repository.GetForDateAsync(new DateOnly(2026, 10, 8));

        CollectionAssert.AreEqual(
            new[] { "회의", "오늘 할 일" },
            day.Select(static item => item.Title).ToArray(),
            "Expected the day's two items in time order.");
    }

    [TestMethod]
    public async Task Series_are_listed_separately_because_a_rule_has_no_date()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);

        await repository.SaveAsync(Task("단발", AgendaList.DefaultId) with
        {
            DueAt = WallClock.Parse("2026-10-08T10:00"),
        });
        await repository.SaveAsync(Task("매주", AgendaList.DefaultId) with
        {
            Rrule = "FREQ=WEEKLY;BYDAY=MO",
            DueAt = WallClock.Parse("2026-10-12T07:00"),
        });

        IReadOnlyList<AgendaItem> series = await repository.GetSeriesAsync();

        Assert.AreEqual(1, series.Count);
        Assert.AreEqual("매주", series[0].Title);
        Assert.IsTrue(series[0].IsSeries);
    }

    [TestMethod]
    public void Auto_hides_a_recurring_task_and_shows_everything_else()
    {
        AgendaItem once = Task("단발", AgendaList.DefaultId);
        AgendaItem repeating = once with { Rrule = "FREQ=DAILY" };
        AgendaItem meeting = once with
        {
            Kind = AgendaKind.Event,
            StartsAt = WallClock.Parse("2026-10-08T14:00"),
        };

        // A daily rule would otherwise be on every day forever, and the Timeline would stop being
        // a record of anything.
        Assert.IsTrue(once.ShowsInTimeline);
        Assert.IsFalse(repeating.ShowsInTimeline);
        Assert.IsTrue(meeting.ShowsInTimeline);
        Assert.IsTrue((repeating with { TimelineVisibility = TimelineVisibility.Always }).ShowsInTimeline);
        Assert.IsFalse((meeting with { TimelineVisibility = TimelineVisibility.Never }).ShowsInTimeline);
    }

    private static IAgendaRepository Repository(TestDatabase fixture) =>
        new SqliteAgendaRepository(fixture.Database, () => Now);

    private static AgendaItem Task(string title, Guid listId) => new(
        Guid.NewGuid(),
        listId,
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
        Now,
        Now);
}
