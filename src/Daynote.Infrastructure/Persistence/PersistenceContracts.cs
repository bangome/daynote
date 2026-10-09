using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence;

public sealed class SqliteDatabaseOptions
{
    public SqliteDatabaseOptions(string databasePath, int writerCapacity = 64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (writerCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(writerCapacity));
        }

        DatabasePath = Path.GetFullPath(databasePath);
        WriterCapacity = writerCapacity;
    }

    public string DatabasePath { get; }

    public int WriterCapacity { get; }
}

public enum PersistenceFailureCode
{
    Fts5Unavailable,
    TrigramUnavailable,
    ForeignKeyViolation,
    FtsIntegrityViolation,
    DatabaseIntegrityViolation,
}

public sealed class PersistenceStartupException : Exception
{
    internal PersistenceStartupException(PersistenceFailureCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public PersistenceFailureCode Code { get; }
}

public sealed class MigrationException : Exception
{
    internal MigrationException(int version)
        : base("Database migration failed.")
    {
        Version = version;
    }

    public int Version { get; }
}

/// <summary>
/// One step in the schema's history: either a .sql file or, where SQL cannot express it, a piece
/// of code.
/// </summary>
/// <remarks>
/// A code step exists for data migrations that need something SQLite has not got — the <c>-[ ]</c>
/// walk of docs/TODOS.md §8 needs a regex, line splitting and a derived uuid. Putting it here
/// rather than in startup is what gives it the three properties §12 asks for: it runs inside the
/// same transaction as the schema change it belongs with, it is recorded as a
/// <c>schema_versions</c> row rather than a flag somewhere, and it therefore runs exactly once per
/// device no matter how often the app is opened.
/// </remarks>
public sealed class SqliteMigration
{
    private static readonly Regex ValidName = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public SqliteMigration(int version, string name, string sql)
        : this(version, name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        Sql = sql;
    }

    public SqliteMigration(
        int version,
        string name,
        Action<SqliteConnection, SqliteTransaction> apply,
        bool backupFirst = false)
        : this(version, name)
    {
        ArgumentNullException.ThrowIfNull(apply);
        Apply = apply;
        BackupFirst = backupFirst;
    }

    private SqliteMigration(int version, string name)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!ValidName.IsMatch(name))
        {
            throw new ArgumentException("Migration name is invalid.", nameof(name));
        }

        Version = version;
        Name = name;
    }

    public int Version { get; }

    public string Name { get; }

    /// <summary>The statements to run, or null when this step is code.</summary>
    public string? Sql { get; }

    /// <summary>The code to run, or null when this step is SQL.</summary>
    public Action<SqliteConnection, SqliteTransaction>? Apply { get; }

    /// <summary>
    /// Whether the database should be copied aside before this step runs.
    /// </summary>
    /// <remarks>
    /// For a step that cannot be undone. A schema change can be reasoned about from the .sql
    /// file; a walk that reads the user's prose and writes rows from it cannot be un-walked, and
    /// docs/TODOS.md §8 asks for a backup in front of exactly that one.
    /// </remarks>
    public bool BackupFirst { get; }
}

public readonly record struct DatabaseInitializationResult(
    int SchemaVersion,
    bool Fts5Available,
    bool TrigramAvailable);

public readonly record struct DatabaseIntegrityResult(
    int ForeignKeyViolationCount,
    int SourceDocumentCount,
    int FtsDocumentCount)
{
    public bool IsValid => ForeignKeyViolationCount == 0 && SourceDocumentCount == FtsDocumentCount;
}

public enum FtsCapabilityStatus
{
    Available,
    Fts5Unavailable,
    TrigramUnavailable,
}

public readonly record struct FtsCapabilityResult(FtsCapabilityStatus Status)
{
    public static FtsCapabilityResult Available => new(FtsCapabilityStatus.Available);

    public static FtsCapabilityResult Fts5Unavailable => new(FtsCapabilityStatus.Fts5Unavailable);

    public static FtsCapabilityResult TrigramUnavailable => new(FtsCapabilityStatus.TrigramUnavailable);
}
