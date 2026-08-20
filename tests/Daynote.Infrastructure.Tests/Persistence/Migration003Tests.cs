using System.Reflection;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class Migration003Tests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-15").Value;
    private static readonly DateTimeOffset Utc =
        DateTimeOffset.Parse("2026-07-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

    [TestMethod]
    public async Task Upgrading_from_an_existing_002_database_preserves_data_and_rebuilds_the_index()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-mig003", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "daynote.db");
        Directory.CreateDirectory(root);
        try
        {
            var factory = new SqliteConnectionFactory(new SqliteDatabaseOptions(path));
            using (SqliteConnection connection = factory.OpenConnection())
            {
                new MigrationRunner([ReadMigration(1, "initial"), ReadMigration(2, "search")]).Apply(connection);
                using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText =
                    "INSERT INTO notes(id,local_date,title,body,sort_order,revision,created_utc,updated_utc) VALUES($note,$date,'Café','migration-note',0,0,$utc,$utc);" +
                    "INSERT INTO clipboard_items(id,local_date,captured_utc,sequence_number,kind,text_value,asset_hash,payload_hash,byte_length) VALUES($clip,$date,$utc,1,'text','migration-clip',NULL,'hash',13);";
                seed.Parameters.AddWithValue("$note", Id(20).ToString("D"));
                seed.Parameters.AddWithValue("$clip", Id(21).ToString("D"));
                seed.Parameters.AddWithValue("$date", Date.ToString());
                seed.Parameters.AddWithValue("$utc", Utc.ToString("O"));
                seed.ExecuteNonQuery();
            }

            await using var database = new SqliteDatabase(new(path));
            DatabaseInitializationResult initialized = database.Initialize();
            var search = new SearchService(new SqliteSearchRepository(database));

            Assert.AreEqual(4, initialized.SchemaVersion);
            Assert.AreEqual(Id(20), (await search.SearchAsync("CAFÉ")).Results.Single().SourceId);
            Assert.AreEqual(Id(21), (await search.SearchAsync("migration-clip")).Results.Single().SourceId);

            DatabaseIntegrityResult integrity = database.CheckIntegrity();
            Assert.IsTrue(integrity.IsValid);
            Assert.AreEqual(2, integrity.SourceDocumentCount);
            Assert.AreEqual(2, integrity.FtsDocumentCount);

            using SqliteConnection verify = database.OpenReadConnection();
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(verify, "SELECT COUNT(*) FROM note_tags;"));
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(verify, "SELECT COUNT(*) FROM day_files;"));
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(verify, "SELECT COUNT(*) FROM file_assets;"));
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(verify, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(verify, "SELECT is_favorite FROM notes WHERE id=" + Quote(Id(20))));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task A_tag_added_after_migration_becomes_searchable_and_the_index_stays_consistent()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => Utc);
        NoteId id = NoteId.Create(Id(1)).Value;
        await repository.SaveNoteAsync(new NoteSaveRequest(id, Date, "제목", "본문", 0, IsNew: true, HasCustomTitle: true));

        await repository.SetTagsAsync(Date, id, ["프로젝트알파"]);
        var search = new SearchService(new SqliteSearchRepository(fixture.Database));

        Assert.AreEqual(id.Value, (await search.SearchAsync("프로젝트알파")).Results.Single().SourceId);
        DatabaseIntegrityResult integrity = fixture.Database.CheckIntegrity();
        Assert.IsTrue(integrity.IsValid);
        Assert.AreEqual(1, integrity.SourceDocumentCount);
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

    private static string Quote(Guid id) => "'" + id.ToString("D") + "'";

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");
}
