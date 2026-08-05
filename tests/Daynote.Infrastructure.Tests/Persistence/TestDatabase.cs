using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory;

    private TestDatabase(string directory, SqliteDatabase database)
    {
        _directory = directory;
        Database = database;
    }

    public SqliteDatabase Database { get; }

    public string DatabasePath => Path.Combine(_directory, "daynote.db");

    public static TestDatabase Create(
        int writerCapacity = 32,
        IFtsCapabilityProbe? capabilityProbe = null,
        ISqliteIntegrityProbe? integrityProbe = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "daynote-task3-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var options = new SqliteDatabaseOptions(Path.Combine(directory, "daynote.db"), writerCapacity);
        return new TestDatabase(directory, new SqliteDatabase(options, capabilityProbe, integrityProbe));
    }

    public static long ScalarInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? throw new AssertFailedException("Expected scalar result."));
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
