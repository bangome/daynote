using System.Globalization;
using System.Security.Cryptography;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Portable.Tests.Persistence.Profiles;

/// <summary>
/// Builds profile folders the way the app leaves them: a migrated database with rows written by plain
/// SQL, so the triggers under test (outbox, tombstones) fire exactly as they do for the real writers.
/// </summary>
internal static class ProfileFixture
{
    public const string UserA = "0b6c1a4e-5d0f-4d57-9a55-0e2f7b3c9d11";
    public const string UserB = "7f3e2d1c-0b9a-4e8d-8c7b-6a5f4e3d2c1b";

    public static SqliteDatabase Open(string root)
    {
        var database = new SqliteDatabase(new SqliteDatabaseOptions(Path.Combine(root, "daynote.db")));
        database.Initialize();
        return database;
    }

    public static string Utc(int day, int hour = 9) =>
        new DateTimeOffset(2026, 9, day, hour, 0, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);

    public static async Task ExecAsync(SqliteDatabase database, string sql, params (string Name, object Value)[] parameters)
    {
        await database.WriteAsync(
            (connection, transaction, _) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach ((string name, object value) in parameters)
                {
                    command.Parameters.AddWithValue(name, value);
                }

                return command.ExecuteNonQuery();
            });
    }

    public static async Task InsertNoteAsync(
        SqliteDatabase database,
        string id,
        string date,
        string title,
        string body,
        int sortOrder,
        string createdUtc,
        string updatedUtc,
        bool favorite = false,
        bool customTitle = false,
        params string[] tags)
    {
        await ExecAsync(
            database,
            "INSERT INTO notes(id,local_date,title,body,sort_order,revision,created_utc,updated_utc,is_favorite) " +
            "VALUES($id,$date,$title,$body,$order,0,$created,$updated,$fav);",
            ("$id", id), ("$date", date), ("$title", title), ("$body", body), ("$order", sortOrder),
            ("$created", createdUtc), ("$updated", updatedUtc), ("$fav", favorite ? 1 : 0));
        if (customTitle)
        {
            await SetSettingAsync(database, "note.custom-title." + id, "1");
        }

        for (int i = 0; i < tags.Length; i++)
        {
            await ExecAsync(
                database,
                "INSERT INTO note_tags(note_id,tag,sort_order) VALUES($id,$tag,$order);",
                ("$id", id), ("$tag", tags[i]), ("$order", i));
        }
    }

    public static Task SetSettingAsync(SqliteDatabase database, string key, string value) =>
        ExecAsync(
            database,
            "INSERT INTO settings(key,value,updated_utc) VALUES($k,$v,$u) " +
            "ON CONFLICT(key) DO UPDATE SET value=excluded.value;",
            ("$k", key), ("$v", value), ("$u", Utc(1)));

    /// <summary>Writes the blob under <c>files/</c> and its two rows; returns the content hash.</summary>
    public static async Task<string> InsertFileAsync(
        SqliteDatabase database,
        string root,
        string id,
        string date,
        string displayName,
        byte[] content,
        string createdUtc,
        bool writeBlob = true)
    {
        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        string relative = Path.Combine(hash[..2], hash + Path.GetExtension(displayName));
        if (writeBlob)
        {
            string blob = Path.Combine(root, "files", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(blob)!);
            await File.WriteAllBytesAsync(blob, content);
        }

        await ExecAsync(
            database,
            "INSERT INTO file_assets(hash,relative_path,byte_length,created_utc) VALUES($h,$p,$l,$c) " +
            "ON CONFLICT(hash) DO NOTHING;",
            ("$h", hash), ("$p", relative), ("$l", (long)content.Length), ("$c", createdUtc));
        await ExecAsync(
            database,
            "INSERT INTO day_files(id,local_date,display_name,byte_length,asset_hash,created_utc) " +
            "VALUES($id,$d,$n,$l,$h,$c);",
            ("$id", id), ("$d", date), ("$n", displayName), ("$l", (long)content.Length), ("$h", hash), ("$c", createdUtc));
        return hash;
    }

    public static object? Scalar(string root, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection =
            new SqliteConnectionFactory(new SqliteDatabaseOptions(Path.Combine(root, "daynote.db"))).OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteScalar();
    }

    public static long Count(string root, string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt64(Scalar(root, sql, parameters), CultureInfo.InvariantCulture);

    /// <summary>
    /// A root as the previous version leaves it: two notes, a custom title, device settings, a pull
    /// cursor, an attachment, a conflict copy and — when <paramref name="userId"/> is given — a signed-in
    /// <c>sync_state</c> and a <c>credentials.dat</c>.
    /// </summary>
    public static async Task CreateLegacyRootAsync(string root, string? userId)
    {
        SqliteDatabase database = Open(root);
        try
        {
            await InsertNoteAsync(database, NoteId(1), "2026-09-01", "Standup", "body one", 0, Utc(1), Utc(1), customTitle: true);
            await InsertNoteAsync(database, NoteId(2), "2026-09-01", "노트 2", "body two", 1, Utc(1), Utc(2));
            await SetSettingAsync(database, "ui.language", "en");
            await SetSettingAsync(database, "product.theme", "dark");
            await InsertFileAsync(database, root, NoteId(90), "2026-09-01", "report.txt", "attachment"u8.ToArray(), Utc(1));
            if (userId is not null)
            {
                await ExecAsync(
                    database,
                    "UPDATE sync_state SET user_id=$u, server_cursor=42, dek_generation=3 WHERE id=1;",
                    ("$u", userId));
                await File.WriteAllBytesAsync(Path.Combine(root, "credentials.dat"), [1, 2, 3, 4]);
            }
        }
        finally
        {
            await database.DisposeAsync();
        }

        Directory.CreateDirectory(Path.Combine(root, "conflicts"));
        await File.WriteAllTextAsync(Path.Combine(root, "conflicts", "2026-09-01-old.txt"), "lost version");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        await File.WriteAllBytesAsync(Path.Combine(root, "assets", "clip.png"), [9, 9]);
    }

    public static string NoteId(int n) => $"00000000-0000-4000-8000-{n:D12}";

    /// <summary>Relative path → SHA-256 of every file under <paramref name="root"/>.</summary>
    public static SortedDictionary<string, string> Snapshot(string root)
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            snapshot[Path.GetRelativePath(root, file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return snapshot;
    }
}
