using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class MigrationTests
{
    [TestMethod]
    public async Task Test_InitialMigration_when_database_is_empty_creates_exact_schema()
    {
        // Given
        await using var fixture = TestDatabase.Create();

        // When
        var initialized = fixture.Database.Initialize();

        // Then
        Assert.AreEqual(4, initialized.SchemaVersion);
        using var connection = fixture.Database.OpenReadConnection();
        var objects = ReadSchemaObjects(connection);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "table:clipboard_items", "table:day_files", "table:file_assets", "table:image_assets",
                "table:note_tags", "table:notes",
                "table:schema_versions", "table:search_documents", "table:search_fts",
                "table:settings", "trigger:search_documents_ad", "trigger:search_documents_ai",
                "trigger:search_documents_au",
                // Cloud sync (004). No content columns: only what still needs sending.
                "table:sync_asset_queue", "table:sync_outbox", "table:sync_state",
                "table:sync_tombstones",
                "trigger:sync_files_ad", "trigger:sync_files_ai",
                "trigger:sync_notes_ad", "trigger:sync_notes_ai", "trigger:sync_notes_au",
            },
            objects.Where(static value => !value.Contains("search_fts_", StringComparison.Ordinal)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "rowid", "id", "local_date", "title", "body", "sort_order", "revision", "created_utc", "updated_utc", "is_favorite" },
            ReadColumns(connection, "notes"));
        CollectionAssert.AreEqual(
            new[] { "note_id", "tag", "sort_order" },
            ReadColumns(connection, "note_tags"));
        CollectionAssert.AreEqual(
            new[] { "hash", "relative_path", "byte_length", "created_utc" },
            ReadColumns(connection, "file_assets"));
        CollectionAssert.AreEqual(
            new[] { "rowid", "id", "local_date", "display_name", "byte_length", "asset_hash", "created_utc" },
            ReadColumns(connection, "day_files"));
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM pragma_foreign_key_list('day_files') WHERE [table]='file_assets' AND [from]='asset_hash' AND [to]='hash';"));
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM pragma_foreign_key_list('note_tags') WHERE [table]='notes' AND [from]='note_id' AND [to]='id' AND on_delete='CASCADE';"));
        CollectionAssert.AreEqual(
            new[] { "hash", "relative_path", "width", "height", "png_byte_length", "created_utc" },
            ReadColumns(connection, "image_assets"));
        CollectionAssert.AreEqual(
            new[] { "rowid", "id", "local_date", "captured_utc", "sequence_number", "kind", "text_value", "asset_hash", "payload_hash", "byte_length" },
            ReadColumns(connection, "clipboard_items"));
        CollectionAssert.AreEqual(new[] { "key", "value", "updated_utc" }, ReadColumns(connection, "settings"));
        CollectionAssert.AreEqual(
            new[] { "rowid", "source_type", "source_id", "local_date", "title", "body", "title_folded", "body_folded" },
            ReadColumns(connection, "search_documents"));
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM pragma_foreign_key_list('clipboard_items') WHERE [table] = 'image_assets' AND [from] = 'asset_hash' AND [to] = 'hash';"));
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM pragma_index_list('notes') AS indexes WHERE indexes.[unique] = 1 AND (SELECT name FROM pragma_index_info(indexes.name) WHERE seqno=0)='local_date' AND (SELECT name FROM pragma_index_info(indexes.name) WHERE seqno=1)='sort_order';"));
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM pragma_index_list('search_documents') AS indexes WHERE indexes.[unique] = 1 AND (SELECT name FROM pragma_index_info(indexes.name) WHERE seqno=0)='source_type' AND (SELECT name FROM pragma_index_info(indexes.name) WHERE seqno=1)='source_id';"));
        var ftsSql = ReadSchemaSql(connection, "search_fts");
        StringAssert.Contains(ftsSql, "content='search_documents'");
        StringAssert.Contains(ftsSql, "content_rowid='rowid'");
        StringAssert.Contains(ftsSql, "tokenize='trigram case_sensitive 0'");
    }

    [TestMethod]
    public async Task Test_MigrationRunner_when_database_is_reopened_is_idempotent()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        await fixture.Database.WriteAsync(
            static (connection, transaction, _) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO settings(key, value, updated_utc) VALUES ($key, $value, $utc);";
                command.Parameters.AddWithValue("$key", "sentinel");
                command.Parameters.AddWithValue("$value", "preserved");
                command.Parameters.AddWithValue("$utc", "2026-07-15T00:00:00Z");
                return command.ExecuteNonQuery();
            });

        // When
        var reopened = fixture.Database.Initialize();

        // Then
        Assert.AreEqual(4, reopened.SchemaVersion);
        using var verification = fixture.Database.OpenReadConnection();
        Assert.AreEqual(4L, TestDatabase.ScalarInt64(verification, "SELECT COUNT(*) FROM schema_versions;"));
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(verification, "SELECT COUNT(*) FROM settings WHERE key='sentinel' AND value='preserved';"));
    }

    [TestMethod]
    public void Test_MigrationRunner_when_migration_faults_rolls_back_all_ddl()
    {
        // Given
        var directory = Path.Combine(Path.GetTempPath(), "daynote-task3-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var factory = new SqliteConnectionFactory(new SqliteDatabaseOptions(Path.Combine(directory, "fault.db"), 4));
        var runner = new MigrationRunner(
            new[]
            {
                new SqliteMigration(
                    1,
                    "fault",
                    "CREATE TABLE schema_versions(version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_utc TEXT NOT NULL); CREATE TABLE survivor(id INTEGER); CREATE VIRTUAL TABLE virtual_survivor USING fts5(value); INSERT INTO missing_table(id) VALUES (1);")
            });
        using var connection = factory.OpenConnection();

        try
        {
            // When
            var exception = Assert.ThrowsExactly<MigrationException>(() => runner.Apply(connection));

            // Then
            Assert.AreEqual(1, exception.Version);
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table','trigger') AND name NOT LIKE 'sqlite_%';"));
        }
        finally
        {
            connection.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Test_Migration_when_metadata_is_malformed_is_rejected_before_execution()
    {
        // Given / When / Then
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SqliteMigration(0, "zero", "SELECT 1;"));
        Assert.ThrowsExactly<ArgumentException>(() => new SqliteMigration(1, "../escape", "SELECT 1;"));
        Assert.ThrowsExactly<ArgumentException>(() => new SqliteMigration(1, "empty", "   "));
        Assert.ThrowsExactly<ArgumentException>(() => new SqliteDatabaseOptions(" ", 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SqliteDatabaseOptions("valid.db", 0));
    }

    [TestMethod]
    public void Test_MigrationRunner_when_database_version_is_newer_rejects_stale_state()
    {
        // Given
        var directory = Path.Combine(Path.GetTempPath(), "daynote-task3-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var factory = new SqliteConnectionFactory(new SqliteDatabaseOptions(Path.Combine(directory, "newer.db"), 4));
        using var connection = factory.OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE schema_versions(version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_utc TEXT NOT NULL); INSERT INTO schema_versions(version,name,applied_utc) VALUES (99,'future','2026-07-15T00:00:00Z');";
            command.ExecuteNonQuery();
        }

        try
        {
            // When
            var exception = Assert.ThrowsExactly<MigrationException>(
                () => new MigrationRunner(new[] { new SqliteMigration(1, "initial", "SELECT 1;") }).Apply(connection));

            // Then
            Assert.AreEqual(99, exception.Version);
            Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM schema_versions WHERE version=99;"));
        }
        finally
        {
            connection.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task Test_MigrationConnectionPolicy_when_reader_attempts_write_rejects_bypass()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        using var connection = fixture.Database.OpenReadConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings(key,value,updated_utc) VALUES ('bypass','blocked','2026-07-15T00:00:00Z');";

        // When / Then
        Assert.ThrowsExactly<SqliteException>(() => command.ExecuteNonQuery());
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM settings;"));
    }

    private static string[] ReadSchemaObjects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type || ':' || name FROM sqlite_master WHERE type IN ('table','trigger') AND name NOT LIKE 'sqlite_%' ORDER BY 1;";
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static string[] ReadColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(1));
        }

        return values.ToArray();
    }

    private static string ReadSchemaSql(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return (string)(command.ExecuteScalar() ?? throw new AssertFailedException($"Missing schema object {name}."));
    }
}
