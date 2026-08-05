using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Notes;

[TestClass]
public sealed class SqliteNoteRepositoryTests
{
    private static readonly LocalDate HistoricalDate = LocalDate.Parse("2026-07-14").Value;
    private static readonly DateTimeOffset FixedUtc = DateTimeOffset.Parse("2026-07-15T03:04:05Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

    [TestMethod]
    public async Task Test_NoteRepository_empty_historical_date_is_lazy_until_first_real_edit()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => FixedUtc);

        NoteSet projected = await repository.GetDayWorkspaceAsync(HistoricalDate);

        Assert.IsTrue(projected.IsProjectionOnly);
        Assert.AreEqual(HistoricalDate, projected.Notes[0].LocalDate);
        Assert.AreEqual("노트 1", projected.Notes[0].Title);
        Assert.AreEqual(0L, Count(fixture, "notes"));
        Assert.AreEqual(0L, Count(fixture, "search_documents"));

        NoteId firstId = Id(1);
        NoteSaveReceipt receipt = await repository.SaveNoteAsync(
            new NoteSaveRequest(firstId, HistoricalDate, "Note 1", "첫 줄\nsecond", 0, IsNew: true, HasCustomTitle: false));

        Assert.AreEqual(0, receipt.Revision);
        Assert.IsTrue(receipt.IsPersisted);
        Assert.AreEqual(1L, Count(fixture, "notes"));
        Assert.AreEqual(1L, Count(fixture, "search_documents"));
        Assert.AreEqual("2026-07-14", ScalarText(fixture, "SELECT local_date FROM notes;"));
    }

    [TestMethod]
    public async Task Test_NoteRepository_empty_default_projection_save_is_not_materialized_or_indexed()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => FixedUtc);

        NoteSaveReceipt receipt = await repository.SaveNoteAsync(
            new NoteSaveRequest(Id(30), HistoricalDate, "Note 1", string.Empty, 0, IsNew: true, HasCustomTitle: false));

        Assert.AreEqual(0, receipt.Revision);
        Assert.IsFalse(receipt.IsPersisted);
        Assert.AreEqual(0L, Count(fixture, "notes"));
        Assert.AreEqual(0L, Count(fixture, "search_documents"));
        Assert.IsTrue((await repository.GetDayWorkspaceAsync(HistoricalDate)).IsProjectionOnly);
    }

    [TestMethod]
    public async Task Test_NoteRepository_add_reorder_delete_restart_keeps_ids_and_contiguous_defaults()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => FixedUtc);
        NoteId firstId = Id(1);
        NoteId secondId = Id(2);
        NoteId thirdId = Id(3);

        DayWorkspace two = await repository.CreateNoteAsync(HistoricalDate, firstId, secondId);
        DayWorkspace three = await repository.CreateNoteAsync(HistoricalDate, Id(99), thirdId);
        DayWorkspace reordered = await repository.ReorderNotesAsync(HistoricalDate, [thirdId, firstId, secondId]);
        DayWorkspace deleted = await repository.DeleteNoteAsync(HistoricalDate, firstId);

        Assert.AreEqual(2, two.Notes.Notes.Count);
        Assert.AreEqual(3, three.Notes.Notes.Count);
        CollectionAssert.AreEqual(new[] { thirdId, firstId, secondId }, reordered.Notes.Notes.Select(static note => note.Id!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { thirdId, secondId }, deleted.Notes.Notes.Select(static note => note.Id!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, deleted.Notes.Notes.Select(static note => note.SortOrder).ToArray());
        CollectionAssert.AreEqual(new[] { "노트 1", "노트 2" }, deleted.Notes.Notes.Select(static note => note.Title).ToArray());
        NoteSaveReceipt afterDelete = await repository.SaveNoteAsync(
            new NoteSaveRequest(secondId, HistoricalDate, "Note 2", "edited after delete", deleted.RevisionOf(secondId), IsNew: false, HasCustomTitle: false));
        Assert.IsTrue(afterDelete.Revision > deleted.RevisionOf(secondId));

        var restarted = new SqliteNoteRepository(fixture.Database, () => FixedUtc.AddDays(1));
        NoteSet restored = await restarted.GetDayWorkspaceAsync(HistoricalDate);
        CollectionAssert.AreEqual(new[] { thirdId, secondId }, restored.Notes.Select(static note => note.Id!.Value).ToArray());
        Assert.AreEqual("edited after delete", restored.Notes[1].Body);
        Assert.AreEqual(0L, ForeignKeyViolations(fixture));
        Assert.IsTrue(fixture.Database.CheckIntegrity().IsValid);
    }

    [TestMethod]
    public async Task Test_NoteRepository_physical_database_restart_preserves_historical_workspace()
    {
        string directory = Path.Combine(Path.GetTempPath(), "daynote-task4-restart", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "daynote.db");
        Directory.CreateDirectory(directory);
        NoteId id = Id(20);
        try
        {
            await using (var firstDatabase = new Daynote.Infrastructure.Persistence.SqliteDatabase(new(path)))
            {
                firstDatabase.Initialize();
                var firstRepository = new SqliteNoteRepository(firstDatabase, () => FixedUtc);
                await firstRepository.SaveNoteAsync(new NoteSaveRequest(id, HistoricalDate, "Note 1", "restart body", 0, IsNew: true, HasCustomTitle: false));
            }

            await using var secondDatabase = new Daynote.Infrastructure.Persistence.SqliteDatabase(new(path));
            secondDatabase.Initialize();
            var secondRepository = new SqliteNoteRepository(secondDatabase, () => FixedUtc.AddDays(1));

            DayWorkspace state = await secondRepository.GetDayWorkspaceStateAsync(HistoricalDate);
            NoteSet restored = state.Notes;

            Assert.AreEqual(id, restored.Notes[0].Id);
            Assert.AreEqual("restart body", restored.Notes[0].Body);
            NoteSaveReceipt afterRestart = await secondRepository.SaveNoteAsync(
                new NoteSaveRequest(id, HistoricalDate, "Note 1", "after restart", state.RevisionOf(id), IsNew: false, HasCustomTitle: false));
            Assert.AreEqual(1, afterRestart.Revision);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Test_NoteRepository_revision_compare_and_swap_rejects_stale_body_without_payload()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => FixedUtc);
        NoteId id = Id(4);
        await repository.SaveNoteAsync(new NoteSaveRequest(id, HistoricalDate, "renamed", "v1", 0, IsNew: true, HasCustomTitle: true));
        NoteSaveReceipt updated = await repository.SaveNoteAsync(new NoteSaveRequest(id, HistoricalDate, "renamed", "v2", 0, IsNew: false, HasCustomTitle: true));

        RecoverableNoteException failure = await Assert.ThrowsExactlyAsync<RecoverableNoteException>(
            async () => await repository.SaveNoteAsync(new NoteSaveRequest(id, HistoricalDate, "renamed", "SECRET stale", 0, IsNew: false, HasCustomTitle: true)));

        Assert.AreEqual(NoteFailureCode.RevisionConflict, failure.Code);
        Assert.IsFalse(failure.Message.Contains("SECRET", StringComparison.Ordinal));
        Assert.AreEqual(1, updated.Revision);
        Assert.AreEqual("v2", ScalarText(fixture, "SELECT body FROM notes WHERE id='00000000-0000-0000-0000-000000000004';"));
    }

    [TestMethod]
    public async Task Test_NoteRepository_delete_last_returns_unpersisted_projection_and_removes_index()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => FixedUtc);
        NoteId id = Id(5);
        await repository.SaveNoteAsync(new NoteSaveRequest(id, HistoricalDate, "Note 1", "body", 0, IsNew: true, HasCustomTitle: false));

        DayWorkspace workspace = await repository.DeleteNoteAsync(HistoricalDate, id);

        Assert.IsTrue(workspace.Notes.IsProjectionOnly);
        Assert.AreEqual(0L, Count(fixture, "notes"));
        Assert.AreEqual(0L, Count(fixture, "search_documents"));
    }

    [TestMethod]
    public async Task Test_NoteRepository_custom_title_marker_survives_default_collision_reorders_and_restart()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => FixedUtc);
        NoteId first = Id(40);
        NoteId second = Id(41);
        await repository.CreateNoteAsync(HistoricalDate, first, second);
        NoteSaveReceipt renamed = await repository.SaveNoteAsync(
            new NoteSaveRequest(second, HistoricalDate, "Note 1", string.Empty, 0, IsNew: false, HasCustomTitle: true));
        Assert.AreEqual(1, renamed.Revision);

        DayWorkspace firstReorder = await repository.ReorderNotesAsync(HistoricalDate, [second, first]);
        Assert.IsTrue(firstReorder.RevisionOf(second) > renamed.Revision);
        RecoverableNoteException stale = await Assert.ThrowsExactlyAsync<RecoverableNoteException>(
            async () => await repository.SaveNoteAsync(
                new NoteSaveRequest(second, HistoricalDate, "Note 1", "stale", renamed.Revision, IsNew: false, HasCustomTitle: true)));
        Assert.AreEqual(NoteFailureCode.RevisionConflict, stale.Code);
        DayWorkspace secondReorder = await repository.ReorderNotesAsync(HistoricalDate, [first, second]);

        Note custom = secondReorder.Notes.Notes.Single(note => note.Id == second);
        Assert.AreEqual("Note 1", custom.Title);
        Assert.IsTrue(custom.HasCustomTitle);
        NoteSaveReceipt saved = await repository.SaveNoteAsync(
            new NoteSaveRequest(second, HistoricalDate, custom.Title, "fresh", secondReorder.RevisionOf(second), IsNew: false, HasCustomTitle: true));
        Assert.IsTrue(saved.Revision > secondReorder.RevisionOf(second));
        DayWorkspace restarted = await repository.GetDayWorkspaceStateAsync(HistoricalDate);
        Note restored = restarted.Notes.Notes.Single(note => note.Id == second);
        Assert.AreEqual("Note 1", restored.Title);
        Assert.IsTrue(restored.HasCustomTitle);
        Assert.AreEqual("fresh", restored.Body);
    }

    private static NoteId Id(int suffix) => NoteId.Create(Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}")).Value;

    private static long Count(TestDatabase fixture, string table)
    {
        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        return TestDatabase.ScalarInt64(connection, $"SELECT COUNT(*) FROM {table};");
    }

    private static long ForeignKeyViolations(TestDatabase fixture)
    {
        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using SqliteDataReader reader = command.ExecuteReader();
        long count = 0;
        while (reader.Read()) count++;
        return count;
    }

    private static string ScalarText(TestDatabase fixture, string sql)
    {
        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(command.ExecuteScalar() ?? throw new AssertFailedException("Expected text scalar."));
    }
}
