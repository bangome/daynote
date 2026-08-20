using System.Globalization;
using System.Text;
using Daynote.Core.Domain;
using Daynote.Core.Files;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Files;

internal static class SqliteDayFileStatements
{
    private const string SelectColumns =
        "SELECT df.id,df.local_date,df.display_name,df.byte_length,df.asset_hash,fa.relative_path,df.created_utc " +
        "FROM day_files df JOIN file_assets fa ON fa.hash=df.asset_hash";

    public static DayFile Add(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        LocalDate localDate,
        string displayName,
        PreparedFileAsset asset,
        string createdUtc)
    {
        ValidateId(id);
        ArgumentNullException.ThrowIfNull(asset);
        UpsertAsset(connection, transaction, asset, createdUtc);
        InsertRow(connection, transaction, id, localDate, displayName, asset, createdUtc);
        UpsertSearch(connection, transaction, id, localDate, displayName);
        return new DayFile(
            id,
            localDate,
            displayName,
            asset.ByteLength,
            asset.Hash,
            asset.RelativePath,
            DateTimeOffset.Parse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            IsAvailable: true);
    }

    public static List<DayFile> ReadForDate(SqliteConnection connection, LocalDate localDate)
    {
        using SqliteCommand command = Create(connection, null,
            SelectColumns + " WHERE df.local_date=$date ORDER BY df.created_utc DESC,df.id DESC;");
        command.Parameters.AddWithValue("$date", localDate.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        var files = new List<DayFile>();
        while (reader.Read())
        {
            files.Add(ReadItem(reader));
        }

        return files;
    }

    /// <summary>
    /// Deletes a day file. <paramref name="deletedUtc"/> records the cloud-sync tombstone from the
    /// app's clock rather than letting the AFTER DELETE trigger stamp it from SQLite's <c>'now'</c>;
    /// see the note on <see cref="Notes.SqliteNoteStatements"/>.
    /// </summary>
    public static DayFileDeleteResult Delete(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        string deletedUtc)
    {
        (bool exists, string? hash, string? path) = ReadAssetIdentity(connection, transaction, id);
        if (!exists)
        {
            return new DayFileDeleteResult(false, null);
        }

        Execute(connection, transaction,
            "INSERT INTO sync_tombstones(entity,entity_id,deleted_utc) VALUES('file',$id,$utc) " +
            "ON CONFLICT(entity,entity_id) DO UPDATE SET deleted_utc=excluded.deleted_utc;",
            ("$id", FormatId(id)), ("$utc", deletedUtc));

        Execute(connection, transaction,
            "DELETE FROM search_documents WHERE source_type='file' AND source_id=$id;", ("$id", FormatId(id)));
        Execute(connection, transaction, "DELETE FROM day_files WHERE id=$id;", ("$id", FormatId(id)));
        string? releasedPath = null;
        if (hash is not null && CountReferences(connection, transaction, hash) == 0)
        {
            Execute(connection, transaction, "DELETE FROM file_assets WHERE hash=$hash;", ("$hash", hash));
            releasedPath = path;
        }

        return new DayFileDeleteResult(true, releasedPath);
    }

    public static HashSet<string> ReadReferencedPaths(SqliteConnection connection)
    {
        using SqliteCommand command = Create(connection, null,
            "SELECT DISTINCT fa.relative_path FROM file_assets fa JOIN day_files df ON df.asset_hash=fa.hash;");
        using SqliteDataReader reader = command.ExecuteReader();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    public static bool IsAssetReferenced(SqliteConnection connection, string hash)
    {
        using SqliteCommand command = Create(connection, null,
            "SELECT EXISTS(SELECT 1 FROM day_files WHERE asset_hash=$hash);");
        command.Parameters.AddWithValue("$hash", hash);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    public static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A day file ID is required.", nameof(id));
        }
    }

    private static void InsertRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        LocalDate localDate,
        string displayName,
        PreparedFileAsset asset,
        string createdUtc)
    {
        using SqliteCommand command = Create(connection, transaction,
            "INSERT INTO day_files(id,local_date,display_name,byte_length,asset_hash,created_utc) " +
            "VALUES($id,$date,$name,$length,$hash,$created);");
        Add(command, ("$id", FormatId(id)), ("$date", localDate.ToString()), ("$name", displayName),
            ("$length", asset.ByteLength), ("$hash", asset.Hash), ("$created", createdUtc));
        command.ExecuteNonQuery();
    }

    private static void UpsertAsset(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PreparedFileAsset asset,
        string createdUtc)
    {
        using (SqliteCommand command = Create(connection, transaction,
            "INSERT INTO file_assets(hash,relative_path,byte_length,created_utc) " +
            "VALUES($hash,$path,$length,$created) ON CONFLICT(hash) DO NOTHING;"))
        {
            Add(command, ("$hash", asset.Hash), ("$path", asset.RelativePath),
                ("$length", asset.ByteLength), ("$created", createdUtc));
            command.ExecuteNonQuery();
        }

        using SqliteCommand verify = Create(connection, transaction,
            "SELECT relative_path,byte_length FROM file_assets WHERE hash=$hash;");
        verify.Parameters.AddWithValue("$hash", asset.Hash);
        using SqliteDataReader reader = verify.ExecuteReader();
        if (!reader.Read() ||
            !string.Equals(reader.GetString(0), asset.RelativePath, StringComparison.Ordinal) ||
            reader.GetInt64(1) != asset.ByteLength)
        {
            throw new InvalidDataException("Existing file asset metadata does not match its content address.");
        }
    }

    private static void UpsertSearch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id,
        LocalDate localDate,
        string displayName)
    {
        string normalized = displayName.Normalize(NormalizationForm.FormC);
        using SqliteCommand command = Create(connection, transaction,
            "INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded) " +
            "VALUES('file',$id,$date,$title,'',$titleFolded,'') " +
            "ON CONFLICT(source_type,source_id) DO UPDATE SET local_date=excluded.local_date,title=excluded.title," +
            "body=excluded.body,title_folded=excluded.title_folded,body_folded=excluded.body_folded;");
        Add(command, ("$id", FormatId(id)), ("$date", localDate.ToString()), ("$title", normalized),
            ("$titleFolded", normalized.ToUpperInvariant().Normalize(NormalizationForm.FormC)));
        command.ExecuteNonQuery();
    }

    private static DayFile ReadItem(SqliteDataReader reader)
    {
        Guid id = Guid.Parse(reader.GetString(0));
        LocalDate date = LocalDate.Parse(reader.GetString(1)).Value;
        DateTimeOffset created = DateTimeOffset.Parse(
            reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new DayFile(
            id, date, reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetString(5), created);
    }

    private static (bool Exists, string? Hash, string? Path) ReadAssetIdentity(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid id)
    {
        using SqliteCommand command = Create(connection, transaction,
            "SELECT df.asset_hash,fa.relative_path FROM day_files df " +
            "LEFT JOIN file_assets fa ON fa.hash=df.asset_hash WHERE df.id=$id;");
        command.Parameters.AddWithValue("$id", FormatId(id));
        using SqliteDataReader reader = command.ExecuteReader();
        return !reader.Read()
            ? (false, null, null)
            : (true, reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static int CountReferences(SqliteConnection connection, SqliteTransaction transaction, string hash)
    {
        using SqliteCommand command = Create(connection, transaction,
            "SELECT COUNT(*) FROM day_files WHERE asset_hash=$hash;");
        command.Parameters.AddWithValue("$hash", hash);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = Create(connection, transaction, sql);
        Add(command, parameters);
        command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, params (string Name, object Value)[] parameters)
    {
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
    }

    private static SqliteCommand Create(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static string FormatId(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
