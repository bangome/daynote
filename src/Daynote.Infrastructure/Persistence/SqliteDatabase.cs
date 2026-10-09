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
            BackupBeforeMigrating(connection);
            var version = _migrationRunner.Apply(connection);
            CheckIntegrity(connection);

            _writer = new SerializedWriter(_connectionFactory, _options.WriterCapacity);
            _initialization = new DatabaseInitializationResult(version, true, true);
            return _initialization.Value;
        }
    }

    /// <summary>
    /// Opens a database the app keeps current, without changing its schema: no backup, no
    /// migration, no integrity or capability check. False when the schema is not current yet,
    /// and the database then stays unopened.
    /// </summary>
    /// <remarks>
    /// For a reader that runs beside the app rather than as it — Android's home-screen widgets,
    /// which can start in the same moment as the app's own <see cref="Initialize"/> after an
    /// update. Two migrations racing on one file is how the loser throws; the one that owns
    /// the schema is the app, so everything else waits for it.
    /// </remarks>
    public bool TryOpenCurrent()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialization is not null)
            {
                return true;
            }

            using (var connection = _connectionFactory.OpenReadConnection())
            {
                if (!_migrationRunner.IsCurrent(connection))
                {
                    return false;
                }
            }

            _writer = new SerializedWriter(_connectionFactory, _options.WriterCapacity);
            _initialization = new DatabaseInitializationResult(_migrationRunner.LatestVersion, true, true);
            return true;
        }
    }

    /// <summary>
    /// Copies the database aside when a step that cannot be undone is about to run
    /// (docs/TODOS.md §8).
    /// </summary>
    /// <remarks>
    /// SQLite's own backup, not a file copy: the live content can be sitting in the -wal file,
    /// and copying only the .db would hand the user a backup missing their most recent writes.
    /// <para>
    /// A failure here stops the upgrade. The point of the copy is that the step after it is
    /// irreversible, so proceeding without one would make the safety net optional exactly when it
    /// is needed. An existing file from a previous attempt is left alone — the first copy is the
    /// one taken before anything was touched.
    /// </para>
    /// </remarks>
    private void BackupBeforeMigrating(SqliteConnection connection)
    {
        if (_migrationRunner.PendingBackupName(connection) is not { } name)
        {
            return;
        }

        string path = Path.ChangeExtension(_options.DatabasePath, null) + $".before-{name}.db";
        if (File.Exists(path) || !HasContent(connection))
        {
            return;
        }

        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Unpooled: a pooled connection keeps the file open after Dispose, and this one is
            // written once and never read by this process again.
            Pooling = false,
        }.ToString());

        destination.Open();
        connection.BackupDatabase(destination);
    }

    /// <summary>
    /// Whether there is anything here worth keeping a copy of.
    /// </summary>
    /// <remarks>
    /// A database being created right now has no notes, and a copy of nothing is a file the user
    /// has to wonder about later. The table itself may not exist yet either — on a first run the
    /// schema arrives in the same pass this guards.
    /// </remarks>
    private static bool HasContent(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();

        // Asked in two steps, because the second cannot even be prepared while the table is absent.
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='notes');";
        if (Scalar(command) == 0L)
        {
            return false;
        }

        command.CommandText = "SELECT EXISTS(SELECT 1 FROM notes);";
        return Scalar(command) != 0L;

        static long Scalar(SqliteCommand command) =>
            Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
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
