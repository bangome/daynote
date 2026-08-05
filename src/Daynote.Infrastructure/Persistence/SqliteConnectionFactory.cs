using System.Text;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly SqliteDatabaseOptions _options;

    public SqliteConnectionFactory(SqliteDatabaseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public SqliteConnection OpenConnection() => OpenConnection(SqliteOpenMode.ReadWriteCreate, enableWal: true);

    public SqliteConnection OpenReadConnection() => OpenConnection(SqliteOpenMode.ReadOnly, enableWal: false);

    private SqliteConnection OpenConnection(SqliteOpenMode mode, bool enableWal)
    {
        var directory = Path.GetDirectoryName(_options.DatabasePath)
            ?? throw new InvalidOperationException("Database path has no parent directory.");
        Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        try
        {
            connection.CreateFunction<string, string>(
                "daynote_nfc",
                static value => value.Normalize(NormalizationForm.FormC),
                isDeterministic: true);
            connection.CreateFunction<string, string>(
                "daynote_fold",
                static value => value.Normalize(NormalizationForm.FormC).ToUpperInvariant().Normalize(NormalizationForm.FormC),
                isDeterministic: true);
            using var command = connection.CreateCommand();
            command.CommandText = enableWal
                ? "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;"
                : "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
