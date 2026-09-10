using System.Globalization;
using System.Text;
using Daynote.Core.Domain;
using Daynote.Infrastructure.Assets;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Files;

/// <summary>
/// Applying attachment changes that came *from* the cloud (docs/CLOUD_SYNC.md §5, Phase 7).
/// </summary>
/// <remarks>
/// Separate from <see cref="SqliteDayFileStatements"/> because the direction changes the rules, not
/// just the caller. A local add has bytes in hand and must be queued for upload; a pulled add has
/// no bytes yet and must not be queued at all — pushing it straight back would be an echo, and on
/// two devices an endless one. Both differences are the outbox, so both live here where the reason
/// can be written down once.
/// </remarks>
internal static class SqliteDayFileMergeStatements
{
    /// <summary>What applying one pulled attachment did.</summary>
    internal enum MergeVerdict
    {
        /// <summary>The row is new here.</summary>
        Inserted,

        /// <summary>Already present. An attachment is immutable, so there is nothing to update.</summary>
        AlreadyPresent,
    }

    /// <summary>
    /// Writes a pulled attachment's row, creating the asset record its bytes will later fill.
    /// </summary>
    /// <remarks>
    /// The <c>file_assets</c> row is written before the bytes exist, and deliberately: it is what
    /// gives the download somewhere to put them, and <c>ListDayFiles</c> already renders a row whose
    /// file is missing as unavailable rather than as an error. So the attachment appears on the day
    /// immediately, greyed, and fills in when the object arrives.
    /// </remarks>
    internal static MergeVerdict Apply(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        LocalDate localDate,
        string displayName,
        long byteLength,
        string assetHash,
        DateTimeOffset createdUtc)
    {
        if (Exists(connection, transaction, id))
        {
            return MergeVerdict.AlreadyPresent;
        }

        string created = createdUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        // ON CONFLICT DO NOTHING rather than a verify-and-throw: another attachment of identical
        // content may already own this hash, with a path derived from *its* filename. The bytes are
        // the same bytes, so the existing path is the right one and the extension difference is
        // cosmetic. Refusing here would fail a sync over two files that agree.
        Execute(
            connection,
            transaction,
            "INSERT INTO file_assets(hash,relative_path,byte_length,created_utc) " +
            "VALUES($hash,$path,$length,$created) ON CONFLICT(hash) DO NOTHING;",
            ("$hash", assetHash),
            ("$path", ContentAddressedFileStore.RelativePathFor(assetHash, displayName)),
            ("$length", byteLength),
            ("$created", created));

        Execute(
            connection,
            transaction,
            "INSERT INTO day_files(id,local_date,display_name,byte_length,asset_hash,created_utc) " +
            "VALUES($id,$date,$name,$length,$hash,$created);",
            ("$id", id),
            ("$date", localDate.ToString()),
            ("$name", displayName),
            ("$length", byteLength),
            ("$hash", assetHash),
            ("$created", created));

        UpsertSearch(connection, transaction, id, localDate, displayName);

        // The AFTER INSERT trigger has just queued this for upload. It came from the server, so
        // sending it back would be an echo — and between two devices, a permanent one.
        Execute(
            connection,
            transaction,
            "DELETE FROM sync_outbox WHERE entity='file' AND entity_id=$id;",
            ("$id", id));

        return MergeVerdict.Inserted;
    }

    /// <summary>
    /// Removes an attachment because the cloud says it is gone.
    /// </summary>
    /// <returns>
    /// The asset path that no row references any more, so the caller can delete the bytes, or null
    /// when the content is still in use or was never here.
    /// </returns>
    internal static string? Remove(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id)
    {
        (string? hash, string? path) = ReadAsset(connection, transaction, id);

        Execute(
            connection,
            transaction,
            "DELETE FROM search_documents WHERE source_type='file' AND source_id=$id;",
            ("$id", id));
        Execute(connection, transaction, "DELETE FROM day_files WHERE id=$id;", ("$id", id));

        // Two queue entries to clear, for two different reasons. The tombstone was just written by
        // the AFTER DELETE trigger and must not be pushed: this delete came from the server. The
        // outbox entry may hold an upload that is now pointless.
        Execute(
            connection,
            transaction,
            "DELETE FROM sync_tombstones WHERE entity='file' AND entity_id=$id;",
            ("$id", id));
        Execute(
            connection,
            transaction,
            "DELETE FROM sync_outbox WHERE entity='file' AND entity_id=$id;",
            ("$id", id));

        if (hash is null || IsStillReferenced(connection, transaction, hash))
        {
            return null;
        }

        Execute(connection, transaction, "DELETE FROM file_assets WHERE hash=$hash;", ("$hash", hash));
        Execute(
            connection,
            transaction,
            "DELETE FROM sync_asset_queue WHERE asset_hash=$hash;",
            ("$hash", hash));
        return path;
    }

    internal static bool Exists(SqliteConnection connection, SqliteTransaction? transaction, string id)
    {
        using SqliteCommand command = Create(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM day_files WHERE id=$id);");
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static (string? Hash, string? Path) ReadAsset(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id)
    {
        using SqliteCommand command = Create(
            connection,
            transaction,
            "SELECT df.asset_hash,fa.relative_path FROM day_files df " +
            "JOIN file_assets fa ON fa.hash=df.asset_hash WHERE df.id=$id;");
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : (null, null);
    }

    private static bool IsStillReferenced(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string hash)
    {
        using SqliteCommand command = Create(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM day_files WHERE asset_hash=$hash);");
        command.Parameters.AddWithValue("$hash", hash);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static void UpsertSearch(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        LocalDate localDate,
        string displayName)
    {
        string normalized = displayName.Normalize(NormalizationForm.FormC);
        Execute(
            connection,
            transaction,
            "INSERT INTO search_documents(source_type,source_id,local_date,title,body,title_folded,body_folded) " +
            "VALUES('file',$id,$date,$title,'',$titleFolded,'') " +
            "ON CONFLICT(source_type,source_id) DO UPDATE SET local_date=excluded.local_date," +
            "title=excluded.title,title_folded=excluded.title_folded;",
            ("$id", id),
            ("$date", localDate.ToString()),
            ("$title", normalized),
            ("$titleFolded", normalized.ToUpperInvariant()));
    }

    private static SqliteCommand Create(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = Create(connection, transaction, sql);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
