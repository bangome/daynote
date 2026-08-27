using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Search;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Sync;

[TestClass]
public sealed class SqliteSyncStoreTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-08-20").Value;
    private static readonly LocalDate OtherDate = LocalDate.Parse("2026-08-21").Value;

    private TestDatabase fixture = null!;
    private SqliteNoteRepository repository = null!;
    private SqliteSyncStore store = null!;
    private DateTimeOffset clock;
    private readonly Dictionary<NoteId, int> revisions = [];

    [TestInitialize]
    public void Setup()
    {
        clock = DateTimeOffset.Parse("2026-08-20T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        repository = new SqliteNoteRepository(fixture.Database, () => clock);
        store = new SqliteSyncStore(fixture.Database, () => clock);
    }

    [TestCleanup]
    public async Task Cleanup() => await fixture.DisposeAsync();

    // ---- outbox ----

    [TestMethod]
    public async Task Saving_a_note_queues_it_for_push()
    {
        NoteId id = await SaveNote(1, "Title", "Body");

        IReadOnlyList<PendingNote> pending = await store.ReadPendingNotesAsync(50);

        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual(id.ToString(), pending[0].Note.Id);
        Assert.AreEqual("Title", pending[0].Note.Title);
        Assert.AreEqual("Body", pending[0].Note.Body);
        Assert.IsTrue(pending[0].Note.HasCustomTitle);
        Assert.AreEqual(clock, pending[0].QueuedUtc);
    }

    [TestMethod]
    public async Task Editing_a_note_repeatedly_queues_it_once()
    {
        NoteId id = await SaveNote(1, "Title", "First");
        Advance(1);
        await EditNote(id, "Title", "Second");
        Advance(1);
        await EditNote(id, "Title", "Third");

        IReadOnlyList<PendingNote> pending = await store.ReadPendingNotesAsync(50);

        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual("Third", pending[0].Note.Body);
    }

    [TestMethod]
    public async Task A_tag_change_queues_the_note()
    {
        // Tag edits carry no timestamp of their own; they are picked up because SetTagsAsync bumps
        // notes.updated_utc. If a future change stops doing that, tag edits would never sync and this
        // test is what catches it.
        NoteId id = await SaveNote(1, "Title", "Body");
        await store.AcknowledgePushAsync([Ack(id, clock)]);
        Assert.AreEqual(0, (await store.ReadPendingNotesAsync(50)).Count);

        Advance(5);
        await repository.SetTagsAsync(Date, id, ["프로젝트", "q3"]);

        IReadOnlyList<PendingNote> pending = await store.ReadPendingNotesAsync(50);
        Assert.AreEqual(1, pending.Count);
        CollectionAssert.AreEqual(new[] { "프로젝트", "q3" }, pending[0].Note.Tags.ToArray());
    }

    [TestMethod]
    public async Task An_untitled_note_reports_its_effective_title()
    {
        await SaveNote(1, "ignored", "Body", hasCustomTitle: false);

        IReadOnlyList<PendingNote> pending = await store.ReadPendingNotesAsync(50);

        // The stored title column holds a placeholder when the user never named the note, so the
        // pushed title must be the resolved one, not the raw column.
        Assert.IsFalse(pending[0].Note.HasCustomTitle);
        Assert.AreEqual(UntitledNote.TitleFor(1), pending[0].Note.Title);
    }

    [TestMethod]
    public async Task Acknowledging_a_push_clears_the_queue()
    {
        NoteId id = await SaveNote(1, "Title", "Body");

        int cleared = await store.AcknowledgePushAsync([Ack(id, clock)]);

        Assert.AreEqual(1, cleared);
        Assert.AreEqual(0, (await store.ReadPendingNotesAsync(50)).Count);
    }

    [TestMethod]
    public async Task An_edit_made_during_a_push_survives_the_acknowledgement()
    {
        // The engine reads the queue, sends it, then acknowledges. An edit landing in between must
        // not be dropped, or that edit never reaches the cloud and nothing ever notices.
        NoteId id = await SaveNote(1, "Title", "Sent");
        DateTimeOffset sentAt = clock;

        Advance(1);
        await EditNote(id, "Title", "Newer");

        int cleared = await store.AcknowledgePushAsync([Ack(id, sentAt)]);

        Assert.AreEqual(0, cleared);
        IReadOnlyList<PendingNote> pending = await store.ReadPendingNotesAsync(50);
        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual("Newer", pending[0].Note.Body);
    }

    [TestMethod]
    public async Task Enrolment_queues_content_that_predates_the_feature()
    {
        NoteId id = await SaveNote(1, "Title", "Body");
        await ClearQueue();

        int queued = await store.EnrollExistingContentAsync();

        Assert.AreEqual(1, queued);
        Assert.AreEqual(id.ToString(), (await store.ReadPendingNotesAsync(50)).Single().Note.Id);
    }

    [TestMethod]
    public async Task Enrolment_does_not_disturb_an_existing_queue_entry()
    {
        NoteId id = await SaveNote(1, "Title", "Body");
        DateTimeOffset queuedAt = clock;

        await store.EnrollExistingContentAsync();

        Assert.AreEqual(queuedAt, (await store.ReadPendingNotesAsync(50)).Single().QueuedUtc);
        Assert.AreEqual(id.ToString(), (await store.ReadPendingNotesAsync(50)).Single().Note.Id);
    }

    // ---- tombstones ----

    [TestMethod]
    public async Task Deleting_a_note_leaves_a_tombstone_and_clears_its_queue_entry()
    {
        NoteId id = await SaveNote(1, "Title", "Body");
        await ClearQueue();

        await repository.DeleteNoteAsync(Date, id);

        IReadOnlyList<SyncTombstone> tombstones = await store.ReadPendingTombstonesAsync(50);
        Assert.AreEqual(1, tombstones.Count);
        Assert.AreEqual(SyncEntityKind.Note, tombstones[0].Kind);
        Assert.AreEqual(id.ToString(), tombstones[0].Id);
        Assert.AreEqual(0, (await store.ReadPendingNotesAsync(50)).Count);
    }

    [TestMethod]
    public async Task A_recreated_id_clears_its_tombstone()
    {
        NoteId id = await SaveNote(1, "Title", "Body");
        await repository.DeleteNoteAsync(Date, id);
        Assert.AreEqual(1, (await store.ReadPendingTombstonesAsync(50)).Count);

        Advance(1);
        await repository.CreateNoteAsync(Date, default, id);

        Assert.AreEqual(0, (await store.ReadPendingTombstonesAsync(50)).Count);
    }

    [TestMethod]
    public async Task Acknowledging_a_tombstone_clears_it()
    {
        NoteId id = await SaveNote(1, "Title", "Body");
        await repository.DeleteNoteAsync(Date, id);
        SyncTombstone tombstone = (await store.ReadPendingTombstonesAsync(50)).Single();

        Assert.AreEqual(1, await store.AcknowledgeTombstonesAsync([tombstone]));
        Assert.AreEqual(0, (await store.ReadPendingTombstonesAsync(50)).Count);
    }

    // ---- merge: last-write-wins ----

    [TestMethod]
    public async Task An_incoming_note_that_does_not_exist_locally_is_inserted()
    {
        SyncNote incoming = Incoming(Id(1), "Remote", "From another device", sortOrder: 0);

        MergeOutcome outcome = await store.MergeNotesAsync([incoming], []);

        Assert.AreEqual(1, outcome.Applied);
        Assert.AreEqual(0, outcome.Ignored);
        NoteSet workspace = await repository.GetDayWorkspaceAsync(Date);
        Assert.AreEqual("From another device", workspace.Notes.Single().Body);
        Assert.AreEqual("Remote", workspace.Notes.Single().Title);
    }

    [TestMethod]
    public async Task A_newer_incoming_note_replaces_the_local_one()
    {
        NoteId id = await SaveNote(1, "Local", "Local body");
        Advance(10);
        SyncNote incoming = Incoming(id.Value, "Remote", "Remote body", 0, updated: clock);

        MergeOutcome outcome = await store.MergeNotesAsync([incoming], []);

        Assert.AreEqual(1, outcome.Applied);
        Assert.AreEqual("Remote body", (await Notes()).Single().Body);
    }

    [TestMethod]
    public async Task An_older_incoming_note_is_ignored_and_stays_queued()
    {
        Advance(10);
        NoteId id = await SaveNote(1, "Local", "Local body");
        SyncNote incoming = Incoming(id.Value, "Remote", "Remote body", 0, updated: clock.AddMinutes(-5));

        MergeOutcome outcome = await store.MergeNotesAsync([incoming], []);

        Assert.AreEqual(0, outcome.Applied);
        Assert.AreEqual(1, outcome.Ignored);
        Assert.AreEqual("Local body", (await Notes()).Single().Body);
        // The local version is newer, so it must still be waiting to go out.
        Assert.AreEqual(1, (await store.ReadPendingNotesAsync(50)).Count);
    }

    [TestMethod]
    public async Task An_identical_timestamp_keeps_the_local_note()
    {
        NoteId id = await SaveNote(1, "Local", "Local body");
        SyncNote incoming = Incoming(id.Value, "Remote", "Remote body", 0, updated: clock);

        MergeOutcome outcome = await store.MergeNotesAsync([incoming], []);

        Assert.AreEqual(1, outcome.Ignored);
        Assert.AreEqual("Local body", (await Notes()).Single().Body);
    }

    [TestMethod]
    public async Task A_replaced_local_body_is_reported_so_it_can_be_backed_up()
    {
        // Last-write-wins is only acceptable because the losing version is handed back for the
        // conflicts folder. Dropping it silently would be data loss with no trace.
        NoteId id = await SaveNote(1, "Local", "The version I typed");
        Advance(10);

        MergeOutcome outcome = await store.MergeNotesAsync(
            [Incoming(id.Value, "Local", "The version that won", 0, updated: clock)],
            []);

        DisplacedNote displaced = outcome.Displaced.Single();
        Assert.AreEqual(id.ToString(), displaced.Id);
        Assert.AreEqual("The version I typed", displaced.Body);
    }

    [TestMethod]
    public async Task A_sort_order_only_change_is_not_reported_as_displaced()
    {
        // Otherwise every re-order on another device would litter the conflicts folder.
        NoteId id = await SaveNote(1, "Local", "Same body");
        Advance(10);

        MergeOutcome outcome = await store.MergeNotesAsync(
            [Incoming(id.Value, "Local", "Same body", 0, updated: clock)],
            []);

        Assert.AreEqual(1, outcome.Applied);
        Assert.AreEqual(0, outcome.Displaced.Count);
    }

    [TestMethod]
    public async Task Merged_notes_are_not_pushed_straight_back()
    {
        await store.MergeNotesAsync([Incoming(Id(1), "Remote", "Remote body", 0)], []);

        // The write fired the outbox trigger; leaving the entry would ping-pong the same content.
        Assert.AreEqual(0, (await store.ReadPendingNotesAsync(50)).Count);
    }

    [TestMethod]
    public async Task Merging_preserves_the_incoming_timestamp()
    {
        DateTimeOffset remoteEdit = clock.AddHours(-3);

        await store.MergeNotesAsync([Incoming(Id(1), "Remote", "Body", 0, updated: remoteEdit)], []);

        // Stamping the merge with local time would make our copy look newer than the server's and
        // push it straight back.
        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        Assert.AreEqual(
            SyncTimestamps.ToLocal(remoteEdit),
            Scalar(connection, $"SELECT updated_utc FROM notes WHERE id='{Id(1)}';"));
    }

    [TestMethod]
    public async Task Merging_bumps_the_revision_so_an_open_editor_cannot_overwrite_it()
    {
        NoteId id = await SaveNote(1, "Local", "Local body");
        int editorRevision = revisions[id];
        Advance(10);

        await store.MergeNotesAsync([Incoming(id.Value, "Remote", "Remote body", 0, updated: clock)], []);

        // An editor still holding the pre-merge revision must fail rather than clobber what arrived.
        await Assert.ThrowsExactlyAsync<RecoverableNoteException>(async () =>
            await repository.SaveNoteAsync(
                new NoteSaveRequest(id, Date, "Local", "Stale write", editorRevision, false, true)));
    }

    // ---- merge: tags, title flag, search ----

    [TestMethod]
    public async Task Merging_replaces_tags_wholesale()
    {
        NoteId id = await SaveNote(1, "Local", "Body");
        await repository.SetTagsAsync(Date, id, ["old", "stale"]);
        Advance(10);

        await store.MergeNotesAsync(
            [Incoming(id.Value, "Local", "Body", 0, updated: clock, tags: ["fresh"])],
            []);

        CollectionAssert.AreEqual(new[] { "fresh" }, (await Notes()).Single().Tags.ToArray());
    }

    [TestMethod]
    public async Task Merging_carries_the_custom_title_flag_in_both_directions()
    {
        // The flag lives in the settings table, so losing it silently renames a note to "Untitled N".
        NoteId id = await SaveNote(1, "A real name", "Body");
        Advance(10);

        await store.MergeNotesAsync(
            [Incoming(id.Value, "ignored", "Body", 0, updated: clock, hasCustomTitle: false)],
            []);
        Note cleared = (await Notes()).Single();
        Assert.IsFalse(cleared.HasCustomTitle);
        Assert.AreEqual(UntitledNote.TitleFor(1), cleared.Title);

        Advance(10);
        await store.MergeNotesAsync(
            [Incoming(id.Value, "Named again", "Body", 0, updated: clock, hasCustomTitle: true)],
            []);
        Assert.AreEqual("Named again", (await Notes()).Single().Title);
    }

    [TestMethod]
    public async Task Merging_keeps_the_favorite_flag()
    {
        await store.MergeNotesAsync([Incoming(Id(1), "Remote", "Body", 0, isFavorite: true)], []);

        Assert.IsTrue((await Notes()).Single().IsFavorite);
    }

    [TestMethod]
    public async Task A_merged_note_becomes_searchable()
    {
        await store.MergeNotesAsync(
            [Incoming(Id(1), "회의록", "분기 계획 논의", 0, tags: ["프로젝트알파"])],
            []);

        var search = new SearchService(new SqliteSearchRepository(fixture.Database));
        Assert.AreEqual(Id(1), (await search.SearchAsync("분기 계획")).Results.Single().SourceId);
        Assert.AreEqual(Id(1), (await search.SearchAsync("프로젝트알파")).Results.Single().SourceId);
        Assert.IsTrue(fixture.Database.CheckIntegrity().IsValid);
    }

    [TestMethod]
    public async Task A_note_removed_by_a_remote_tombstone_leaves_the_search_index()
    {
        NoteId id = await SaveNote(1, "회의록", "분기 계획 논의");
        Advance(10);

        await store.MergeNotesAsync([], [new SyncTombstone(SyncEntityKind.Note, id.ToString(), clock)]);

        var search = new SearchService(new SqliteSearchRepository(fixture.Database));
        Assert.AreEqual(0, (await search.SearchAsync("분기 계획")).Results.Count);
        Assert.IsTrue(fixture.Database.CheckIntegrity().IsValid);
    }

    // ---- merge: tombstones ----

    [TestMethod]
    public async Task A_remote_tombstone_deletes_the_local_note_without_queueing_a_delete()
    {
        NoteId id = await SaveNote(1, "Local", "Body");
        Advance(10);

        MergeOutcome outcome = await store.MergeNotesAsync(
            [],
            [new SyncTombstone(SyncEntityKind.Note, id.ToString(), clock)]);

        Assert.AreEqual(1, outcome.Deleted);
        Assert.AreEqual(0, (await Notes()).Count);
        // The delete came from the server; pushing it back would be pointless traffic.
        Assert.AreEqual(0, (await store.ReadPendingTombstonesAsync(50)).Count);
    }

    [TestMethod]
    public async Task A_local_edit_newer_than_a_remote_tombstone_survives()
    {
        Advance(10);
        NoteId id = await SaveNote(1, "Local", "Edited after the delete");

        MergeOutcome outcome = await store.MergeNotesAsync(
            [],
            [new SyncTombstone(SyncEntityKind.Note, id.ToString(), clock.AddMinutes(-5))]);

        Assert.AreEqual(0, outcome.Deleted);
        Assert.AreEqual(1, outcome.Ignored);
        Assert.AreEqual("Edited after the delete", (await Notes()).Single().Body);
        // Still queued, so the next push re-creates it on the server.
        Assert.AreEqual(1, (await store.ReadPendingNotesAsync(50)).Count);
    }

    [TestMethod]
    public async Task A_local_delete_newer_than_an_incoming_note_is_not_resurrected()
    {
        NoteId id = await SaveNote(1, "Local", "Body");
        Advance(10);
        await repository.DeleteNoteAsync(Date, id);

        MergeOutcome outcome = await store.MergeNotesAsync(
            [Incoming(id.Value, "Remote", "Older body", 0, updated: clock.AddMinutes(-5))],
            []);

        Assert.AreEqual(0, outcome.Applied);
        Assert.AreEqual(1, outcome.Ignored);
        Assert.AreEqual(0, (await Notes()).Count);
        // Our delete is still the newer fact, so it must still be on its way out.
        Assert.AreEqual(1, (await store.ReadPendingTombstonesAsync(50)).Count);
    }

    [TestMethod]
    public async Task An_incoming_note_newer_than_a_local_delete_is_resurrected()
    {
        NoteId id = await SaveNote(1, "Local", "Body");
        await repository.DeleteNoteAsync(Date, id);
        Advance(30);

        MergeOutcome outcome = await store.MergeNotesAsync(
            [Incoming(id.Value, "Remote", "Written after the delete", 0, updated: clock)],
            []);

        Assert.AreEqual(1, outcome.Applied);
        Assert.AreEqual(
            "Written after the delete",
            (await Notes()).Single().Body);
        Assert.AreEqual(0, (await store.ReadPendingTombstonesAsync(50)).Count);
    }

    [TestMethod]
    public async Task A_tombstone_for_something_we_never_had_is_a_no_op()
    {
        MergeOutcome outcome = await store.MergeNotesAsync(
            [],
            [new SyncTombstone(SyncEntityKind.Note, Id(77).ToString(), clock)]);

        Assert.AreEqual(0, outcome.Deleted);
        Assert.AreEqual(0, (await store.ReadPendingTombstonesAsync(50)).Count);
    }

    // ---- merge: the UNIQUE (local_date, sort_order) collision, docs/CLOUD_SYNC.md §6.1 ----

    [TestMethod]
    public async Task Two_devices_adding_a_note_at_the_same_slot_do_not_collide()
    {
        // The failure this guards: both notes claim sort_order 0 on the same date, and a naive insert
        // dies on UNIQUE (local_date, sort_order).
        NoteId mine = await SaveNote(1, "Mine", "Mine");
        Advance(5);

        MergeOutcome outcome = await store.MergeNotesAsync(
            [Incoming(Id(2), "Theirs", "Theirs", sortOrder: 0, updated: clock)],
            []);

        Assert.AreEqual(1, outcome.Applied);
        IReadOnlyList<Note> workspace = await Notes();
        Assert.AreEqual(2, workspace.Count);
        CollectionAssert.AreEqual(new[] { 0, 1 }, workspace.Select(note => note.SortOrder).ToArray());
        Assert.AreEqual(2, workspace.Select(note => note.Id!.Value).Distinct().Count());
        _ = mine;
    }

    [TestMethod]
    public async Task A_contested_slot_resolves_the_same_way_on_both_devices()
    {
        // Determinism is what makes convergence possible: given the same set of notes, both devices
        // must compute the same order or they push corrections at each other forever.
        SyncNote first = Incoming(Id(2), "Second created", "b", sortOrder: 0, created: clock.AddMinutes(10));
        SyncNote second = Incoming(Id(3), "First created", "a", sortOrder: 0, created: clock);

        await store.MergeNotesAsync([first, second], []);
        int[] oneOrder = await ReadOrder();

        await using TestDatabase mirror = TestDatabase.Create();
        mirror.Database.Initialize();
        var mirrorStore = new SqliteSyncStore(mirror.Database, () => clock);
        await mirrorStore.MergeNotesAsync([second, first], []);

        using SqliteConnection connection = mirror.Database.OpenReadConnection();
        Assert.AreEqual(
            Id(3).ToString(),
            Scalar(connection, $"SELECT id FROM notes WHERE local_date='{Date}' AND sort_order=0;"));
        CollectionAssert.AreEqual(new[] { 0, 1 }, oneOrder);
    }

    [TestMethod]
    public async Task Manual_ordering_is_preserved_across_a_merge()
    {
        // Ordering the date purely by creation time would silently undo every drag-and-drop the user
        // ever made, which is why sort_order leads the comparison.
        NoteId first = await SaveNote(1, "First", "a");
        NoteId second = await SaveNote(2, "Second", "b");
        await repository.ReorderNotesAsync(Date, [second, first]);
        Advance(10);

        await store.MergeNotesAsync([Incoming(Id(3), "Remote", "c", sortOrder: 2, updated: clock)], []);

        CollectionAssert.AreEqual(
            new[] { second, first, NoteId.Create(Id(3)).Value },
            (await Notes()).Select(note => note.Id!.Value).ToArray());
    }

    [TestMethod]
    public async Task A_note_pushed_out_of_its_slot_gets_a_newer_timestamp_so_the_change_can_travel()
    {
        // A sort_order change with an unchanged timestamp is rejected by the server as stale, so the
        // note would sit in the queue forever, re-pushed on every sync and never accepted.
        NoteId mine = await SaveNote(1, "Mine", "Mine");
        DateTimeOffset originalEdit = clock;
        await ClearQueue();
        Advance(5);

        // Created earlier, so it wins the contested slot and pushes the local note to slot 1.
        await store.MergeNotesAsync(
            [Incoming(Id(2), "Theirs", "Theirs", sortOrder: 0, updated: clock, created: clock.AddHours(-1))],
            []);

        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        string moved = Scalar(connection, $"SELECT updated_utc FROM notes WHERE id='{mine}';");
        Assert.AreNotEqual(SyncTimestamps.ToLocal(originalEdit), moved);
        Assert.AreEqual(SyncTimestamps.ToLocal(clock), moved);
        // ...and it must be queued, or the new order never reaches the other device.
        Assert.IsTrue((await store.ReadPendingNotesAsync(50)).Any(note => note.Note.Id == mine.ToString()));
    }

    [TestMethod]
    public async Task A_note_that_does_not_move_is_not_marked_as_edited()
    {
        NoteId untouched = await SaveNote(1, "Mine", "Mine");
        DateTimeOffset originalEdit = clock;
        await ClearQueue();
        Advance(5);

        // The incoming note lands after the local one, so nothing shifts.
        await store.MergeNotesAsync(
            [Incoming(Id(2), "Theirs", "Theirs", sortOrder: 1, updated: clock)],
            []);

        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        Assert.AreEqual(
            SyncTimestamps.ToLocal(originalEdit),
            Scalar(connection, $"SELECT updated_utc FROM notes WHERE id='{untouched}';"));
        // A merge must not manufacture a pending push for a note it did not touch.
        Assert.AreEqual(0, (await store.ReadPendingNotesAsync(50)).Count);
    }

    [TestMethod]
    public async Task A_merge_does_not_discard_a_sibling_notes_pending_edit()
    {
        // Re-ordering a date rewrites every note on it, which fires the outbox trigger for notes the
        // merge did not really change. Cleaning those up must not take a real pending edit with it,
        // or that edit is lost from the queue and never reaches the cloud.
        NoteId pending = await SaveNote(1, "Mine", "Edited but not yet pushed");
        Advance(5);

        // Lands after the local note, so the local note does not move.
        await store.MergeNotesAsync(
            [Incoming(Id(2), "Theirs", "Theirs", sortOrder: 1, updated: clock)],
            []);

        IReadOnlyList<PendingNote> queue = await store.ReadPendingNotesAsync(50);
        Assert.AreEqual(1, queue.Count);
        Assert.AreEqual(pending.ToString(), queue[0].Note.Id);
        Assert.AreEqual("Edited but not yet pushed", queue[0].Note.Body);
    }

    [TestMethod]
    public async Task Deleting_a_note_leaves_the_remaining_notes_densely_ordered()
    {
        NoteId first = await SaveNote(1, "First", "a");
        NoteId second = await SaveNote(2, "Second", "b");
        NoteId third = await SaveNote(3, "Third", "c");

        await repository.DeleteNoteAsync(Date, second);

        IReadOnlyList<Note> workspace = await Notes();
        CollectionAssert.AreEqual(new[] { first, third }, workspace.Select(n => n.Id!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, workspace.Select(n => n.SortOrder).ToArray());
    }

    [TestMethod]
    public async Task A_note_moved_to_another_date_leaves_both_dates_densely_ordered()
    {
        NoteId first = await SaveNote(1, "First", "a");
        NoteId second = await SaveNote(2, "Second", "b");
        Advance(10);

        // The other device moved the first note to the next day.
        await store.MergeNotesAsync(
            [Incoming(first.Value, "First", "a", sortOrder: 0, updated: clock, date: OtherDate)],
            []);

        IReadOnlyList<Note> origin = await Notes();
        IReadOnlyList<Note> destination = await Notes(OtherDate);
        Assert.AreEqual(second, origin.Single().Id!.Value);
        Assert.AreEqual(0, origin.Single().SortOrder);
        Assert.AreEqual(first, destination.Single().Id!.Value);
        Assert.AreEqual(0, destination.Single().SortOrder);
    }

    [TestMethod]
    public async Task Many_notes_merged_into_one_date_stay_uniquely_ordered()
    {
        await SaveNote(1, "Mine", "Mine");
        Advance(5);

        SyncNote[] incoming = Enumerable.Range(2, 12)
            .Select(index => Incoming(Id(index), $"Remote {index}", "body", sortOrder: 0, updated: clock))
            .ToArray();

        MergeOutcome outcome = await store.MergeNotesAsync(incoming, []);

        Assert.AreEqual(12, outcome.Applied);
        int[] orders = await ReadOrder();
        CollectionAssert.AreEqual(Enumerable.Range(0, 13).ToArray(), orders);
    }

    // ---- state ----

    [TestMethod]
    public async Task A_fresh_database_is_signed_out_with_no_cursor()
    {
        SyncStateSnapshot state = await store.ReadStateAsync();

        Assert.IsFalse(state.IsSignedIn);
        Assert.AreEqual(0L, state.ServerCursor);
        Assert.IsFalse(state.IsLocked);
        Assert.IsNull(state.LastSyncUtc);
    }

    [TestMethod]
    public async Task Signing_in_records_the_account_and_signing_out_clears_it()
    {
        await store.SignInAsync("user-1", dekGeneration: 3);
        SyncStateSnapshot signedIn = await store.ReadStateAsync();
        Assert.AreEqual("user-1", signedIn.UserId);
        Assert.AreEqual(3, signedIn.DekGeneration);

        await store.SignOutAsync();
        Assert.IsFalse((await store.ReadStateAsync()).IsSignedIn);
    }

    [TestMethod]
    public async Task Signing_out_keeps_the_queue_so_nothing_is_stranded()
    {
        await SaveNote(1, "Title", "Body");

        await store.SignInAsync("user-1", 1);
        await store.SignOutAsync();

        // Local content is still local content; signing back in must push what never got out.
        Assert.AreEqual(1, (await store.ReadPendingNotesAsync(50)).Count);
    }

    [TestMethod]
    public async Task The_cursor_advances_and_never_moves_backwards()
    {
        await store.AdvanceCursorAsync(120);
        Assert.AreEqual(120L, (await store.ReadStateAsync()).ServerCursor);

        // A stale response arriving late must not make us re-read: re-reading re-applies merges and
        // re-emits conflict backups.
        await store.AdvanceCursorAsync(90);
        Assert.AreEqual(120L, (await store.ReadStateAsync()).ServerCursor);
    }

    [TestMethod]
    public async Task Advancing_the_cursor_records_the_sync_time()
    {
        await store.AdvanceCursorAsync(1);

        Assert.AreEqual(clock, (await store.ReadStateAsync()).LastSyncUtc);
    }

    [TestMethod]
    public async Task The_locked_flag_round_trips()
    {
        await store.SetLockedAsync(true);
        Assert.IsTrue((await store.ReadStateAsync()).IsLocked);

        await store.SetLockedAsync(false);
        Assert.IsFalse((await store.ReadStateAsync()).IsLocked);
    }

    [TestMethod]
    public async Task Signing_in_clears_the_locked_flag()
    {
        await store.SetLockedAsync(true);

        await store.SignInAsync("user-1", 2);

        Assert.IsFalse((await store.ReadStateAsync()).IsLocked);
    }

    // ---- helpers ----

    private void Advance(int minutes) => clock = clock.AddMinutes(minutes);

    /// <summary>
    /// Adds a note the way the app does: create the row, then save content into it. Saving with
    /// IsNew only works for the first note on a date, so this is the path that scales past one.
    /// </summary>
    private async Task<NoteId> SaveNote(
        int idSuffix,
        string title,
        string body,
        LocalDate? date = null,
        bool hasCustomTitle = true)
    {
        LocalDate target = date ?? Date;
        NoteId id = NoteId.Create(Id(idSuffix)).Value;
        // An invalid projection id means "one real note", which is what the + button does.
        await repository.CreateNoteAsync(target, default, id);
        NoteSaveReceipt receipt = await repository.SaveNoteAsync(
            new NoteSaveRequest(id, target, title, body, 0, IsNew: false, hasCustomTitle));
        revisions[id] = receipt.Revision;
        return id;
    }

    private async Task<int> EditNote(NoteId id, string title, string body)
    {
        NoteSaveReceipt receipt = await repository.SaveNoteAsync(
            new NoteSaveRequest(id, Date, title, body, revisions[id], IsNew: false, HasCustomTitle: true));
        revisions[id] = receipt.Revision;
        return receipt.Revision;
    }

    /// <summary>
    /// The persisted notes for a date. An empty date reports a single virtual projection note, so a
    /// raw workspace count never reads as zero and must not be asserted against directly.
    /// </summary>
    private async Task<IReadOnlyList<Note>> Notes(LocalDate? date = null)
    {
        NoteSet set = await repository.GetDayWorkspaceAsync(date ?? Date);
        return [.. set.Notes.Where(static note => !note.IsProjection)];
    }

    private async Task ClearQueue()
    {
        IReadOnlyList<PendingNote> pending = await store.ReadPendingNotesAsync(500);
        await store.AcknowledgePushAsync(
            [.. pending.Select(note => new PendingAck(SyncEntityKind.Note, note.Note.Id, note.QueuedUtc))]);
    }

    private async Task<int[]> ReadOrder() => [.. (await Notes()).Select(note => note.SortOrder)];

    private PendingAck Ack(NoteId id, DateTimeOffset queuedUtc) =>
        new(SyncEntityKind.Note, id.ToString(), queuedUtc);

    private SyncNote Incoming(
        Guid id,
        string title,
        string body,
        int sortOrder,
        DateTimeOffset? updated = null,
        DateTimeOffset? created = null,
        bool isFavorite = false,
        bool hasCustomTitle = true,
        IReadOnlyList<string>? tags = null,
        LocalDate? date = null) =>
        new(
            id.ToString("D"),
            date ?? Date,
            title,
            body,
            sortOrder,
            isFavorite,
            hasCustomTitle,
            tags ?? [],
            created ?? updated ?? clock,
            updated ?? clock);

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(command.ExecuteScalar() ?? throw new AssertFailedException("Expected a value."));
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");
}
