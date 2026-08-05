using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence;

public sealed class SqliteDatabase : IAsyncDisposable
{
    private readonly SqliteDatabaseOptions _options;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFtsCapabilityProbe _capabilityProbe;
    private readonly ISqliteIntegrityProbe _integrityProbe;
    private readonly MigrationRunner _migrationRunner;
    private readonly object _lifecycleLock = new();
    private SerializedWriter? _writer;
    private DatabaseInitializationResult? _initialization;
    private bool _disposed;

    public SqliteDatabase(
        SqliteDatabaseOptions options,
        IFtsCapabilityProbe? capabilityProbe = null,
        ISqliteIntegrityProbe? integrityProbe = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionFactory = new SqliteConnectionFactory(options);
        _capabilityProbe = capabilityProbe ?? new FtsCapabilityProbe();
        _integrityProbe = integrityProbe ?? new SqliteIntegrityProbe();
        _migrationRunner = MigrationRunner.FromEmbeddedResources();
    }

    public DatabaseInitializationResult Initialize()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialization is { } initialized)
            {
                return initialized;
            }

            using var connection = _connectionFactory.OpenConnection();
            var capability = _capabilityProbe.Check(connection);
            EnsureCapability(capability);
            var version = _migrationRunner.Apply(connection);
            CheckIntegrity(connection);

            _writer = new SerializedWriter(_connectionFactory, _options.WriterCapacity);
            _initialization = new DatabaseInitializationResult(version, true, true);
            return _initialization.Value;
        }
    }

    public SqliteConnection OpenReadConnection()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialization is null)
            {
                throw new InvalidOperationException("Database has not been initialized.");
            }
        }

        return _connectionFactory.OpenReadConnection();
    }

    public ValueTask<TResult> WriteAsync<TResult>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return ValueTask.FromException<TResult>(new ObjectDisposedException(nameof(SqliteDatabase)));
            }

            if (_writer is null)
            {
                return ValueTask.FromException<TResult>(new InvalidOperationException("Database has not been initialized."));
            }

            return _writer.ExecuteAsync(operation, cancellationToken);
        }
    }

    public DatabaseIntegrityResult CheckIntegrity()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialization is null)
            {
                throw new InvalidOperationException("Database has not been initialized.");
            }
        }

        using var connection = _connectionFactory.OpenConnection();
        return CheckIntegrity(connection);
    }

    public async ValueTask DisposeAsync()
    {
        SerializedWriter? writer;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            writer = _writer;
        }

        if (writer is not null)
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }

    }

    private static void EnsureCapability(FtsCapabilityResult capability)
    {
        switch (capability.Status)
        {
            case FtsCapabilityStatus.Available:
                return;
            case FtsCapabilityStatus.Fts5Unavailable:
                throw new PersistenceStartupException(
                    PersistenceFailureCode.Fts5Unavailable,
                    "Required SQLite capability is unavailable.");
            case FtsCapabilityStatus.TrigramUnavailable:
                throw new PersistenceStartupException(
                    PersistenceFailureCode.TrigramUnavailable,
                    "Required SQLite capability is unavailable.");
            default:
                throw new InvalidOperationException("Unknown capability status.");
        }
    }

    private DatabaseIntegrityResult CheckIntegrity(SqliteConnection connection)
    {
        bool coreIntegrityValid;
        try
        {
            coreIntegrityValid = _integrityProbe.Check(connection);
        }
        catch (SqliteException)
        {
            throw new PersistenceStartupException(
                PersistenceFailureCode.DatabaseIntegrityViolation,
                "Database integrity check failed.");
        }

        if (!coreIntegrityValid)
        {
            throw new PersistenceStartupException(
                PersistenceFailureCode.DatabaseIntegrityViolation,
                "Database integrity check failed.");
        }

        var foreignKeyViolations = CountRows(connection, "PRAGMA foreign_key_check;");
        if (foreignKeyViolations != 0)
        {
            throw new PersistenceStartupException(
                PersistenceFailureCode.ForeignKeyViolation,
                "Database integrity check failed.");
        }

        var sourceCount = ReadCount(connection, "SELECT COUNT(*) FROM search_documents;");
        var ftsCount = ReadCount(connection, "SELECT COUNT(*) FROM search_fts;");
        try
        {
            using var integrity = connection.CreateCommand();
            integrity.CommandText = "INSERT INTO search_fts(search_fts, rank) VALUES ('integrity-check', 1);";
            integrity.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            throw new PersistenceStartupException(
                PersistenceFailureCode.FtsIntegrityViolation,
                "Database integrity check failed.");
        }

        if (sourceCount != ftsCount)
        {
            throw new PersistenceStartupException(
                PersistenceFailureCode.FtsIntegrityViolation,
                "Database integrity check failed.");
        }

        return new DatabaseIntegrityResult(foreignKeyViolations, sourceCount, ftsCount);
    }

    private static int CountRows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            count++;
        }

        return count;
    }

    private static int ReadCount(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
