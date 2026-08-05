using System.Globalization;
using Daynote.Core.Domain;
using Daynote.Core.Files;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Files;

public sealed class SqliteDayFileRepository : IDayFileRepository
{
    private readonly SqliteDatabase database;
    private readonly Func<DateTimeOffset> utcNow;

    public SqliteDayFileRepository(SqliteDatabase database, Func<DateTimeOffset>? utcNow = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ValueTask<DayFile> AddAsync(
        Guid id,
        LocalDate localDate,
        string displayName,
        PreparedFileAsset asset,
        CancellationToken cancellationToken = default)
    {
        SqliteDayFileStatements.ValidateId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(asset);
        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return SqliteDayFileStatements.Add(
                    connection, transaction, id, localDate, displayName, asset, FormatUtc(utcNow()));
            },
            cancellationToken);
    }

    public ValueTask<IReadOnlyList<DayFile>> GetForDateAsync(
        LocalDate localDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<DayFile>>(
            SqliteDayFileStatements.ReadForDate(connection, localDate));
    }

    public ValueTask<DayFileDeleteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        SqliteDayFileStatements.ValidateId(id);
        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return SqliteDayFileStatements.Delete(connection, transaction, id);
            },
            cancellationToken);
    }

    public ValueTask<IReadOnlySet<string>> GetReferencedAssetPathsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlySet<string>>(
            SqliteDayFileStatements.ReadReferencedPaths(connection));
    }

    public ValueTask<bool> IsAssetReferencedAsync(
        string hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult(SqliteDayFileStatements.IsAssetReferenced(connection, hash));
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
