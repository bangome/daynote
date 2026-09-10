using Daynote.Core.Sync;
using Daynote.Infrastructure.Files;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The attachment side of the local sync store (docs/CLOUD_SYNC.md §5, Phase 7).
/// </summary>
/// <remarks>
/// Two jobs. Merging pulled attachment rows into <c>day_files</c>, and keeping
/// <c>sync_asset_queue</c> — the list of content hashes whose bytes still have to move. The queue
/// is a table rather than a column on <c>file_assets</c> for the same reason the outbox is not a
/// flag on <c>notes</c>: a separate row cannot be forgotten by the next writer, including the MCP
/// server, which shares this database.
/// </remarks>
public sealed partial class SqliteSyncStore
{
    public ValueTask<FileMergeOutcome> MergeFilesAsync(
        IReadOnlyList<SyncFile> files,
        IReadOnlyList<SyncTombstone> tombstones,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(tombstones);
        if (files.Count == 0 && tombstones.Count == 0)
        {
            return ValueTask.FromResult(FileMergeOutcome.Empty);
        }

        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();

                int applied = 0;
                int ignored = 0;
                int removed = 0;
                var downloadable = new List<string>();

                // Deletes first, exactly as the note merge does it: a delete that ran after an add
                // must not be undone by that add being replayed in the same page.
                foreach (SyncTombstone tombstone in tombstones)
                {
                    if (tombstone.Kind != SyncEntityKind.File)
                    {
                        continue;
                    }

                    if (!SqliteDayFileMergeStatements.Exists(connection, transaction, tombstone.Id))
                    {
                        // Gone here too. Drop our own tombstone: both sides agree, and keeping it
                        // would push a delete the server has already recorded.
                        Execute(
                            connection,
                            transaction,
                            "DELETE FROM sync_tombstones WHERE entity='file' AND entity_id=$id;",
                            ("$id", tombstone.Id));
                        continue;
                    }

                    SqliteDayFileMergeStatements.Remove(connection, transaction, tombstone.Id);
                    removed += 1;
                }

                foreach (SyncFile file in files)
                {
                    SqliteDayFileMergeStatements.MergeVerdict verdict = SqliteDayFileMergeStatements.Apply(
                        connection,
                        transaction,
                        file.Id,
                        file.LocalDate,
                        file.DisplayName,
                        file.ByteLength,
                        file.AssetHash,
                        file.CreatedUtc);

                    if (verdict == SqliteDayFileMergeStatements.MergeVerdict.Inserted)
                    {
                        applied += 1;
                    }
                    else
                    {
                        // An attachment is immutable — added or removed, never edited — so a second
                        // delivery of one we already hold is an echo, not a change.
                        ignored += 1;
                    }

                    // Asked for whether the row was new or not: the row can exist from an earlier
                    // run whose download had not finished, and the bytes are what is missing.
                    downloadable.Add(file.AssetHash);
                }

                return new FileMergeOutcome(applied, ignored, removed, downloadable);
            },
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<string>> ReadAssetQueueAsync(
        AssetDirection direction,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        // Fewest attempts first, so one asset that fails every time cannot stand in front of the
        // ones behind it and stall the queue for good.
        command.CommandText =
            """
            SELECT asset_hash FROM sync_asset_queue
             WHERE direction = $direction
             ORDER BY attempts, asset_hash
             LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$direction", WireDirection(direction));
        command.Parameters.AddWithValue("$limit", limit);

        var queued = new List<string>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            queued.Add(reader.GetString(0));
        }

        return queued;
    }

    public ValueTask EnqueueAssetAsync(
        string assetHash,
        AssetDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetHash);

        return AsVoid(database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                // The direction is updated and the attempt count is not, and both halves matter.
                // Updating: one row per hash, so a hash left behind by a failed *upload* would
                // otherwise keep its 'up' direction and never be picked up by the download pass —
                // the attachment would stay unavailable on this device for good. Not resetting the
                // attempts: a page pulled repeatedly would keep a broken download permanently at
                // the front of the queue.
                return Execute(
                    connection,
                    transaction,
                    """
                    INSERT INTO sync_asset_queue(asset_hash, direction) VALUES ($hash, $direction)
                        ON CONFLICT(asset_hash) DO UPDATE SET direction = excluded.direction;
                    """,
                    ("$hash", assetHash),
                    ("$direction", WireDirection(direction)));
            },
            cancellationToken));
    }

    public ValueTask DequeueAssetAsync(string assetHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetHash);

        return AsVoid(database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Execute(
                    connection,
                    transaction,
                    "DELETE FROM sync_asset_queue WHERE asset_hash = $hash;",
                    ("$hash", assetHash));
            },
            cancellationToken));
    }

    public ValueTask RecordAssetFailureAsync(
        string assetHash,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetHash);
        ArgumentNullException.ThrowIfNull(error);

        return AsVoid(database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                // Upserts rather than updates, because an upload failure has no queue row of its
                // own: uploads are driven by the outbox, and this is where a repeatedly failing one
                // becomes visible instead of retrying silently forever. A download failure always
                // has a row already, so the conflict branch runs and 'up' below never overwrites
                // its direction — the insert's value applies only when there was nothing there.
                return Execute(
                    connection,
                    transaction,
                    """
                    INSERT INTO sync_asset_queue(asset_hash, direction, attempts, last_error)
                    VALUES ($hash, 'up', 1, $error)
                        ON CONFLICT(asset_hash) DO UPDATE SET
                            attempts = attempts + 1,
                            last_error = excluded.last_error;
                    """,
                    ("$hash", assetHash),
                    ("$error", error));
            },
            cancellationToken));
    }

    private static string WireDirection(AssetDirection direction) =>
        direction == AssetDirection.Up ? "up" : "down";
}
