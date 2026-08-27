using System.Reflection;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class Migration004Tests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-08-20").Value;
    private static readonly DateTimeOffset Utc =
        DateTimeOffset.Parse("2026-08-20T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

    [TestMethod]
    public async Task Upgrading_a_v3_database_keeps_its_content_and_leaves_the_queue_empty()
    {
        // The outbox is trigger-maintained, so content written before the upgrade has no entry. That
        // is intentional: enrolment (below) is an explicit step at first sign-in, not a side effect of
        // migrating, so a user who never signs in gets no sync bookkeeping churn.
        await using Upgraded upgraded = await UpgradeFromV3();

        Assert.AreEqual(4, upgraded.SchemaVersion);
        using SqliteConnection connection = upgraded.Database.OpenReadConnection();
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM notes;"));
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM day_files;"));
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM sync_outbox;"));
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM sync_tombstones;"));
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.IsTrue(upgraded.Database.CheckIntegrity().IsValid);
    }

    [TestMethod]
    public async Task The_state_row_exists_exactly_once_after_upgrading()
    {
        await using Upgraded upgraded = await UpgradeFromV3();

        using SqliteConnection connection = upgraded.Database.OpenReadConnection();
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM sync_state;"));
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT server_cursor FROM sync_state;"));
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT locked FROM sync_state;"));
    }

    [TestMethod]
    public async Task Enrolment_after_upgrading_queues_the_pre_existing_note_and_file()
    {
        await using Upgraded upgraded = await UpgradeFromV3();
        var store = new SqliteSyncStore(upgraded.Database, () => Utc);

        int queued = await store.EnrollExistingContentAsync();

        Assert.AreEqual(2, queued);
        Assert.AreEqual(1, (await store.ReadPendingNotesAsync(50)).Count);
        Assert.AreEqual(1, (await store.ReadPendingFilesAsync(50)).Count);
    }

    [TestMethod]
    public async Task Enrolment_is_safe_to_run_twice()
    {
        // First sign-in on a second PC, or a retry after a failed one, must not double-queue.
        await using Upgraded upgraded = await UpgradeFromV3();
        var store = new SqliteSyncStore(upgraded.Database, () => Utc);

        await store.EnrollExistingContentAsync();
        Assert.AreEqual(0, await store.EnrollExistingContentAsync());

        using SqliteConnection connection = upgraded.Database.OpenReadConnection();
        Assert.AreEqual(2L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM sync_outbox;"));
    }

    [TestMethod]
    public async Task A_note_written_after_upgrading_is_queued_by_the_trigger()
    {
        await using Upgraded upgraded = await UpgradeFromV3();
        var repository = new SqliteNoteRepository(upgraded.Database, () => Utc);
        var store = new SqliteSyncStore(upgraded.Database, () => Utc);
        NoteId id = NoteId.Create(Id(30)).Value;

        await repository.CreateNoteAsync(Date, default, id);
        await repository.SaveNoteAsync(new NoteSaveRequest(id, Date, "New", "Body", 0, false, true));

        Assert.AreEqual(id.ToString(), (await store.ReadPendingNotesAsync(50)).Single().Note.Id);
    }

    [TestMethod]
    public async Task Deleting_a_pre_existing_note_after_upgrading_leaves_a_tombstone()
    {
        await using Upgraded upgraded = await UpgradeFromV3();
        var repository = new SqliteNoteRepository(upgraded.Database, () => Utc);
        var store = new SqliteSyncStore(upgraded.Database, () => Utc);

        await repository.DeleteNoteAsync(Date, NoteId.Create(Id(20)).Value);

        SyncTombstone tombstone = (await store.ReadPendingTombstonesAsync(50)).Single();
        Assert.AreEqual(SyncEntityKind.Note, tombstone.Kind);
        Assert.AreEqual(Id(20).ToString("D"), tombstone.Id);
        // The app's clock, not SQLite's: last-write-wins orders this against edits.
        Assert.AreEqual(Utc, tombstone.DeletedUtc);
    }

    /// <summary>
    /// Builds a database at schema version 3 with real content, then opens it normally so the
    /// shipping migration runner applies 004 to it.
    /// </summary>
    private static async Task<Upgraded> UpgradeFromV3()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-mig004", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "daynote.db");

        var factory = new SqliteConnectionFactory(new SqliteDatabaseOptions(path));
        using (SqliteConnection connection = factory.OpenConnection())
        {
            new MigrationRunner(
            [
                ReadMigration(1, "initial"),
                ReadMigration(2, "search"),
                ReadMigration(3, "notes_meta_and_files"),
            ]).Apply(connection);

            using SqliteCommand seed = connection.CreateCommand();
            seed.CommandText =
                "INSERT INTO notes(id,local_date,title,body,sort_order,revision,created_utc,updated_utc) " +
                "VALUES($note,$date,'Before the upgrade','body',0,0,$utc,$utc);" +
                "INSERT INTO note_tags(note_id,tag,sort_order) VALUES($note,'legacy',0);" +
                "INSERT INTO file_assets(hash,relative_path,byte_length,created_utc) " +
                "VALUES('aa','aa/aa.txt',4,$utc);" +
                "INSERT INTO day_files(id,local_date,display_name,byte_length,asset_hash,created_utc) " +
                "VALUES($file,$date,'notes.txt',4,'aa',$utc);";
            seed.Parameters.AddWithValue("$note", Id(20).ToString("D"));
            seed.Parameters.AddWithValue("$file", Id(21).ToString("D"));
            seed.Parameters.AddWithValue("$date", Date.ToString());
            seed.Parameters.AddWithValue("$utc", Utc.ToString("O"));
            seed.ExecuteNonQuery();
        }

        var database = new SqliteDatabase(new SqliteDatabaseOptions(path));
        DatabaseInitializationResult initialized = database.Initialize();
        await Task.Yield();
        return new Upgraded(root, database, initialized.SchemaVersion);
    }

    private static SqliteMigration ReadMigration(int version, string name)
    {
        Assembly assembly = typeof(MigrationRunner).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .Single(value => value.EndsWith($".Migrations.{version:D3}_{name}.sql", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return new SqliteMigration(version, name, reader.ReadToEnd());
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    private sealed class Upgraded(string root, SqliteDatabase database, int schemaVersion) : IAsyncDisposable
    {
        public SqliteDatabase Database { get; } = database;

        public int SchemaVersion { get; } = schemaVersion;

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
