using Daynote.Core.Agenda;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Agenda;

/// <summary>
/// Ticking a to-do, against a real database (docs/TODOS.md §5).
/// </summary>
[TestClass]
public sealed class ToggleAgendaItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 5, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Wednesday = new(2026, 10, 7);

    [TestMethod]
    public async Task A_one_off_to_do_is_ticked_where_it_stands()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem item = await fixture.SaveAsync(OneOff());

        AgendaItem done = await fixture.Toggle.ToggleAsync(Row(item));

        Assert.AreEqual(AgendaStatus.Completed, done.Status);
        Assert.AreEqual(item.Id, done.Id);
        Assert.AreEqual(AgendaStatus.Completed, (await fixture.Agenda.GetAsync(item.Id))!.Status);
        // One row, not two: nothing about a one-off needs an override.
        Assert.HasCount(1, await fixture.Agenda.GetAllAsync());
    }

    [TestMethod]
    public async Task Unticking_clears_the_completion_stamp()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem item = await fixture.SaveAsync(OneOff());

        AgendaItem done = await fixture.Toggle.ToggleAsync(Row(item));
        Assert.IsNotNull(done.CompletedUtc);

        AgendaItem open = await fixture.Toggle.ToggleAsync(Row(done));

        // Cleared on the way back, so an item ticked, unticked and ticked again carries the stamp
        // of when it was actually finished rather than the first time anyone tried.
        Assert.AreEqual(AgendaStatus.NeedsAction, open.Status);
        Assert.IsNull(open.CompletedUtc);
    }

    [TestMethod]
    public async Task Ticking_one_occurrence_writes_an_override_and_leaves_the_rule_alone()
    {
        // The one reading nobody wants is "I did this every Monday forever".
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem series = await fixture.SaveAsync(Series());
        AgendaDayRow row = AgendaDay.For(Wednesday, [series]).Open.Single();

        AgendaItem created = await fixture.Toggle.ToggleAsync(row);

        Assert.AreNotEqual(series.Id, created.Id);
        Assert.AreEqual(series.Id, created.SeriesId);
        Assert.AreEqual(row.RecurrenceId, created.RecurrenceId);
        Assert.AreEqual(AgendaStatus.Completed, created.Status);
        // The override is one occurrence, not a second series.
        Assert.IsNull(created.Rrule);
        Assert.AreEqual(AgendaStatus.NeedsAction, (await fixture.Agenda.GetAsync(series.Id))!.Status);
    }

    [TestMethod]
    public async Task The_ticked_occurrence_moves_to_the_done_list_and_the_others_do_not()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem series = await fixture.SaveAsync(Series());
        await fixture.Toggle.ToggleAsync(AgendaDay.For(Wednesday, [series]).Open.Single());

        IReadOnlyList<AgendaItem> all = await fixture.Agenda.GetAllAsync();

        Assert.HasCount(1, AgendaDay.For(Wednesday, all).Done);
        Assert.IsEmpty(AgendaDay.For(Wednesday, all).Open);
        // Tomorrow is untouched: one occurrence was finished, not the routine.
        Assert.HasCount(1, AgendaDay.For(Wednesday.AddDays(1), all).Open);
    }

    [TestMethod]
    public async Task Unticking_an_occurrence_keeps_the_override_rather_than_deleting_it()
    {
        // It may be carrying a moved time as well, and throwing that away because somebody
        // unticked a box would be destroying an edit they did not mention.
        await using Fixture fixture = await Fixture.CreateAsync();
        AgendaItem series = await fixture.SaveAsync(Series());
        AgendaItem created = await fixture.Toggle.ToggleAsync(AgendaDay.For(Wednesday, [series]).Open.Single());

        IReadOnlyList<AgendaItem> all = await fixture.Agenda.GetAllAsync();
        AgendaItem reopened = await fixture.Toggle.ToggleAsync(AgendaDay.For(Wednesday, all).Done.Single());

        Assert.AreEqual(created.Id, reopened.Id);
        Assert.AreEqual(AgendaStatus.NeedsAction, reopened.Status);
        Assert.HasCount(2, await fixture.Agenda.GetAllAsync());
    }

    private static AgendaDayRow Row(AgendaItem item) => new(item, null, item.DueAt);

    private static AgendaItem OneOff() => Item() with
    {
        DueAt = new WallClock(new DateTime(2026, 10, 7, 14, 0, 0)),
        HasDueTime = true,
    };

    /// <summary>
    /// A repeating to-do as the @ command stores one: DTSTART anchors the rule, DUE carries the
    /// clock. Writing only DTSTART fails the schema's own check, which is how the first version
    /// of this was caught.
    /// </summary>
    private static AgendaItem Series() => Item() with
    {
        Rrule = "FREQ=DAILY",
        StartsAt = new WallClock(new DateTime(2026, 10, 1, 7, 0, 0)),
        DueAt = new WallClock(new DateTime(2026, 10, 1, 7, 0, 0)),
        HasDueTime = true,
    };

    private static AgendaItem Item() => new(
        Guid.NewGuid(),
        AgendaList.DefaultId,
        AgendaKind.Task,
        "스트레칭",
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
        AgendaAlert.Default,
        Now,
        Now);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TestDatabase database;

        private Fixture(TestDatabase database, SqliteAgendaRepository agenda)
        {
            this.database = database;
            Agenda = agenda;
            Toggle = new ToggleAgendaItem(agenda, () => Now);
        }

        internal SqliteAgendaRepository Agenda { get; }

        internal ToggleAgendaItem Toggle { get; }

        internal static ValueTask<Fixture> CreateAsync()
        {
            TestDatabase database = TestDatabase.Create();
            database.Database.Initialize();
            return ValueTask.FromResult(
                new Fixture(database, new SqliteAgendaRepository(database.Database, () => Now)));
        }

        internal async ValueTask<AgendaItem> SaveAsync(AgendaItem item)
        {
            await Agenda.SaveAsync(item);
            return item;
        }

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }
}
