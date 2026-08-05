using System.Text.RegularExpressions;

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

public sealed class SqliteMigration
{
    private static readonly Regex ValidName = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public SqliteMigration(int version, string name, string sql)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (!ValidName.IsMatch(name))
        {
            throw new ArgumentException("Migration name is invalid.", nameof(name));
        }

        Version = version;
        Name = name;
        Sql = sql;
    }

    public int Version { get; }

    public string Name { get; }

    public string Sql { get; }
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
