using Daynote.Core.Agenda;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Agenda;

/// <summary>
/// <see cref="IAgendaRepository"/> over the shared SQLite database, in the shape the day-file and
/// note repositories use: writes serialised through <see cref="SqliteDatabase.WriteAsync"/>, reads
/// on their own connection.
/// </summary>
public sealed class SqliteAgendaRepository : IAgendaRepository
{
    private readonly SqliteDatabase database;
    private readonly Func<DateTimeOffset> utcNow;

    public SqliteAgendaRepository(SqliteDatabase database, Func<DateTimeOffset>? utcNow = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ValueTask<IReadOnlyList<AgendaList>> GetListsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<AgendaList>>(
            SqliteAgendaStatements.ReadLists(connection));
    }

    public ValueTask<AgendaList> CreateListAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A list needs an id.", nameof(id));
        }

        string now = SqliteAgendaStatements.FormatUtc(utcNow());
        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return SqliteAgendaStatements.InsertList(connection, transaction, id, name, now);
            },
            cancellationToken);
    }

    public ValueTask<AgendaList?> RenameListAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string now = SqliteAgendaStatements.FormatUtc(utcNow());
        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return SqliteAgendaStatements.RenameList(connection, transaction, id, name, now);
            },
            cancellationToken);
    }

    public ValueTask<int?> DeleteListAsync(Guid id, CancellationToken cancellationToken = default)
    {
        string now = SqliteAgendaStatements.FormatUtc(utcNow());
        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return SqliteAgendaStatements.DeleteList(connection, transaction, id, now);
            },
            cancellationToken);
    }

    public ValueTask<AgendaItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult(SqliteAgendaStatements.ReadItem(connection, id));
    }

    public ValueTask<IReadOnlyList<AgendaItem>> GetForDateAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<AgendaItem>>(
            SqliteAgendaStatements.ReadForDate(connection, localDate));
    }

    public ValueTask<IReadOnlyList<AgendaItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<AgendaItem>>(
            SqliteAgendaStatements.ReadAll(connection));
    }

    public ValueTask<IReadOnlyList<AgendaItem>> GetSeriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<AgendaItem>>(
            SqliteAgendaStatements.ReadSeries(connection));
    }

    public ValueTask<IReadOnlyList<AgendaItem>> GetForListAsync(
        Guid listId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = database.OpenReadConnection();
        return ValueTask.FromResult<IReadOnlyList<AgendaItem>>(
            SqliteAgendaStatements.ReadForList(connection, listId));
    }

    public ValueTask SaveAsync(AgendaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return database.WriteAsync<object?>(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                SqliteAgendaStatements.Save(connection, transaction, item);
                return null;
            },
            cancellationToken).Discard();
    }

    public ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return SqliteAgendaStatements.Delete(connection, transaction, id);
            },
            cancellationToken);
}

internal static class ValueTaskExtensions
{
    /// <summary>Drops a write's result without blocking, so a void save can share WriteAsync.</summary>
    public static async ValueTask Discard<T>(this ValueTask<T> task) => await task.ConfigureAwait(false);
}
