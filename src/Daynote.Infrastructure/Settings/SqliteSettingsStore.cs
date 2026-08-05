using System.Globalization;
using Daynote.Core.Settings;
using Daynote.Core.Time;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Settings;

/// <summary>
/// Typed get/set over the shared <c>settings</c> table. Writes go through the single serialized
/// writer (same channel as notes/clipboard) so there is never a second writer; reads use short-lived
/// read connections. Bool values persist as <c>1</c>/<c>0</c>.
/// </summary>
public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly SqliteDatabase _database;
    private readonly IClock _clock;

    public SqliteSettingsStore(SqliteDatabase database, IClock clock)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        using SqliteConnection connection = _database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        object? value = command.ExecuteScalar();
        return ValueTask.FromResult(value is string text ? text : null);
    }

    public async ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        string utc = _clock.Read().UtcInstant.ToString("O", CultureInfo.InvariantCulture);
        await _database.WriteAsync((connection, transaction, _) =>
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO settings(key,value,updated_utc) VALUES($key,$value,$utc) " +
                "ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_utc=excluded.updated_utc;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$utc", utc);
            command.ExecuteNonQuery();
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default)
    {
        string? value = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        return value is null ? fallback : string.Equals(value, "1", StringComparison.Ordinal);
    }

    public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default) =>
        SetAsync(key, value ? "1" : "0", cancellationToken);
}
