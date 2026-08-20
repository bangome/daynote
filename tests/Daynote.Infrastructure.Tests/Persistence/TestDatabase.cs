using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory;
    private readonly bool _ownsDirectory;

    private TestDatabase(string directory, SqliteDatabase database, bool ownsDirectory = true)
    {
        _directory = directory;
        _ownsDirectory = ownsDirectory;
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

    /// <summary>
    /// Opens a database inside a caller-owned directory, so a test can put the database and
    /// credentials.dat in one data root the way the shipping app does. The caller owns cleanup.
    /// </summary>
    public static TestDatabase CreateIn(string directory, int writerCapacity = 32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        var options = new SqliteDatabaseOptions(Path.Combine(directory, "daynote.db"), writerCapacity);
        return new TestDatabase(directory, new SqliteDatabase(options), ownsDirectory: false);
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
        if (_ownsDirectory && Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
