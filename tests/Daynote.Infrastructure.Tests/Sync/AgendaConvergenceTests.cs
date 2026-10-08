using Daynote.Core.Agenda;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// Two real local databases, one shared server, real crypto: to-dos and events making the round
/// trip (docs/TODOS.md §9). <see cref="AgendaSyncTests"/> covers the local queue and the merge
/// rules in isolation; this is the same machinery with the engine and the wire in between.
/// </summary>
[TestClass]
public sealed class AgendaConvergenceTests
{
    private static readonly AesGcmSyncCrypto Crypto = new();
    private const string UserId = "11111111-1111-4111-8111-111111111111";

    private DateTimeOffset now;
    private InMemorySyncServer server = null!;
    private KeyMaterial dataKey = null!;
    private Device alice = null!;
    private Device bob = null!;

    [TestInitialize]
    public void Setup()
    {
        // Deliberately in the past. A tombstone is stamped by the database trigger from the real
        // machine clock, not this one, and last-write-wins compares the two: a test clock set in
        // the future would make every delete lose to the row it is deleting.
        now = DateTimeOffset.Parse(
            "2026-08-20T09:00:00Z",
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        server = new InMemorySyncServer(() => now);
        dataKey = KeyMaterial.Random();
        alice = Device.Create("alice", server, dataKey, () => now);
        bob = Device.Create("bob", server, dataKey, () => now);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await alice.DisposeAsync();
        await bob.DisposeAsync();
        dataKey.Dispose();
    }

    [TestMethod]
    public async Task A_to_do_created_on_one_device_arrives_on_the_other_whole()
    {
        AgendaItem created = Item(1) with
        {
            Kind = AgendaKind.Event,
            Title = "분기 계획 회의",
            Description = "자료 미리 공유",
            StartsAt = new WallClock(new DateTime(2026, 8, 21, 14, 0, 0)),
            EndsAt = new WallClock(new DateTime(2026, 8, 21, 15, 0, 0)),
            Rrule = "FREQ=WEEKLY;BYDAY=FR",
            Priority = 5,
            TimelineVisibility = TimelineVisibility.Always,
            AlarmLeadMinutes = [10],
        };
        await alice.Save(created);

        await Converge();

        AgendaItem? received = await bob.Get(created.Id);
        Assert.IsNotNull(received);
        Assert.AreEqual("분기 계획 회의", received.Title);
        Assert.AreEqual(AgendaKind.Event, received.Kind);
        Assert.AreEqual(created.StartsAt, received.StartsAt);
        Assert.AreEqual("FREQ=WEEKLY;BYDAY=FR", received.Rrule);
        CollectionAssert.AreEqual(new[] { 10 }, received.AlarmLeadMinutes.ToArray());

        // Wall clock, not an instant: an item read back as UTC would drift a hour every DST change.
        Assert.AreEqual(DateTimeKind.Unspecified, received.StartsAt!.Value.Value.Kind);
    }

    [TestMethod]
    public async Task An_item_lands_in_the_list_it_was_filed_under_not_the_default()
    {
        Guid work = Id(900);
        await alice.CreateList(work, "업무");
        await alice.Save(Item(1) with { ListId = work });

        await Converge();

        AgendaItem? received = await bob.Get(Id(1));
        Assert.IsNotNull(received);
        // The fallback to the default exists for a page boundary, not for ordinary traffic: the
        // engine drains lists before items, so the container is always there first.
        Assert.AreEqual(work, received.ListId);
        Assert.AreEqual("업무", (await bob.Lists()).Single(list => list.Id == work).Name);
    }

    [TestMethod]
    public async Task Completing_it_on_one_device_completes_it_on_the_other()
    {
        await alice.Save(Item(1));
        await Converge();

        Advance(5);
        await alice.Save((await alice.Get(Id(1)))! with
        {
            Status = AgendaStatus.Completed,
            CompletedUtc = now,
            UpdatedUtc = now,
        });
        await Converge();

        Assert.AreEqual(AgendaStatus.Completed, (await bob.Get(Id(1)))!.Status);
    }

    [TestMethod]
    public async Task A_delete_propagates_and_does_not_come_back()
    {
        await alice.Save(Item(1));
        await Converge();
        Assert.IsNotNull(await bob.Get(Id(1)));

        Advance(5);
        await alice.Delete(Id(1));
        await Converge();
        await Converge();

        Assert.IsNull(await bob.Get(Id(1)));
        Assert.IsNull(await alice.Get(Id(1)));
    }

    [TestMethod]
    public async Task An_override_arrives_attached_to_its_series()
    {
        AgendaItem series = Item(1) with { Rrule = "FREQ=DAILY" };
        await alice.Save(series);
        await alice.Save(Item(2) with
        {
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 8, 21, 7, 0, 0)),
            Title = "이 날만 늦게",
        });

        await Converge();

        AgendaItem? received = await bob.Get(Id(2));
        Assert.IsNotNull(received);
        // The override is a foreign key onto the row carrying the rule. If the two ever crossed on
        // the wire, this insert would fail rather than quietly land detached.
        Assert.AreEqual(series.Id, received.SeriesId);
        Assert.IsTrue(received.IsOverride);
    }

    [TestMethod]
    public async Task Deleting_a_series_takes_its_overrides_with_it_on_both_devices()
    {
        AgendaItem series = Item(1) with { Rrule = "FREQ=DAILY" };
        await alice.Save(series);
        await alice.Save(Item(2) with
        {
            SeriesId = series.Id,
            RecurrenceId = new WallClock(new DateTime(2026, 8, 21, 7, 0, 0)),
        });
        await Converge();
        Assert.IsNotNull(await bob.Get(Id(2)));

        Advance(5);
        await alice.Delete(series.Id);
        await Converge();

        // The cascade fires one tombstone per row locally, and each of them travels: a parent
        // tombstone alone would leave the other device holding orphans it could never show.
        Assert.IsNull(await bob.Get(Id(1)));
        Assert.IsNull(await bob.Get(Id(2)));
    }

    [TestMethod]
    public async Task Two_devices_do_not_end_up_with_two_default_lists()
    {
        // Both databases created their own built-in list offline, under the same fixed id. That id
        // is the whole mechanism: without it this test would end with two defaults and a unique
        // index violation on whichever device merged second.
        await Converge();

        Assert.AreEqual(1, (await bob.Lists()).Count(list => list.IsDefault));
        Assert.AreEqual(1, (await alice.Lists()).Count(list => list.IsDefault));
        Assert.AreEqual(1, (await bob.Lists()).Count);
    }

    [TestMethod]
    public async Task The_server_never_sees_the_title()
    {
        await alice.Save(Item(1) with { Title = "SECRET-TASK", Description = "SECRET-NOTE" });
        await alice.Sync();

        foreach (string blob in server.StoredBlobs)
        {
            Assert.IsFalse(blob.Contains("SECRET-TASK", StringComparison.Ordinal));
            Assert.IsFalse(blob.Contains("SECRET-NOTE", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task A_service_that_has_never_heard_of_to_dos_keeps_them_queued()
    {
        server.SupportsAgenda = false;
        await alice.Save(Item(1));

        SyncReport report = await alice.Sync();

        Assert.IsTrue(report.AgendaSyncUnsupported);
        Assert.AreEqual(0, report.AgendaPushed);

        // Nothing was lost: the queue still holds it, so the first run after the service is
        // updated sends it. Reading the silence as a rejection would have cleared the queue.
        server.SupportsAgenda = true;
        await Converge();
        Assert.IsNotNull(await bob.Get(Id(1)));
    }

    [TestMethod]
    public async Task A_newer_edit_wins_and_the_loser_is_not_written_to_the_conflicts_folder()
    {
        await alice.Save(Item(1));
        await Converge();

        Advance(5);
        await bob.Save((await bob.Get(Id(1)))! with { Title = "bob", UpdatedUtc = now });
        Advance(5);
        await alice.Save((await alice.Get(Id(1)))! with { Title = "alice", UpdatedUtc = now });
        await Converge();

        Assert.AreEqual("alice", (await bob.Get(Id(1)))!.Title);
        // A to-do is a title, a date and a checkbox, all of them visible. Saving every losing
        // version would fill the folder that also holds the user's prose with noise.
        Assert.AreEqual(0, alice.Conflicts.Saved.Count);
        Assert.AreEqual(0, bob.Conflicts.Saved.Count);
    }

    private void Advance(int minutes) => now = now.AddMinutes(minutes);

    private async Task Converge()
    {
        await alice.Sync();
        await bob.Sync();
        await alice.Sync();
        await bob.Sync();
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    private AgendaItem Item(int suffix) => new(
        Id(suffix),
        AgendaList.DefaultId,
        AgendaKind.Task,
        $"할 일 {suffix}",
        string.Empty,
        "Asia/Seoul",
        StartsAt: new WallClock(new DateTime(2026, 8, 21, 7, 0, 0)),
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
        now,
        now);

    private sealed class Device : IAsyncDisposable
    {
        private readonly TestDatabase fixture;
        private readonly SqliteAgendaRepository agenda;
        private readonly SyncEngine engine;
        private readonly KeyMaterial key;

        private Device(
            TestDatabase fixture,
            SqliteAgendaRepository agenda,
            SyncEngine engine,
            RecordingConflictSink conflicts,
            KeyMaterial key)
        {
            this.fixture = fixture;
            this.agenda = agenda;
            this.engine = engine;
            this.key = key;
            Conflicts = conflicts;
        }

        internal RecordingConflictSink Conflicts { get; }

        internal static Device Create(
            string label,
            InMemorySyncServer server,
            KeyMaterial key,
            Func<DateTimeOffset> clock)
        {
            TestDatabase fixture = TestDatabase.Create();
            fixture.Database.Initialize();

            var agenda = new SqliteAgendaRepository(fixture.Database, clock);
            var store = new SqliteSyncStore(fixture.Database, clock);
            var conflicts = new RecordingConflictSink();
            var engine = new SyncEngine(server.ClientFor(label), Crypto, store, clock, conflicts);

            store.SignInAsync(UserId, 1).AsTask().GetAwaiter().GetResult();
            // What a real sign-in does: the built-in list predates the triggers, so without this it
            // would never be queued and the other device would never learn its name.
            store.EnrollExistingContentAsync().AsTask().GetAwaiter().GetResult();

            return new Device(fixture, agenda, engine, conflicts, key);
        }

        internal ValueTask<SyncReport> Sync() => engine.SyncAsync(new SyncSession(UserId, key));

        internal Task Save(AgendaItem item) => agenda.SaveAsync(item).AsTask();

        internal Task<AgendaItem?> Get(Guid id) => agenda.GetAsync(id).AsTask();

        internal Task Delete(Guid id) => agenda.DeleteAsync(id).AsTask();

        internal Task CreateList(Guid id, string name) => agenda.CreateListAsync(id, name).AsTask();

        internal Task<IReadOnlyList<AgendaList>> Lists() => agenda.GetListsAsync().AsTask();

        public ValueTask DisposeAsync() => fixture.DisposeAsync();
    }
}
