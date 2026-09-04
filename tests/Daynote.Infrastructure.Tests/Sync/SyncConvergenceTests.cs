using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// Two real local databases, one shared server, real crypto: the end-to-end check that a change made
/// on one PC shows up on the other and that neither ever hands the server readable content.
/// </summary>
[TestClass]
public sealed class SyncConvergenceTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-08-20").Value;
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
        now = DateTimeOffset.Parse(
            "2026-08-20T09:00:00Z",
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        server = new InMemorySyncServer(() => now);
        // One account, so both devices unwrapped the same data key at sign-in.
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

    // ---- the six operations the app can perform ----

    [TestMethod]
    public async Task A_note_created_on_one_device_arrives_on_the_other()
    {
        NoteId id = await alice.AddNote(1, "회의록", "분기 계획 논의");

        await alice.Sync();
        await bob.Sync();

        Note received = (await bob.Notes()).Single();
        Assert.AreEqual(id, received.Id!.Value);
        Assert.AreEqual("회의록", received.Title);
        Assert.AreEqual("분기 계획 논의", received.Body);
    }

    [TestMethod]
    public async Task An_edit_propagates()
    {
        NoteId id = await alice.AddNote(1, "Title", "First draft");
        await Converge();

        Advance(5);
        await alice.EditNote(id, "Title", "Second draft");
        await Converge();

        Assert.AreEqual("Second draft", (await bob.Notes()).Single().Body);
    }

    [TestMethod]
    public async Task A_delete_propagates_and_does_not_come_back()
    {
        NoteId id = await alice.AddNote(1, "Title", "Body");
        await Converge();
        Assert.AreEqual(1, (await bob.Notes()).Count);

        Advance(5);
        await alice.DeleteNote(id);
        await Converge();

        Assert.AreEqual(0, (await bob.Notes()).Count);

        // A second round must not resurrect it from either side's leftovers.
        await Converge();
        Assert.AreEqual(0, (await alice.Notes()).Count);
        Assert.AreEqual(0, (await bob.Notes()).Count);
    }

    [TestMethod]
    public async Task A_reorder_propagates()
    {
        NoteId first = await alice.AddNote(1, "First", "a");
        NoteId second = await alice.AddNote(2, "Second", "b");
        await Converge();

        Advance(5);
        await alice.Reorder([second, first]);
        await Converge();

        CollectionAssert.AreEqual(
            new[] { second, first },
            (await bob.Notes()).Select(note => note.Id!.Value).ToArray());
    }

    [TestMethod]
    public async Task A_favourite_toggle_propagates()
    {
        NoteId id = await alice.AddNote(1, "Title", "Body");
        await Converge();

        Advance(5);
        await alice.ToggleFavorite(id);
        await Converge();

        Assert.IsTrue((await bob.Notes()).Single().IsFavorite);
    }

    [TestMethod]
    public async Task Tags_propagate()
    {
        NoteId id = await alice.AddNote(1, "Title", "Body");
        await Converge();

        Advance(5);
        await alice.SetTags(id, ["프로젝트", "q3"]);
        await Converge();

        CollectionAssert.AreEqual(
            new[] { "프로젝트", "q3" },
            (await bob.Notes()).Single().Tags.ToArray());
    }

    [TestMethod]
    public async Task An_untitled_note_stays_untitled_on_the_other_device()
    {
        // The custom-title flag lives in the settings table, so a note that was never named must not
        // arrive wearing the placeholder title as if the user had typed it.
        await alice.AddNote(1, "ignored", "Body", hasCustomTitle: false);
        await Converge();

        Note received = (await bob.Notes()).Single();
        Assert.IsFalse(received.HasCustomTitle);
        Assert.AreEqual(UntitledNote.TitleFor(1), received.Title);
    }

    [TestMethod]
    public async Task A_named_note_keeps_its_name_on_the_other_device()
    {
        await alice.AddNote(1, "A real name", "Body");
        await Converge();

        Note received = (await bob.Notes()).Single();
        Assert.IsTrue(received.HasCustomTitle);
        Assert.AreEqual("A real name", received.Title);
    }

    // ---- concurrent edits ----

    [TestMethod]
    public async Task The_later_edit_wins_and_the_earlier_one_is_kept_as_a_conflict()
    {
        NoteId id = await alice.AddNote(1, "Title", "Original");
        await Converge();

        // Both devices edit while offline; Bob's edit is the later one.
        Advance(5);
        await alice.EditNote(id, "Title", "Alice's version");
        Advance(10);
        await bob.EditNote(id, "Title", "Bob's version");

        await Converge();

        Assert.AreEqual("Bob's version", (await alice.Notes()).Single().Body);
        Assert.AreEqual("Bob's version", (await bob.Notes()).Single().Body);
        // Alice's edit lost, so a copy of it must exist for her to recover.
        Assert.AreEqual("Alice's version", alice.Conflicts.Saved.Single().Body);
    }

    [TestMethod]
    public async Task A_delete_on_one_device_beats_an_earlier_edit_on_the_other()
    {
        NoteId id = await alice.AddNote(1, "Title", "Body");
        await Converge();

        Advance(5);
        await alice.EditNote(id, "Title", "Edited then deleted elsewhere");
        Advance(10);
        await bob.DeleteNote(id);

        await Converge();

        Assert.AreEqual(0, (await alice.Notes()).Count);
        Assert.AreEqual(0, (await bob.Notes()).Count);
        // The delete destroyed a version Alice never pushed anywhere else.
        Assert.AreEqual("Edited then deleted elsewhere", alice.Conflicts.Saved.Single().Body);
    }

    [TestMethod]
    public async Task An_edit_after_a_delete_brings_the_note_back_everywhere()
    {
        NoteId id = await alice.AddNote(1, "Title", "Body");
        await Converge();

        Advance(5);
        await bob.DeleteNote(id);
        Advance(10);
        await alice.EditNote(id, "Title", "Still working on it");

        await Converge();

        Assert.AreEqual("Still working on it", (await alice.Notes()).Single().Body);
        Assert.AreEqual("Still working on it", (await bob.Notes()).Single().Body);
    }

    [TestMethod]
    public async Task Both_devices_adding_a_note_to_the_same_date_keep_both()
    {
        // The §6.1 collision, end to end: both notes claim sort_order 0 on the same date.
        NoteId mine = await alice.AddNote(1, "Alice", "a");
        NoteId theirs = await bob.AddNote(2, "Bob", "b");

        await Converge();

        Assert.AreEqual(2, (await alice.Notes()).Count);
        Assert.AreEqual(2, (await bob.Notes()).Count);
        CollectionAssert.AreEqual(new[] { 0, 1 }, (await alice.Notes()).Select(n => n.SortOrder).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, (await bob.Notes()).Select(n => n.SortOrder).ToArray());

        // ...and both devices agree on which one is first.
        CollectionAssert.AreEqual(
            (await alice.Notes()).Select(n => n.Id!.Value).ToArray(),
            (await bob.Notes()).Select(n => n.Id!.Value).ToArray());
        Assert.AreEqual(2, new[] { mine, theirs }.Distinct().Count());
    }

    [TestMethod]
    public async Task A_busy_date_on_both_devices_converges_to_one_dense_order()
    {
        for (int index = 1; index <= 4; index += 1)
        {
            await alice.AddNote(index, $"Alice {index}", "a");
            await bob.AddNote(index + 10, $"Bob {index}", "b");
        }

        await Converge();

        IReadOnlyList<Note> onAlice = await alice.Notes();
        IReadOnlyList<Note> onBob = await bob.Notes();
        Assert.AreEqual(8, onAlice.Count);
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).ToArray(), onAlice.Select(n => n.SortOrder).ToArray());
        CollectionAssert.AreEqual(
            onAlice.Select(n => n.Id!.Value).ToArray(),
            onBob.Select(n => n.Id!.Value).ToArray());
    }

    [TestMethod]
    public async Task Repeated_syncs_settle_instead_of_pushing_forever()
    {
        await alice.AddNote(1, "Alice", "a");
        await bob.AddNote(2, "Bob", "b");
        await Converge();

        int pushesBefore = server.PushCount;
        await Converge();
        await Converge();

        // Once converged, further syncs must stop sending content. A design that re-pushed a
        // re-ordered note every cycle would never show up as wrong data, only as endless traffic.
        SyncReport last = await alice.Sync();
        Assert.AreEqual(0, last.Pushed);
        Assert.AreEqual(0, last.TombstonesPushed);
        Assert.IsTrue(server.PushCount - pushesBefore < 12, $"{server.PushCount - pushesBefore} pushes");
    }

    // ---- a new device joining ----

    [TestMethod]
    public async Task A_new_device_receives_everything_after_enrolment_is_not_even_needed()
    {
        await alice.AddNote(1, "First", "a");
        await alice.AddNote(2, "Second", "b");
        await alice.Sync();

        await using Device laptop = Device.Create("laptop", server, dataKey, () => now);
        await laptop.Sync();

        Assert.AreEqual(2, (await laptop.Notes()).Count);
    }

    [TestMethod]
    public async Task Content_that_predates_sign_in_is_pushed_after_enrolment()
    {
        // A user who has been writing locally for months then signs in: the outbox is trigger-fed, so
        // that history only moves once it has been enrolled.
        await using Device veteran = Device.Create("veteran", server, dataKey, () => now, signIn: false);
        await veteran.AddNote(1, "Written before signing in", "body");
        await veteran.SignIn();

        await veteran.Sync();
        await bob.Sync();

        Assert.AreEqual("Written before signing in", (await bob.Notes()).Single().Title);
    }

    // ---- what the server can see ----

    [TestMethod]
    public async Task The_server_never_holds_anything_readable()
    {
        await alice.AddNote(1, "제목이 여기 있다", "본문 SECRET-BODY-MARKER");
        await alice.SetTags(NoteId.Create(Id(1)).Value, ["SECRET-TAG"]);
        await alice.Sync();

        Assert.AreEqual(1, server.StoredBlobs.Count);
        string blob = server.StoredBlobs.Single();
        foreach (string secret in new[] { "SECRET-BODY-MARKER", "SECRET-TAG", "제목이 여기 있다", "2026-08-20" })
        {
            Assert.IsFalse(
                blob.Contains(secret, StringComparison.OrdinalIgnoreCase),
                $"The server's copy leaked '{secret}'.");
        }

        // Even the date is inside the envelope, so the server cannot tell which days are written on.
        Assert.IsTrue(blob.StartsWith("v1.", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task A_device_with_the_wrong_key_cannot_read_what_it_pulls()
    {
        await alice.AddNote(1, "Title", "Body");
        await alice.Sync();

        using KeyMaterial wrongKey = KeyMaterial.Random();
        await using Device impostor = Device.Create("impostor", server, wrongKey, () => now);

        SyncReport report = await impostor.Sync();

        // Counted and reported, never quietly skipped: silently dropping it would look identical to
        // the note having been deleted.
        Assert.AreEqual(1, report.Undecryptable);
        Assert.IsTrue(report.HasUnreadableRecords);
        Assert.AreEqual(0, (await impostor.Notes()).Count);
    }

    // ---- refusals ----

    [TestMethod]
    public async Task A_locked_account_does_not_sync()
    {
        await alice.AddNote(1, "Title", "Body");
        await alice.SetLocked(true);

        SyncReport report = await alice.Sync();

        Assert.AreEqual(SyncOutcome.Locked, report.Outcome);
        Assert.AreEqual(0, server.StoredBlobs.Count);
    }

    [TestMethod]
    public async Task A_signed_out_device_does_not_sync()
    {
        await using Device offline = Device.Create("offline", server, dataKey, () => now, signIn: false);
        await offline.AddNote(1, "Title", "Body");

        SyncReport report = await offline.Sync();

        Assert.AreEqual(SyncOutcome.SignedOut, report.Outcome);
        Assert.AreEqual(0, server.StoredBlobs.Count);
    }

    [TestMethod]
    public async Task A_badly_wrong_local_clock_refuses_to_sync()
    {
        await alice.AddNote(1, "Title", "Body");
        // Syncing under a clock this wrong would let last-write-wins pick the wrong winner, which is
        // worse than not syncing at all.
        alice.SetSkew(TimeSpan.FromDays(1));

        SyncReport report = await alice.Sync();

        Assert.AreEqual(SyncOutcome.ClockSkew, report.Outcome);
        Assert.AreEqual(0, server.StoredBlobs.Count);
    }

    private void Advance(int minutes) => now = now.AddMinutes(minutes);

    private async Task Converge()
    {
        // Two rounds each: the first exchanges, the second lets each side see the other's result.
        await alice.Sync();
        await bob.Sync();
        await alice.Sync();
        await bob.Sync();
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    /// <summary>One PC: its own database, repository, store, clock, and engine.</summary>
    private sealed class Device : IAsyncDisposable
    {
        private readonly TestDatabase fixture;
        private readonly SqliteNoteRepository repository;
        private readonly SqliteSyncStore store;
        private readonly SyncEngine engine;
        private readonly KeyMaterial key;
        private TimeSpan skew;

        private Device(
            TestDatabase fixture,
            SqliteNoteRepository repository,
            SqliteSyncStore store,
            SyncEngine engine,
            RecordingConflictSink conflicts,
            KeyMaterial key)
        {
            this.fixture = fixture;
            this.repository = repository;
            this.store = store;
            this.engine = engine;
            this.key = key;
            Conflicts = conflicts;
        }

        internal RecordingConflictSink Conflicts { get; }

        internal static Device Create(
            string label,
            InMemorySyncServer server,
            KeyMaterial key,
            Func<DateTimeOffset> sharedClock,
            bool signIn = true)
        {
            TestDatabase fixture = TestDatabase.Create();
            fixture.Database.Initialize();

            Device? device = null;
            DateTimeOffset Now() => sharedClock() + device!.skew;

            var repository = new SqliteNoteRepository(fixture.Database, Now);
            var store = new SqliteSyncStore(fixture.Database, Now);
            var conflicts = new RecordingConflictSink();
            var engine = new SyncEngine(server.ClientFor(label), Crypto, store, Now, conflicts);

            device = new Device(fixture, repository, store, engine, conflicts, key);
            if (signIn)
            {
                store.SignInAsync(UserId, 1).AsTask().GetAwaiter().GetResult();
            }

            return device;
        }

        /// <summary>Makes this device's clock wrong, for the test that is about exactly that.</summary>
        internal void SetSkew(TimeSpan value) => skew = value;

        internal async Task SignIn()
        {
            await store.SignInAsync(UserId, 1);
            await store.EnrollExistingContentAsync();
        }

        internal Task SetLocked(bool locked) => store.SetLockedAsync(locked).AsTask();

        internal ValueTask<SyncReport> Sync() => engine.SyncAsync(new SyncSession(UserId, key));

        internal async Task<NoteId> AddNote(
            int idSuffix,
            string title,
            string body,
            bool hasCustomTitle = true)
        {
            NoteId id = NoteId.Create(Id(idSuffix)).Value;
            await repository.CreateNoteAsync(Date, default, id);
            await repository.SaveNoteAsync(
                new NoteSaveRequest(id, Date, title, body, 0, IsNew: false, hasCustomTitle));
            return id;
        }

        internal async Task EditNote(NoteId id, string title, string body) =>
            // Read the current revision rather than tracking it: a merge may have bumped it.
            await repository.SaveNoteAsync(
                new NoteSaveRequest(id, Date, title, body, await Revision(id), IsNew: false, true));

        internal Task DeleteNote(NoteId id) => repository.DeleteNoteAsync(Date, id).AsTask();

        internal Task Reorder(IReadOnlyList<NoteId> order) =>
            repository.ReorderNotesAsync(Date, order).AsTask();

        internal Task ToggleFavorite(NoteId id) => repository.ToggleFavoriteAsync(Date, id).AsTask();

        internal Task SetTags(NoteId id, IReadOnlyList<string> tags) =>
            repository.SetTagsAsync(Date, id, tags).AsTask();

        internal async Task<IReadOnlyList<Note>> Notes()
        {
            NoteSet set = await repository.GetDayWorkspaceAsync(Date);
            return [.. set.Notes.Where(static note => !note.IsProjection)];
        }

        private async Task<int> Revision(NoteId id)
        {
            using var connection = fixture.Database.OpenReadConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT revision FROM notes WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            object? value = command.ExecuteScalar();
            await Task.Yield();
            return value is null
                ? throw new AssertFailedException($"No note {id}.")
                : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public ValueTask DisposeAsync() => fixture.DisposeAsync();
    }
}
