using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

[TestClass]
public sealed class FtsCapabilityTests
{
    [TestMethod]
    public async Task Test_FtsCapability_when_runtime_supports_fts5_and_trigram_initializes()
    {
        // Given
        await using var fixture = TestDatabase.Create();

        // When
        var result = fixture.Database.Initialize();

        // Then
        Assert.IsTrue(result.Fts5Available);
        Assert.IsTrue(result.TrigramAvailable);
        using var connection = fixture.Database.OpenReadConnection();
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT sqlite_compileoption_used('ENABLE_FTS5');"));
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM sqlite_temp_master WHERE name='daynote_trigram_capability';"));
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_fts5_is_unavailable_rejects_startup_without_schema_changes()
    {
        // Given
        await using var fixture = TestDatabase.Create(capabilityProbe: new UnavailableFtsProbe());

        // When
        var exception = Assert.ThrowsExactly<PersistenceStartupException>(() => fixture.Database.Initialize());

        // Then
        Assert.AreEqual(PersistenceFailureCode.Fts5Unavailable, exception.Code);
        Assert.AreEqual("Required SQLite capability is unavailable.", exception.Message);
        Assert.IsFalse(exception.Message.Contains(fixture.DatabasePath, StringComparison.Ordinal));
        using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
        connection.Open();
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table','trigger') AND name NOT LIKE 'sqlite_%';"));
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_data_is_consistent_reports_source_and_index_counts()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        await fixture.Database.WriteAsync(
            static (connection, transaction, _) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO search_documents(source_type, source_id, local_date, title, body, title_folded, body_folded) VALUES ($type,$id,$date,$title,$body,$titleFolded,$bodyFolded);";
                command.Parameters.AddWithValue("$type", "note");
                command.Parameters.AddWithValue("$id", "note-1");
                command.Parameters.AddWithValue("$date", "2026-07-15");
                command.Parameters.AddWithValue("$title", "Title");
                command.Parameters.AddWithValue("$body", "searchable body");
                command.Parameters.AddWithValue("$titleFolded", "TITLE");
                command.Parameters.AddWithValue("$bodyFolded", "SEARCHABLE BODY");
                return command.ExecuteNonQuery();
            });

        // When
        var result = fixture.Database.CheckIntegrity();

        // Then
        Assert.AreEqual(0, result.ForeignKeyViolationCount);
        Assert.AreEqual(1, result.SourceDocumentCount);
        Assert.AreEqual(1, result.FtsDocumentCount);
        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_foreign_key_is_invalid_returns_payload_free_failure()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var disable = connection.CreateCommand();
            disable.CommandText = "PRAGMA foreign_keys=OFF;";
            disable.ExecuteNonQuery();
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO clipboard_items(id,local_date,captured_utc,sequence_number,kind,text_value,asset_hash,payload_hash,byte_length) VALUES ('bad','2026-07-15','2026-07-15T00:00:00Z',1,'image',NULL,'missing','hash',1);";
            insert.ExecuteNonQuery();
        }

        // When
        var exception = Assert.ThrowsExactly<PersistenceStartupException>(() => fixture.Database.CheckIntegrity());

        // Then
        Assert.AreEqual(PersistenceFailureCode.ForeignKeyViolation, exception.Code);
        Assert.AreEqual("Database integrity check failed.", exception.Message);
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_external_index_drifts_rejects_integrity()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        await fixture.Database.WriteAsync(
            static (connection, transaction, _) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded) VALUES ('note','drift','2026-07-15','title','body','TITLE','BODY');";
                return command.ExecuteNonQuery();
            });
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var corrupt = connection.CreateCommand();
            corrupt.CommandText = "INSERT INTO search_fts(search_fts,rowid,title,body,title_folded,body_folded) SELECT 'delete',rowid,title,body,title_folded,body_folded FROM search_documents WHERE source_id='drift';";
            corrupt.ExecuteNonQuery();
        }

        // When
        var exception = Assert.ThrowsExactly<PersistenceStartupException>(() => fixture.Database.CheckIntegrity());

        // Then
        Assert.AreEqual(PersistenceFailureCode.FtsIntegrityViolation, exception.Code);
        Assert.AreEqual("Database integrity check failed.", exception.Message);
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_source_is_updated_and_deleted_keeps_triggers_synchronized()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        await fixture.Database.WriteAsync(
            static (connection, transaction, _) =>
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded) VALUES ('note','trigger','2026-07-15','old title','old body','OLD TITLE','OLD BODY');";
                return insert.ExecuteNonQuery();
            });

        // When
        await fixture.Database.WriteAsync(
            static (connection, transaction, _) =>
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE search_documents SET title='new title',body='new searchable body',title_folded='NEW TITLE',body_folded='NEW SEARCHABLE BODY' WHERE source_id='trigger';";
                return update.ExecuteNonQuery();
            });

        // Then
        using (var connection = fixture.Database.OpenReadConnection())
        {
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM search_fts WHERE search_fts MATCH 'old';"));
            Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM search_fts WHERE search_fts MATCH 'searchable';"));
        }

        await fixture.Database.WriteAsync(
            static (connection, transaction, _) =>
            {
                using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM search_documents WHERE source_id='trigger';";
                return delete.ExecuteNonQuery();
            });
        var integrity = fixture.Database.CheckIntegrity();
        Assert.AreEqual(0, integrity.SourceDocumentCount);
        Assert.AreEqual(0, integrity.FtsDocumentCount);
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_core_integrity_is_non_ok_rejects_with_typed_failure()
    {
        // Given
        await using var fixture = TestDatabase.Create(integrityProbe: new InvalidCoreIntegrityProbe());

        // When
        var exception = Assert.ThrowsExactly<PersistenceStartupException>(() => fixture.Database.Initialize());

        // Then
        Assert.AreEqual(PersistenceFailureCode.DatabaseIntegrityViolation, exception.Code);
        Assert.AreEqual("Database integrity check failed.", exception.Message);
        Assert.IsFalse(exception.Message.Contains(fixture.DatabasePath, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_core_database_is_healthy_integrity_probe_returns_ok()
    {
        // Given
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        using var connection = fixture.Database.OpenReadConnection();

        // When
        var isValid = new SqliteIntegrityProbe().Check(connection);

        // Then
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public async Task Test_FtsCapability_when_manual_core_integrity_becomes_non_ok_rejects_with_typed_failure()
    {
        // Given
        await using var fixture = TestDatabase.Create(integrityProbe: new SequencedCoreIntegrityProbe());
        fixture.Database.Initialize();

        // When
        var exception = Assert.ThrowsExactly<PersistenceStartupException>(() => fixture.Database.CheckIntegrity());

        // Then
        Assert.AreEqual(PersistenceFailureCode.DatabaseIntegrityViolation, exception.Code);
        Assert.AreEqual("Database integrity check failed.", exception.Message);
    }

    private sealed class UnavailableFtsProbe : IFtsCapabilityProbe
    {
        public FtsCapabilityResult Check(SqliteConnection connection) => FtsCapabilityResult.Fts5Unavailable;
    }

    private sealed class InvalidCoreIntegrityProbe : ISqliteIntegrityProbe
    {
        public bool Check(SqliteConnection connection) => false;
    }

    private sealed class SequencedCoreIntegrityProbe : ISqliteIntegrityProbe
    {
        private int _calls;

        public bool Check(SqliteConnection connection) => Interlocked.Increment(ref _calls) == 1;
    }
}
