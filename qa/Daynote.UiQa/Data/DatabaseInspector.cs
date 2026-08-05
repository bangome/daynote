using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Daynote.UiQa.Data;

/// <summary>
/// Read-only, payload-redacted view of a Daynote database. It opens the file read-only and selects
/// only counts and non-payload metadata (never note titles/bodies, clipboard text, or image bytes),
/// so the harness can assert binary observables (row counts, dedup, date isolation, FTS sync)
/// without ever writing user content into an evidence file.
/// </summary>
public static class DatabaseInspector
{
    public static DatabaseSnapshot Inspect(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!File.Exists(databasePath))
        {
            return new DatabaseSnapshot(databasePath, Exists: false, 0, 0, 0, 0, 0, 0, 0, ForeignKeyViolations: 0);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        long notes = Scalar(connection, "SELECT COUNT(*) FROM notes;");
        long clipboardItems = Scalar(connection, "SELECT COUNT(*) FROM clipboard_items;");
        long clipboardText = Scalar(connection, "SELECT COUNT(*) FROM clipboard_items WHERE kind = 'text';");
        long clipboardImage = Scalar(connection, "SELECT COUNT(*) FROM clipboard_items WHERE kind = 'image';");
        long imageAssets = Scalar(connection, "SELECT COUNT(*) FROM image_assets;");
        long searchDocuments = Scalar(connection, "SELECT COUNT(*) FROM search_documents;");
        long distinctDates = Scalar(connection, "SELECT COUNT(DISTINCT local_date) FROM notes;");
        long fkViolations = CountRows(connection, "PRAGMA foreign_key_check;");

        return new DatabaseSnapshot(
            databasePath,
            Exists: true,
            Notes: notes,
            ClipboardItems: clipboardItems,
            ClipboardTextItems: clipboardText,
            ClipboardImageItems: clipboardImage,
            ImageAssets: imageAssets,
            SearchDocuments: searchDocuments,
            DistinctNoteDates: distinctDates,
            ForeignKeyViolations: fkViolations);
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long CountRows(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        long rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        return rows;
    }
}

/// <summary>Payload-free database observables. No field can contain user note or clipboard content.</summary>
public sealed record DatabaseSnapshot(
    [property: JsonIgnore] string DatabasePath,
    bool Exists,
    long Notes,
    long ClipboardItems,
    long ClipboardTextItems,
    long ClipboardImageItems,
    long ImageAssets,
    long SearchDocuments,
    long DistinctNoteDates,
    long ForeignKeyViolations)
{
    /// <summary>Search index stays synchronized with its sources: one document per note plus one
    /// per text clipboard item (image-only clipboard items are never indexed).</summary>
    public bool SearchIndexSynchronized => SearchDocuments == Notes + ClipboardTextItems;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
