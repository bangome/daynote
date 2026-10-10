using Daynote.Core.Agenda;

namespace Daynote.Core.Tests;

/// <summary>
/// Deleting and editing a to-do, and one occurrence of a repeating one (docs/TODOS.md §5).
/// </summary>
/// <remarks>
/// Against a store that cascades a series' delete to its overrides, as the database does. What a
/// delete leaves for sync — the tombstones — is pinned against the real database in
/// Infrastructure.Tests.
/// </remarks>
[TestClass]
public sealed class DeleteAndEditAgendaItemTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stamp = new(2026, 10, 9, 5, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly Guid Note = Guid.Parse("00000000-0000-4000-8000-0000000000aa");

    [TestMethod]
    public async Task A_one_off_is_deleted_and_undo_puts_it_back_newer_than_before()
    {
        AgendaItem report = Report();
        var store = new Store(report);
        var delete = new DeleteAgendaItem(store, () => Stamp);

        AgendaDeletion deletion = await delete.DeleteAsync(AgendaDay.For(Today, store.Items).Open.Single());

        Assert.IsEmpty(store.Items);
        await delete.RestoreAsync(deletion);

        AgendaItem back = store.Items.Single();
        Assert.AreEqual(report.Id, back.Id);
        Assert.AreEqual(report.Title, back.Title);
        Assert.AreEqual(Note, back.SourceNoteId);
        Assert.AreEqual(Stamp, back.UpdatedUtc);
        Assert.IsGreaterThan(report.UpdatedUtc, back.UpdatedUtc, "Restored with its old stamp, it loses to its own tombstone.");
    }

    [TestMethod]
    public async Task Deleting_one_occurrence_adds_an_exdate_and_the_other_days_remain()
    {
        AgendaItem series = Stretch();
        var store = new Store(series);
        var delete = new DeleteAgendaItem(store, () => Stamp);
        AgendaDayRow row = AgendaDay.For(Today, store.Items).Open.Single();

        AgendaDeletion deletion = await delete.DeleteAsync(row, AgendaRepeatScope.Occurrence);

        AgendaItem rule = store.Items.Single();
        Assert.AreEqual(series.Id, rule.Id, "The rule was deleted rather than told to skip a day.");
        Assert.AreSequenceEqual(new[] { row.RecurrenceId!.Value }, rule.ExceptionDates.ToArray());
        Assert.AreEqual(Stamp, rule.UpdatedUtc, "An EXDATE that does not bump the row never syncs.");
        Assert.IsEmpty(AgendaDay.For(Today, store.Items).Open);
        Assert.HasCount(1, AgendaDay.For(Today.AddDays(-1), store.Items).Open);
        Assert.HasCount(1, AgendaDay.For(Today.AddDays(1), store.Items).Open);

        await delete.RestoreAsync(deletion);

        Assert.IsEmpty(store.Items.Single().ExceptionDates);
        Assert.HasCount(1, AgendaDay.For(Today, store.Items).Open);
    }

    [TestMethod]
    public async Task Deleting_a_ticked_occurrence_takes_its_override_and_undo_brings_it_back()
    {
        AgendaItem series = Stretch();
        var store = new Store(series);
        await new ToggleAgendaItem(store, () => Stamp).ToggleAsync(AgendaDay.For(Today, store.Items).Open.Single());
        AgendaDayRow done = AgendaDay.For(Today, store.Items).Done.Single();
        var delete = new DeleteAgendaItem(store, () => Stamp.AddMinutes(1));

        AgendaDeletion deletion = await delete.DeleteAsync(done, AgendaRepeatScope.Occurrence);

        Assert.AreEqual(series.Id, store.Items.Single().Id, "The override outlived its occurrence.");
        Assert.IsEmpty(AgendaDay.For(Today, store.Items).Done);

        await delete.RestoreAsync(deletion);

        AgendaDayRow back = AgendaDay.For(Today, store.Items).Done.Single();
        Assert.AreEqual(done.Item.Id, back.Item.Id);
        Assert.AreEqual(AgendaStatus.Completed, back.Item.Status);
    }

    [TestMethod]
    public async Task Deleting_every_repeat_removes_the_series_and_its_overrides()
    {
        AgendaItem series = Stretch();
        var store = new Store(series);
        await new ToggleAgendaItem(store, () => Stamp).ToggleAsync(AgendaDay.For(Today, store.Items).Open.Single());
        AgendaDayRow tomorrow = AgendaDay.For(Today.AddDays(1), store.Items).Open.Single();
        var delete = new DeleteAgendaItem(store, () => Stamp.AddMinutes(1));

        AgendaDeletion deletion = await delete.DeleteAsync(tomorrow, AgendaRepeatScope.Series);

        Assert.IsEmpty(store.Items);
        Assert.HasCount(2, deletion.Removed);
        Assert.AreEqual(series.Id, deletion.Removed[0].Id, "The series has to go back before its overrides.");

        await delete.RestoreAsync(deletion);

        Assert.HasCount(2, store.Items);
        Assert.HasCount(1, AgendaDay.For(Today, store.Items).Done);
        Assert.HasCount(1, AgendaDay.For(Today.AddDays(1), store.Items).Open);
    }

    [TestMethod]
    public async Task An_edit_keeps_the_id_and_the_note_and_bumps_the_stamp()
    {
        AgendaItem report = Report();
        var store = new Store(report);
        AgendaItem draft = Draft(report) with { Title = "보고서 다시 보내기", Description = "팀장님 참조" };

        AgendaItem saved = await new EditAgendaItem(store, () => Stamp)
            .SaveAsync(AgendaDay.For(Today, store.Items).Open.Single(), draft);

        AgendaItem stored = store.Items.Single();
        Assert.AreEqual(report.Id, stored.Id);
        Assert.AreEqual(saved, stored);
        Assert.AreEqual("보고서 다시 보내기", stored.Title);
        Assert.AreEqual("팀장님 참조", stored.Description);
        Assert.AreEqual(Note, stored.SourceNoteId, "An edit dropped where the to-do was captured.");
        Assert.AreEqual(Created, stored.CreatedUtc);
        Assert.AreEqual(Stamp, stored.UpdatedUtc);
    }

    [TestMethod]
    public async Task Editing_this_occurrence_writes_an_override_and_leaves_the_rule()
    {
        AgendaItem series = Stretch();
        var store = new Store(series);
        AgendaDayRow row = AgendaDay.For(Today, store.Items).Open.Single();
        AgendaItem draft = Draft(series) with
        {
            Title = "스트레칭 20분",
            Rrule = null,
            DueAt = new WallClock(Today.ToDateTime(new TimeOnly(8, 30))),
            StartsAt = null,
        };

        AgendaItem moved = await new EditAgendaItem(store, () => Stamp).SaveAsync(row, draft, AgendaRepeatScope.Occurrence);

        Assert.AreNotEqual(series.Id, moved.Id);
        Assert.AreEqual(series.Id, moved.SeriesId);
        Assert.AreEqual(row.RecurrenceId, moved.RecurrenceId);
        Assert.IsNull(moved.Rrule);
        Assert.AreEqual(moved.DueAt, moved.StartsAt, "The override's two halves disagree.");
        Assert.AreEqual(series, store.Items.Single(item => item.Id == series.Id), "The rule changed.");
        AgendaDayRow today = AgendaDay.For(Today, store.Items).Open.Single();
        Assert.AreEqual("스트레칭 20분", today.Item.Title);
        Assert.AreEqual(new TimeOnly(8, 30), TimeOnly.FromDateTime(today.At!.Value.Value));
        Assert.AreEqual("스트레칭", AgendaDay.For(Today.AddDays(1), store.Items).Open.Single().Item.Title);

        // A second edit of the same occurrence rewrites its override rather than adding another.
        AgendaItem again = await new EditAgendaItem(store, () => Stamp.AddMinutes(1))
            .SaveAsync(today, draft with { Title = "스트레칭 30분" }, AgendaRepeatScope.Occurrence);
        Assert.AreEqual(moved.Id, again.Id);
        Assert.HasCount(2, store.Items);
    }

    [TestMethod]
    public async Task Editing_every_repeat_rewrites_the_rule_from_its_own_first_day()
    {
        AgendaItem series = Stretch();
        var store = new Store(series);
        AgendaDayRow row = AgendaDay.For(Today, store.Items).Open.Single();
        var draft = Draft(series) with
        {
            Title = "아침 스트레칭",
            StartsAt = new WallClock(Today.ToDateTime(new TimeOnly(6, 30))),
            DueAt = new WallClock(Today.ToDateTime(new TimeOnly(6, 30))),
        };

        AgendaItem rule = await new EditAgendaItem(store, () => Stamp).SaveAsync(row, draft, AgendaRepeatScope.Series);

        Assert.AreEqual(series.Id, rule.Id);
        Assert.HasCount(1, store.Items);
        Assert.AreEqual(new WallClock(new DateTime(2026, 10, 1, 6, 30, 0)), rule.StartsAt, "The rule moved to the day it was opened on.");
        Assert.AreEqual("FREQ=DAILY", rule.Rrule);
        Assert.AreEqual("아침 스트레칭", AgendaDay.For(Today.AddDays(3), store.Items).Open.Single().Item.Title);
    }

    /// <summary>What the sheet composes from a form showing <paramref name="item"/>: a new id and fresh stamps.</summary>
    private static AgendaItem Draft(AgendaItem item) => item with
    {
        Id = Guid.NewGuid(),
        SourceNoteId = null,
        CreatedUtc = Stamp,
        UpdatedUtc = Stamp,
    };

    private static AgendaItem Report() => Item() with
    {
        Title = "보고서 보내기",
        DueAt = new WallClock(Today.ToDateTime(new TimeOnly(14, 0))),
        HasDueTime = true,
        SourceNoteId = Note,
    };

    private static AgendaItem Stretch() => Item() with
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
        Created,
        Created);

    /// <summary>In memory, with the database's one cascade: a series takes its overrides with it.</summary>
    private sealed class Store(params AgendaItem[] seed) : IAgendaRepository
    {
        private readonly Dictionary<Guid, AgendaItem> items = seed.ToDictionary(static i => i.Id);

        internal IReadOnlyList<AgendaItem> Items => [.. items.Values];

        public ValueTask SaveAsync(AgendaItem item, CancellationToken cancellationToken = default)
        {
            items[item.Id] = item;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            foreach (Guid child in items.Values.Where(item => item.SeriesId == id).Select(static item => item.Id).ToList())
            {
                items.Remove(child);
            }

            return ValueTask.FromResult(items.Remove(id));
        }

        public ValueTask<AgendaItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(items.GetValueOrDefault(id));

        public ValueTask<IReadOnlyList<AgendaItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Items);

        public ValueTask<IReadOnlyList<AgendaList>> GetListsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AgendaList> CreateListAsync(Guid id, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AgendaList?> RenameListAsync(Guid id, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int?> DeleteListAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AgendaItem>> GetForDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AgendaItem>> GetSeriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AgendaItem>> GetForListAsync(Guid listId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
