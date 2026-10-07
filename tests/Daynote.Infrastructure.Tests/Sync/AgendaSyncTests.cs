using Daynote.Core.Agenda;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// The local sync bookkeeping for to-dos, events and their lists (docs/TODOS.md §9).
/// </summary>
/// <remarks>
/// Nothing in the app reads the agenda tables yet and the engine does not drive any of this, so
/// these tests are the only thing holding the promises: that an edit queues, that a delete leaves
/// a tombstone that outlives the row, that a pull cannot be echoed straight back, and that an item
/// never gets dropped for naming a list this device has not heard of.
/// </remarks>
[TestClass]
public sealed class AgendaSyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Work = new("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public async Task Saving_an_item_queues_it_for_push()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        AgendaItem item = Task("Buy milk", AgendaList.DefaultId);
        await Repository(fixture).SaveAsync(item);

        IReadOnlyList<PendingAgendaItem> pending = await Store(fixture).ReadPendingAgendaItemsAsync(10);

        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual(item.Id, pending[0].Item.Id);
        Assert.AreEqual("Buy milk", pending[0].Item.Title);
        Assert.AreEqual(item.UpdatedUtc, pending[0].QueuedUtc);
    }

    [TestMethod]
    public async Task The_queued_stamp_matches_what_an_acknowledgement_sends_back()
    {
        // The whole point of aligning the agenda tables on DateTimeOffset.ToString("O") in
        // migration 006. AcknowledgePushAsync matches queued_utc as an exact string, so a second
        // timestamp format here would leave every agenda row queued and never acknowledged.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        AgendaItem item = Task("Buy milk", AgendaList.DefaultId);
        await Repository(fixture).SaveAsync(item);

        ISyncStore store = Store(fixture);
        IReadOnlyList<PendingAgendaItem> pending = await store.ReadPendingAgendaItemsAsync(10);
        int cleared = await store.AcknowledgePushAsync(
        [
            new PendingAck(SyncEntityKind.AgendaItem, item.Id.ToString(), pending[0].QueuedUtc),
        ]);

        Assert.AreEqual(1, cleared);
        Assert.AreEqual(0, (await store.ReadPendingAgendaItemsAsync(10)).Count);
    }

    [TestMethod]
    public async Task Editing_only_the_alarms_still_queues_the_item()
    {
        // Alarms and exception dates have no triggers of their own: the repository rewrites the
        // whole item and bumps updated_utc, so the item's trigger covers them. If a future writer
        // stops bumping it, this fails rather than the change silently never syncing.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);
        AgendaItem item = Task("Standup", AgendaList.DefaultId);
        await repository.SaveAsync(item);

        ISyncStore store = Store(fixture);
        IReadOnlyList<PendingAgendaItem> first = await store.ReadPendingAgendaItemsAsync(10);
        await store.AcknowledgePushAsync(
            [new PendingAck(SyncEntityKind.AgendaItem, item.Id.ToString(), first[0].QueuedUtc)]);

        await repository.SaveAsync(item with { AlarmLeadMinutes = [10], UpdatedUtc = Now.AddMinutes(1) });

        IReadOnlyList<PendingAgendaItem> pending = await store.ReadPendingAgendaItemsAsync(10);
        Assert.AreEqual(1, pending.Count);
        CollectionAssert.AreEqual(new[] { 10 }, pending[0].Item.AlarmLeadMinutes.ToArray());
    }

    [TestMethod]
    public async Task Deleting_a_series_leaves_a_tombstone_for_every_cascaded_override()
    {
        // The other device holds the overrides as rows of their own, so a tombstone for the parent
        // alone would leave them orphaned.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);
        AgendaItem series = Task("Standup", AgendaList.DefaultId) with { Rrule = "FREQ=DAILY" };
        AgendaItem moved = Task("Standup (moved)", AgendaList.DefaultId) with
        {
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 10, 9, 7, 0, 0)),
        };
        await repository.SaveAsync(series);
        await repository.SaveAsync(moved);

        await repository.DeleteAsync(series.Id);

        IReadOnlyList<SyncTombstone> tombstones = await Store(fixture).ReadPendingTombstonesAsync(10);
        CollectionAssert.AreEquivalent(
            new[] { series.Id.ToString(), moved.Id.ToString() },
            tombstones.Where(static stone => stone.Kind == SyncEntityKind.AgendaItem)
                .Select(static stone => stone.Id).ToArray());
    }

    [TestMethod]
    public async Task Deleting_a_list_queues_the_items_it_moved_to_the_default()
    {
        // A list is not a reason to lose a task, so the items move rather than following it. The
        // other device learns where they went because each one is pushed with its new list_id.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        IAgendaRepository repository = Repository(fixture);
        await repository.CreateListAsync(Work, "Work");
        AgendaItem item = Task("Ship it", Work);
        await repository.SaveAsync(item);

        ISyncStore store = Store(fixture);
        await Drain(store);
        await repository.DeleteListAsync(Work);

        IReadOnlyList<PendingAgendaItem> items = await store.ReadPendingAgendaItemsAsync(10);
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(AgendaList.DefaultId, items[0].Item.ListId);
        Assert.IsTrue((await store.ReadPendingTombstonesAsync(10))
            .Any(stone => stone.Kind == SyncEntityKind.AgendaList && stone.Id == Work.ToString()));
    }

    [TestMethod]
    public async Task A_pulled_item_is_not_pushed_straight_back()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        ISyncStore store = Store(fixture);

        AgendaMergeOutcome outcome = await store.MergeAgendaItemsAsync(
            [Task("From the phone", AgendaList.DefaultId)],
            []);

        Assert.AreEqual(1, outcome.Applied);
        Assert.AreEqual(0, (await store.ReadPendingAgendaItemsAsync(10)).Count);
    }

    [TestMethod]
    public async Task A_newer_local_edit_beats_an_older_pulled_one()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        AgendaItem local = Task("Mine", AgendaList.DefaultId) with { UpdatedUtc = Now.AddMinutes(5) };
        await Repository(fixture).SaveAsync(local);

        ISyncStore store = Store(fixture);
        AgendaMergeOutcome outcome = await store.MergeAgendaItemsAsync(
            [local with { Title = "Theirs", UpdatedUtc = Now }],
            []);

        Assert.AreEqual(0, outcome.Applied);
        Assert.AreEqual(1, outcome.Ignored);
        Assert.AreEqual("Mine", (await Repository(fixture).GetAsync(local.Id))!.Title);

        // Still queued: the local version has to reach the server, or the two never converge.
        Assert.AreEqual(1, (await store.ReadPendingAgendaItemsAsync(10)).Count);
    }

    [TestMethod]
    public async Task A_pulled_item_naming_an_unknown_list_lands_in_the_default()
    {
        // §9: a container is not a reason to lose a task.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        AgendaItem orphan = Task("Ship it", Work);

        AgendaMergeOutcome outcome = await Store(fixture).MergeAgendaItemsAsync([orphan], []);

        Assert.AreEqual(1, outcome.Applied);
        Assert.AreEqual(AgendaList.DefaultId, (await Repository(fixture).GetAsync(orphan.Id))!.ListId);
    }

    [TestMethod]
    public async Task An_override_is_applied_after_the_series_it_belongs_to()
    {
        // Both arrive in one page and the override is a foreign key onto the series, so the order
        // the page happens to be in must not decide whether it lands.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        AgendaItem series = Task("Standup", AgendaList.DefaultId) with { Rrule = "FREQ=DAILY" };
        AgendaItem moved = Task("Standup (moved)", AgendaList.DefaultId) with
        {
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 10, 9, 7, 0, 0)),
        };

        AgendaMergeOutcome outcome = await Store(fixture).MergeAgendaItemsAsync([moved, series], []);

        Assert.AreEqual(2, outcome.Applied);
        Assert.IsNotNull(await Repository(fixture).GetAsync(moved.Id));
    }

    [TestMethod]
    public async Task A_remote_delete_of_the_default_list_is_refused()
    {
        // It is where everything else lands, so there would be nowhere to put its contents. A
        // device that managed to delete it is wrong, not ahead.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();

        AgendaMergeOutcome outcome = await Store(fixture).MergeAgendaListsAsync(
            [],
            [new SyncTombstone(SyncEntityKind.AgendaList, AgendaList.DefaultId.ToString(), Now.AddYears(1))]);

        Assert.AreEqual(0, outcome.Deleted);
        Assert.AreEqual(1, outcome.Ignored);
        Assert.AreEqual(1, (await Repository(fixture).GetListsAsync()).Count);
    }

    [TestMethod]
    public async Task A_pulled_list_cannot_claim_to_be_the_default()
    {
        // `is_default` is unique in the schema, so honouring a remote flag would make applying a
        // page depend on the order its rows arrive in. The default is the fixed id and nothing else.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();

        // The codec already strips the claim (AgendaPayloadTests); this is the store refusing to
        // act on one even if it somehow arrives.
        AgendaMergeOutcome outcome = await Store(fixture).MergeAgendaListsAsync(
            [new AgendaList(Work, "Work", 1, IsDefault: true, Now, Now)],
            []);

        Assert.AreEqual(1, outcome.Applied);
        IReadOnlyList<AgendaList> lists = await Repository(fixture).GetListsAsync();
        Assert.AreEqual(1, lists.Count(static list => list.IsDefault));
        Assert.AreEqual(AgendaList.DefaultId, lists.Single(static list => list.IsDefault).Id);
    }

    [TestMethod]
    public async Task Signing_in_enrols_agenda_rows_that_predate_the_triggers()
    {
        // The default list was inserted by migration 005, before any trigger existed. It is
        // renameable and carries a fixed id so two devices converge on one row, so it has to be
        // pushed like any other.
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        ISyncStore store = Store(fixture);
        await Drain(store);

        await store.EnrollExistingContentAsync();

        IReadOnlyList<PendingAgendaList> lists = await store.ReadPendingAgendaListsAsync(10);
        Assert.AreEqual(1, lists.Count);
        Assert.AreEqual(AgendaList.DefaultId, lists[0].List.Id);
    }

    private static async System.Threading.Tasks.Task Drain(ISyncStore store)
    {
        foreach (PendingAgendaList list in await store.ReadPendingAgendaListsAsync(100))
        {
            await store.AcknowledgePushAsync(
                [new PendingAck(SyncEntityKind.AgendaList, list.List.Id.ToString(), list.QueuedUtc)]);
        }

        foreach (PendingAgendaItem item in await store.ReadPendingAgendaItemsAsync(100))
        {
            await store.AcknowledgePushAsync(
                [new PendingAck(SyncEntityKind.AgendaItem, item.Item.Id.ToString(), item.QueuedUtc)]);
        }
    }

    private static IAgendaRepository Repository(TestDatabase fixture) =>
        new SqliteAgendaRepository(fixture.Database, () => Now);

    private static ISyncStore Store(TestDatabase fixture) =>
        new SqliteSyncStore(fixture.Database, () => Now);

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
