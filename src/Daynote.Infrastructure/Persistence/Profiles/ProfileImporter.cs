using System.Globalization;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Settings;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Assets;
using Daynote.Infrastructure.Backup;
using Daynote.Infrastructure.Files;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Sync;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence.Profiles;

/// <summary>What <see cref="ProfileImporter.ImportFromAsync"/> brought over.</summary>
/// <param name="NotesImported">Notes written into the destination (new, or newer than its copy).</param>
/// <param name="NotesKept">Notes the destination already had in a version at least as new, or deleted later.</param>
/// <param name="FilesImported">Attachments added to the destination.</param>
/// <param name="FilesAlreadyPresent">Attachments the destination already had under the same id.</param>
/// <param name="FilesSkipped">
/// Attachments left behind: their bytes were never on this device (pulled but not yet downloaded), or
/// the destination deleted that id after it was added.
/// </param>
/// <param name="BlobsCopied">Attachment files physically copied; identical content is stored once.</param>
/// <param name="ConflictCopies">Destination notes a newer imported version replaced, saved to its conflicts folder.</param>
public sealed record ProfileImportResult(
    int NotesImported,
    int NotesKept,
    int FilesImported,
    int FilesAlreadyPresent,
    int FilesSkipped,
    int BlobsCopied,
    int ConflictCopies)
{
    public static ProfileImportResult Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>What a profile holds that its user wrote: the untouched first-run sample does not count.</summary>
public readonly record struct ProfileContent(int Notes, int Files)
{
    public bool IsEmpty => Notes == 0 && Files == 0;
}

/// <summary>
/// Copies one profile's notes and attachments into another on the same device (docs/PROFILES.md §5.2
/// step 4, §5.5): local into an account when the user chooses to bring their notes along, and an
/// account into local when a deleted account's notes are kept.
/// </summary>
/// <remarks>
/// <para>
/// Everything goes through the destination's own writers — <see cref="SqliteSyncStore.ImportNotesAsync"/>
/// for notes and the day-file statements for attachments — so the destination's outbox triggers queue
/// it exactly as if it had been written there, and an account pushes what it received on its next sync.
/// </para>
/// <para>
/// Note ids are UUIDs, so an id present on both sides is the same note, and the later
/// <c>updated_utc</c> wins, as in a pull; a destination version that loses is written to its conflicts
/// folder rather than lost. Attachments are immutable, so an id already there is simply kept.
/// </para>
/// <para>
/// The untouched first-run sample is not imported: it is scaffolding, not something the user wrote,
/// and an account that received it from every device would collect a copy per device.
/// </para>
/// </remarks>
public sealed class ProfileImporter
{
    private readonly SqliteDatabase _destination;
    private readonly string _destinationRoot;
    private readonly Func<DateTimeOffset>? _utcNow;

    /// <param name="destination">The destination profile's open, initialized database.</param>
    /// <param name="destinationRoot">The destination profile's folder (for attachments and conflict copies).</param>
    /// <param name="utcNow">Clock for re-ordered notes; the system clock when null.</param>
    public ProfileImporter(SqliteDatabase destination, string destinationRoot, Func<DateTimeOffset>? utcNow = null)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        _destinationRoot = Path.GetFullPath(destinationRoot);
        _utcNow = utcNow;
    }

    /// <summary>
    /// Imports everything the profile in <paramref name="sourceRoot"/> holds. Leaves the source as it
    /// is: clearing it after a <i>Move</i> is the caller's decision, made once this has succeeded.
    /// </summary>
    public async ValueTask<ProfileImportResult> ImportFromAsync(
        string sourceRoot,
        CancellationToken cancellationToken = default) =>
        (await ImportCoreAsync(sourceRoot, cancellationToken).ConfigureAwait(false)).Result;

    /// <summary>
    /// Imports everything the profile in <paramref name="sourceRoot"/> holds, then removes from the
    /// source exactly what was imported (docs/PROFILES.md §5.2 step 4, <i>Move</i>), so the notes are
    /// not in two places.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A note is removed only if the source still holds the version that was read: one edited in the
    /// meantime (the MCP server shares that database) stays, and the next import takes it. The
    /// untouched first-run sample was never imported and so is never removed, and neither is an
    /// attachment the destination did not end up holding (its bytes were never on this device).
    /// </para>
    /// <para>
    /// The removal leaves no tombstones and no outbox entries behind. The source is the local profile,
    /// which never syncs, and a tombstone there would make a later import in the other direction —
    /// a deleted account's notes kept as local notes (§5.5) — read these notes as deleted since.
    /// </para>
    /// </remarks>
    public async ValueTask<ProfileImportResult> MoveFromAsync(
        string sourceRoot,
        CancellationToken cancellationToken = default)
    {
        (ProfileImportResult result, List<SyncNote> notes, List<SourceFile> files) =
            await ImportCoreAsync(sourceRoot, cancellationToken).ConfigureAwait(false);

        // Only what the destination now holds: an attachment skipped because its bytes were never on
        // this device (or deleted there later) did not move, so it must not be cleared here either.
        files = [.. files.Where(static file => file.Arrived)];
        if (notes.Count == 0 && files.Count == 0)
        {
            return result;
        }

        string source = Path.GetFullPath(sourceRoot);
        var sourceDatabase = new SqliteDatabase(
            new SqliteDatabaseOptions(Path.Combine(source, ProfileStore.DatabaseFileName)));
        IReadOnlyList<string> released;
        try
        {
            sourceDatabase.Initialize();
            released = await sourceDatabase.WriteAsync(
                (connection, transaction, token) => RemoveMoved(connection, transaction, notes, files, token),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await sourceDatabase.DisposeAsync().ConfigureAwait(false);
        }

        string sourceFiles = Path.Combine(source, BackupService.FilesDirName);
        foreach (string relative in released)
        {
            // After the commit: a blob deleted before it would be missing if the transaction rolled back.
            if (Inside(sourceFiles, relative) is { } blob)
            {
                try
                {
                    File.Delete(blob);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return result;
    }

    private async ValueTask<(ProfileImportResult Result, List<SyncNote> Notes, List<SourceFile> Files)> ImportCoreAsync(
        string sourceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        string source = Path.GetFullPath(sourceRoot);
        if (string.Equals(
            Path.TrimEndingDirectorySeparator(source),
            Path.TrimEndingDirectorySeparator(_destinationRoot),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A profile cannot import from itself.", nameof(sourceRoot));
        }

        string sourceDatabasePath = Path.Combine(source, ProfileStore.DatabaseFileName);
        if (!File.Exists(sourceDatabasePath))
        {
            return (ProfileImportResult.Empty, [], []);
        }

        List<SyncNote> notes;
        List<SourceFile> files;
        var sourceDatabase = new SqliteDatabase(new SqliteDatabaseOptions(sourceDatabasePath));
        try
        {
            // Initialize, not a bare connection: it brings an older source up to the current schema,
            // so the columns read below exist whatever version last wrote it.
            sourceDatabase.Initialize();
            using SqliteConnection connection = sourceDatabase.OpenReadConnection();
            notes = ReadNotes(connection);
            files = ReadFiles(connection);
        }
        finally
        {
            await sourceDatabase.DisposeAsync().ConfigureAwait(false);
        }

        var store = new SqliteSyncStore(_destination, _utcNow);
        MergeOutcome merged = await store.ImportNotesAsync(notes, cancellationToken).ConfigureAwait(false);
        if (merged.Displaced.Count > 0)
        {
            await new FileSystemConflictSink(_destinationRoot)
                .SaveAsync(merged.Displaced, cancellationToken)
                .ConfigureAwait(false);
        }

        (int imported, int present, int skipped, int copied) =
            await ImportFilesAsync(source, files, cancellationToken).ConfigureAwait(false);

        return (
            new ProfileImportResult(
                merged.Applied,
                merged.Ignored,
                imported,
                present,
                skipped,
                copied,
                merged.Displaced.Count),
            notes,
            files);
    }

    /// <summary>
    /// Deletes the moved notes and attachments from the source through the ordinary delete statements
    /// (search rows, tags, custom titles, unreferenced assets), then drops the tombstones and outbox
    /// entries those deletes leave. Returns the blob paths no row references any more.
    /// </summary>
    private static List<string> RemoveMoved(
        SqliteConnection connection,
        SqliteTransaction transaction,
        List<SyncNote> notes,
        List<SourceFile> files,
        CancellationToken cancellationToken)
    {
        string now = SyncTimestamps.ToLocal(DateTimeOffset.UtcNow);
        foreach (SyncNote note in notes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (SqliteCommand unchanged = connection.CreateCommand())
            {
                unchanged.Transaction = transaction;
                unchanged.CommandText = "SELECT updated_utc FROM notes WHERE id=$id;";
                unchanged.Parameters.AddWithValue("$id", note.Id);
                if (unchanged.ExecuteScalar() is not string stored
                    || !SyncTimestamps.TryParseLocal(stored, out DateTimeOffset updated)
                    || updated != note.UpdatedUtc)
                {
                    continue;
                }
            }

            SqliteNoteStatements.Delete(connection, transaction, NoteId.Create(Guid.Parse(note.Id)).Value, now);
            Forget(connection, transaction, "note", note.Id);
        }

        var released = new List<string>();
        foreach (SourceFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DayFileDeleteResult deleted = SqliteDayFileStatements.Delete(connection, transaction, Guid.Parse(file.Id), now);
            Forget(connection, transaction, "file", file.Id);
            if (deleted.ReleasedAssetPath is { } path)
            {
                released.Add(path);
            }
        }

        return released;
    }

    private static void Forget(SqliteConnection connection, SqliteTransaction transaction, string entity, string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM sync_tombstones WHERE entity=$entity AND entity_id=$id; " +
            "DELETE FROM sync_outbox WHERE entity=$entity AND entity_id=$id;";
        command.Parameters.AddWithValue("$entity", entity);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Counts what the user wrote in a profile, for the "move N notes to this account?" question
    /// (docs/PROFILES.md §5.2 step 2). The first-run sample counts only once it has been edited.
    /// </summary>
    public static ProfileContent CountUserContent(SqliteDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        using SqliteConnection connection = database.OpenReadConnection();
        (string? sampleId, string? sampleBody) = ReadSample(connection);

        int notes;
        using (SqliteCommand command = connection.CreateCommand())
        {
            if (sampleId is null || sampleBody is null)
            {
                command.CommandText = "SELECT COUNT(*) FROM notes;";
            }
            else
            {
                command.CommandText = "SELECT COUNT(*) FROM notes WHERE NOT (lower(id)=lower($id) AND body=$body);";
                command.Parameters.AddWithValue("$id", sampleId);
                command.Parameters.AddWithValue("$body", sampleBody);
            }

            notes = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using SqliteCommand files = connection.CreateCommand();
        files.CommandText = "SELECT COUNT(*) FROM day_files;";
        return new ProfileContent(notes, Convert.ToInt32(files.ExecuteScalar(), CultureInfo.InvariantCulture));
    }

    private async ValueTask<(int Imported, int Present, int Skipped, int Copied)> ImportFilesAsync(
        string source,
        List<SourceFile> files,
        CancellationToken cancellationToken)
    {
        int imported = 0;
        int present = 0;
        int skipped = 0;
        int copied = 0;
        if (files.Count == 0)
        {
            return (imported, present, skipped, copied);
        }

        string sourceFiles = Path.Combine(source, BackupService.FilesDirName);
        string destinationFiles = Path.Combine(_destinationRoot, BackupService.FilesDirName);
        var assets = new ContentAddressedFileStore(_destinationRoot);

        foreach (SourceFile file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DestinationState state = ReadDestinationState(file);
            if (state.RowExists)
            {
                present += 1;
                file.Arrived = true;
                continue;
            }

            if (state.DeletedUtc is { } deleted && deleted > file.CreatedUtc)
            {
                // Deleted here after it was added there: last write wins, and the delete is later.
                skipped += 1;
                continue;
            }

            string? blob = Inside(sourceFiles, file.RelativePath);
            if (blob is null || !File.Exists(blob))
            {
                skipped += 1;
                continue;
            }

            PreparedFileAsset asset;
            if (state.AssetPath is { } existingPath)
            {
                // The destination already knows this content, possibly as a pulled row still waiting for
                // its bytes, under a path derived from another filename. Fill that path rather than
                // creating a second one the day-file writer would reject as inconsistent.
                string? target = Inside(destinationFiles, existingPath);
                if (target is null)
                {
                    skipped += 1;
                    continue;
                }

                bool createdNew = false;
                if (!File.Exists(target))
                {
                    CopyAtomically(blob, target);
                    createdNew = true;
                }

                asset = new PreparedFileAsset(file.AssetHash, existingPath, state.AssetLength, createdNew);
            }
            else
            {
                FileStream stream = File.OpenRead(blob);
                await using (stream.ConfigureAwait(false))
                {
                    asset = await assets
                        .PrepareAsync(stream, Path.GetExtension(file.RelativePath), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            bool added = await _destination.WriteAsync(
                (connection, transaction, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (SqliteDayFileMergeStatements.Exists(connection, transaction, file.Id))
                    {
                        return false;
                    }

                    // The original timestamp, not now: the attachment was added then, and a later
                    // tombstone on another device must still be able to win against it.
                    SqliteDayFileStatements.Add(
                        connection,
                        transaction,
                        Guid.Parse(file.Id),
                        file.LocalDate,
                        file.DisplayName,
                        asset,
                        file.CreatedUtcText);
                    return true;
                },
                cancellationToken).ConfigureAwait(false);

            if (added)
            {
                imported += 1;
                copied += asset.CreatedNew ? 1 : 0;
            }
            else
            {
                present += 1;
            }

            file.Arrived = true;
        }

        return (imported, present, skipped, copied);
    }

    private DestinationState ReadDestinationState(SourceFile file)
    {
        using SqliteConnection connection = _destination.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(SELECT 1 FROM day_files WHERE id=$id),
                   (SELECT deleted_utc FROM sync_tombstones WHERE entity='file' AND entity_id=$id),
                   (SELECT relative_path FROM file_assets WHERE hash=$hash),
                   (SELECT byte_length FROM file_assets WHERE hash=$hash);
            """;
        command.Parameters.AddWithValue("$id", file.Id);
        command.Parameters.AddWithValue("$hash", file.AssetHash);
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return new DestinationState(
            reader.GetInt32(0) != 0,
            reader.IsDBNull(1) ? null : ReadTimestamp(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt64(3));
    }

    private static List<SyncNote> ReadNotes(SqliteConnection connection)
    {
        (string? sampleId, string? sampleBody) = ReadSample(connection);

        var tags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT note_id, tag FROM note_tags ORDER BY note_id, sort_order;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string noteId = reader.GetString(0);
                if (!tags.TryGetValue(noteId, out List<string>? list))
                {
                    list = [];
                    tags[noteId] = list;
                }

                list.Add(reader.GetString(1));
            }
        }

        var notes = new List<SyncNote>();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT n.id, n.local_date, n.title, n.body, n.sort_order, n.is_favorite,
                       n.created_utc, n.updated_utc,
                       EXISTS(SELECT 1 FROM settings WHERE key = 'note.custom-title.' || n.id)
                  FROM notes n
                 ORDER BY n.local_date, n.sort_order;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                string body = reader.GetString(3);
                if (sampleId is not null
                    && string.Equals(id, sampleId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(body, sampleBody, StringComparison.Ordinal))
                {
                    continue;
                }

                bool hasCustomTitle = reader.GetInt32(8) != 0;
                int sortOrder = reader.GetInt32(4);
                notes.Add(new SyncNote(
                    id,
                    LocalDate.Parse(reader.GetString(1)).Value,
                    // Resolved the way the outbox reader resolves it, so the merge compares like with
                    // like when it decides whether a replaced version is worth a conflict copy.
                    hasCustomTitle ? reader.GetString(2) : UntitledNote.TitleFor(sortOrder + 1),
                    body,
                    sortOrder,
                    reader.GetInt32(5) != 0,
                    hasCustomTitle,
                    tags.TryGetValue(id, out List<string>? noteTags) ? noteTags : [],
                    ReadTimestamp(reader.GetString(6)),
                    ReadTimestamp(reader.GetString(7))));
            }
        }

        return notes;
    }

    private static List<SourceFile> ReadFiles(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT df.id, df.local_date, df.display_name, df.asset_hash, df.created_utc, fa.relative_path
              FROM day_files df
              JOIN file_assets fa ON fa.hash = df.asset_hash
             ORDER BY df.created_utc, df.id;
            """;
        var files = new List<SourceFile>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string created = reader.GetString(4);
            files.Add(new SourceFile(
                reader.GetString(0),
                LocalDate.Parse(reader.GetString(1)).Value,
                reader.GetString(2),
                reader.GetString(3),
                created,
                ReadTimestamp(created),
                reader.GetString(5)));
        }

        return files;
    }

    private static (string? Id, string? Body) ReadSample(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT value FROM settings WHERE key=$idKey), (SELECT value FROM settings WHERE key=$bodyKey);";
        command.Parameters.AddWithValue("$idKey", OnboardingSettings.SampleNoteIdKey);
        command.Parameters.AddWithValue("$bodyKey", OnboardingSettings.SampleNoteBodyKey);
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    /// <summary>
    /// The absolute path of <paramref name="relativePath"/> under <paramref name="root"/>, or null when
    /// it would land outside it. The relative path comes out of a database, so it is not trusted to
    /// stay in its folder.
    /// </summary>
    private static string? Inside(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static void CopyAtomically(string from, string to)
    {
        string directory = Path.GetDirectoryName(to)
            ?? throw new InvalidOperationException("The path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(from, temporary);
            File.Move(temporary, to, overwrite: false);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    private static DateTimeOffset ReadTimestamp(string value) =>
        SyncTimestamps.TryParseLocal(value, out DateTimeOffset parsed)
            ? parsed
            : throw new InvalidOperationException($"A stored timestamp could not be read: '{value}'.");

    private sealed record SourceFile(
        string Id,
        LocalDate LocalDate,
        string DisplayName,
        string AssetHash,
        string CreatedUtcText,
        DateTimeOffset CreatedUtc,
        string RelativePath)
    {
        /// <summary>Set once the destination holds this attachment, imported now or already there.</summary>
        public bool Arrived { get; set; }
    }

    private readonly record struct DestinationState(
        bool RowExists,
        DateTimeOffset? DeletedUtc,
        string? AssetPath,
        long AssetLength);
}
