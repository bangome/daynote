using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The local half of sync: what still needs pushing, what a pull changed, and how far we have read.
/// Holds no crypto and performs no I/O beyond SQLite — the engine above it encrypts and transports.
/// </summary>
/// <remarks>
/// Timestamps cross this boundary as <see cref="DateTimeOffset"/>, never as strings. Comparing the
/// local <c>"O"</c> format against the wire format as text gets the ordering wrong (see
/// <see cref="SyncTimestamps"/>), so conversion happens only here and in the transport.
/// </remarks>
public sealed class SqliteSyncStore
{
    /// <summary>
    /// Temporary slot base used while re-ordering a date. Larger than any real note count, so a
    /// uniform shift by this amount cannot collide with an unshifted row under
    /// <c>UNIQUE (local_date, sort_order)</c>. Sort orders are always dense 0..n-1 at rest.
    /// </summary>
    private const int ParkingOffset = 1_000_000;

    private readonly SqliteDatabase database;
    private readonly Func<DateTimeOffset> utcNow;

    public SqliteSyncStore(SqliteDatabase database, Func<DateTimeOffset>? utcNow = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Queues everything already on this PC for its first push. Needed because the outbox is
    /// maintained by triggers, which only see writes made after migration 004 — content that existed
    /// before it has no queue entry and would otherwise never reach the cloud.
    /// </summary>
    public ValueTask<int> EnrollExistingContentAsync(CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            static (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                int queued = Execute(
                    connection,
                    transaction,
                    """
                    -- OR IGNORE rather than an upsert clause: SQLite will not parse
                    -- `ON CONFLICT` after a bare SELECT, and keeping an existing entry untouched is
                    -- exactly what we want anyway.
                    INSERT OR IGNORE INTO sync_outbox(entity, entity_id, queued_utc)
                    SELECT 'note', id, updated_utc FROM notes;
                    """);
                queued += Execute(
                    connection,
                    transaction,
                    """
                    INSERT OR IGNORE INTO sync_outbox(entity, entity_id, queued_utc)
                    SELECT 'file', id, created_utc FROM day_files;
                    """);
                return queued;
            },
            cancellationToken);

    public async ValueTask<IReadOnlyList<PendingNote>> ReadPendingNotesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = database.OpenReadConnection();
        var pending = new List<PendingNote>();
        var order = new List<string>();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT n.id, n.local_date, n.title, n.body, n.sort_order, n.is_favorite,
                       n.created_utc, n.updated_utc, o.queued_utc,
                       EXISTS(SELECT 1 FROM settings WHERE key = 'note.custom-title.' || n.id)
                  FROM sync_outbox o
                  JOIN notes n ON n.id = o.entity_id
                 WHERE o.entity = 'note'
                 ORDER BY o.queued_utc, n.id
                 LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                bool hasCustomTitle = reader.GetInt32(9) != 0;
                int sortOrder = reader.GetInt32(4);
                pending.Add(new PendingNote(
                    new SyncNote(
                        id,
                        LocalDate.Parse(reader.GetString(1)).Value,
                        // The stored title is a placeholder when the user never named the note, so
                        // resolve it the same way the workspace reader does.
                        hasCustomTitle ? reader.GetString(2) : UntitledNote.TitleFor(sortOrder + 1),
                        reader.GetString(3),
                        sortOrder,
                        reader.GetInt32(5) != 0,
                        hasCustomTitle,
                        [],
                        ReadTimestamp(reader.GetString(6)),
                        ReadTimestamp(reader.GetString(7))),
                    ReadTimestamp(reader.GetString(8))));
                order.Add(id);
            }
        }

        if (pending.Count == 0)
        {
            return pending;
        }

        Dictionary<string, List<string>> tags = ReadTags(connection, order);
        for (int i = 0; i < pending.Count; i += 1)
        {
            if (tags.TryGetValue(pending[i].Note.Id, out List<string>? noteTags))
            {
                pending[i] = pending[i] with { Note = pending[i].Note with { Tags = noteTags } };
            }
        }

        return pending;
    }

    /// <summary>
    /// Attachment metadata awaiting push. The bytes and the merge side arrive with the R2 work in a
    /// later phase; this exists so the outbox can be drained rather than growing unbounded.
    /// </summary>
    public async ValueTask<IReadOnlyList<PendingFile>> ReadPendingFilesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT f.id, f.local_date, f.display_name, f.byte_length, f.asset_hash, f.created_utc,
                   o.queued_utc
              FROM sync_outbox o
              JOIN day_files f ON f.id = o.entity_id
             WHERE o.entity = 'file'
             ORDER BY o.queued_utc, f.id
             LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var pending = new List<PendingFile>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            pending.Add(new PendingFile(
                new SyncFile(
                    reader.GetString(0),
                    LocalDate.Parse(reader.GetString(1)).Value,
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    ReadTimestamp(reader.GetString(5))),
                ReadTimestamp(reader.GetString(6))));
        }

        return pending;
    }

    public async ValueTask<IReadOnlyList<SyncTombstone>> ReadPendingTombstonesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT entity, entity_id, deleted_utc FROM sync_tombstones
             ORDER BY deleted_utc, entity_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var tombstones = new List<SyncTombstone>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            tombstones.Add(new SyncTombstone(
                ParseKind(reader.GetString(0)),
                reader.GetString(1),
                ReadTimestamp(reader.GetString(2))));
        }

        return tombstones;
    }

    /// <summary>
    /// Removes outbox entries the server has accepted. Conditional on <c>queued_utc</c>: an edit made
    /// while the push was in flight leaves a newer queue entry, which must survive or that edit is
    /// silently never sent. Returns how many entries were actually removed.
    /// </summary>
    public ValueTask<int> AcknowledgePushAsync(
        IReadOnlyList<PendingAck> acknowledged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledged);
        if (acknowledged.Count == 0)
        {
            return ValueTask.FromResult(0);
        }

        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                int cleared = 0;
                foreach (PendingAck ack in acknowledged)
                {
                    cleared += Execute(
                        connection,
                        transaction,
                        """
                        DELETE FROM sync_outbox
                         WHERE entity = $entity AND entity_id = $id AND queued_utc = $queued;
                        """,
                        ("$entity", WireKind(ack.Kind)),
                        ("$id", ack.Id),
                        ("$queued", SyncTimestamps.ToLocal(ack.QueuedUtc)));
                }

                return cleared;
            },
            cancellationToken);
    }

    public ValueTask<int> AcknowledgeTombstonesAsync(
        IReadOnlyList<SyncTombstone> acknowledged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledged);
        if (acknowledged.Count == 0)
        {
            return ValueTask.FromResult(0);
        }

        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                int cleared = 0;
                foreach (SyncTombstone tombstone in acknowledged)
                {
                    cleared += Execute(
                        connection,
                        transaction,
                        """
                        DELETE FROM sync_tombstones
                         WHERE entity = $entity AND entity_id = $id AND deleted_utc = $deleted;
                        """,
                        ("$entity", WireKind(tombstone.Kind)),
                        ("$id", tombstone.Id),
                        ("$deleted", SyncTimestamps.ToLocal(tombstone.DeletedUtc)));
                }

                return cleared;
            },
            cancellationToken);
    }

    /// <summary>
    /// Applies pulled notes and tombstones under last-write-wins, then restores a dense sort order on
    /// every date it touched. Returns what was applied, what was ignored as stale, and any local note
    /// bodies the merge destroyed so the caller can write them to the conflicts folder first.
    /// </summary>
    public ValueTask<MergeOutcome> MergeNotesAsync(
        IReadOnlyList<SyncNote> notes,
        IReadOnlyList<SyncTombstone> tombstones,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(tombstones);
        if (notes.Count == 0 && tombstones.Count == 0)
        {
            return ValueTask.FromResult(MergeOutcome.Empty);
        }

        DateTimeOffset mergeInstant = utcNow();

        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();

                var displaced = new List<DisplacedNote>();
                var affectedDates = new HashSet<string>(StringComparer.Ordinal);
                var mergedIds = new HashSet<string>(StringComparer.Ordinal);
                var incomingByDate = new Dictionary<string, List<SyncNote>>(StringComparer.Ordinal);
                int applied = 0;
                int ignored = 0;
                int removed = 0;

                foreach (SyncTombstone tombstone in tombstones)
                {
                    if (tombstone.Kind != SyncEntityKind.Note)
                    {
                        continue;
                    }

                    LocalNoteRow? local = ReadNoteRow(connection, transaction, tombstone.Id);
                    if (local is null)
                    {
                        // Already gone here too. Drop our own tombstone: both sides agree, and
                        // keeping it would push a delete the server has already recorded.
                        DeleteTombstone(connection, transaction, SyncEntityKind.Note, tombstone.Id);
                        continue;
                    }

                    if (local.Value.UpdatedUtc > tombstone.DeletedUtc)
                    {
                        // A local edit outlives the remote delete. The outbox still holds it, so the
                        // next push re-creates the note on the server.
                        ignored += 1;
                        continue;
                    }

                    displaced.Add(ToDisplaced(local.Value));
                    DeleteNote(connection, transaction, tombstone.Id, tombstone.DeletedUtc);
                    // The AFTER DELETE trigger just wrote a tombstone of its own. Remove it: this
                    // delete came *from* the server and must not be pushed back to it.
                    DeleteTombstone(connection, transaction, SyncEntityKind.Note, tombstone.Id);
                    affectedDates.Add(local.Value.LocalDate);
                    removed += 1;
                }

                foreach (SyncNote incoming in notes)
                {
                    DateTimeOffset? localDelete =
                        ReadTombstone(connection, transaction, SyncEntityKind.Note, incoming.Id);
                    if (localDelete is { } deletedAt && deletedAt > incoming.UpdatedUtc)
                    {
                        // We deleted it after this version was written. Keep the tombstone queued.
                        ignored += 1;
                        continue;
                    }

                    LocalNoteRow? local = ReadNoteRow(connection, transaction, incoming.Id);
                    if (local is { } current && current.UpdatedUtc >= incoming.UpdatedUtc)
                    {
                        // Equal timestamps mean the same version of the same id, so there is nothing
                        // to choose between: keeping local is the deterministic answer.
                        ignored += 1;
                        continue;
                    }

                    if (local is { } losing)
                    {
                        // Only a real content difference is worth preserving; a sort-order-only
                        // change would otherwise fill the conflicts folder with noise.
                        if (!string.Equals(losing.Body, incoming.Body, StringComparison.Ordinal) ||
                            !string.Equals(losing.EffectiveTitle, incoming.Title, StringComparison.Ordinal))
                        {
                            displaced.Add(ToDisplaced(losing));
                        }

                        if (!string.Equals(losing.LocalDate, incoming.LocalDate.ToString(), StringComparison.Ordinal))
                        {
                            affectedDates.Add(losing.LocalDate);
                        }
                    }

                    if (localDelete is not null)
                    {
                        DeleteTombstone(connection, transaction, SyncEntityKind.Note, incoming.Id);
                    }

                    string date = incoming.LocalDate.ToString();
                    affectedDates.Add(date);
                    mergedIds.Add(incoming.Id);
                    if (!incomingByDate.TryGetValue(date, out List<SyncNote>? bucket))
                    {
                        bucket = [];
                        incomingByDate[date] = bucket;
                    }

                    bucket.Add(incoming);
                    applied += 1;
                }

                // Snapshot the queue before touching anything. Re-ordering a date rewrites every note
                // on it, which fires the outbox trigger for notes the merge did not really change; the
                // cleanup in Resequence undoes that, and needs to know which entries were already
                // there. Without this, merging one note would discard a sibling note's pending local
                // edit and that edit would never reach the cloud.
                HashSet<string> alreadyQueued = ReadQueuedNoteIds(connection, transaction);

                // Park, write, then re-order: the UNIQUE (local_date, sort_order) constraint cannot be
                // deferred in SQLite, so two devices both adding a note at slot 0 would collide on a
                // naive insert. See docs/CLOUD_SYNC.md §6.1.
                foreach (string date in affectedDates)
                {
                    ParkDate(connection, transaction, date);
                }

                foreach (List<SyncNote> bucket in incomingByDate.Values)
                {
                    int parkedSlot = ParkingOffset * 2;
                    foreach (SyncNote incoming in bucket)
                    {
                        WriteMergedNote(connection, transaction, incoming, parkedSlot);
                        parkedSlot += 1;
                    }
                }

                foreach (string date in affectedDates)
                {
                    Resequence(
                        connection,
                        transaction,
                        date,
                        mergedIds,
                        alreadyQueued,
                        incomingByDate,
                        mergeInstant);
                }

                // Never queue what we just received: the triggers fired on every write above.
                foreach (string id in mergedIds)
                {
                    DeleteOutbox(connection, transaction, SyncEntityKind.Note, id);
                }

                return new MergeOutcome(applied, ignored, removed, displaced);
            },
            cancellationToken);
    }

    public async ValueTask<SyncStateSnapshot> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = database.OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT user_id, server_cursor, dek_generation, locked, last_sync_utc FROM sync_state WHERE id = 1;";
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("The sync_state row is missing; migration 004 did not run.");
        }

        DateTimeOffset? lastSync = reader.IsDBNull(4) ? null : ReadTimestamp(reader.GetString(4));
        return new SyncStateSnapshot(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.GetInt32(3) != 0,
            lastSync);
    }

    public ValueTask SignInAsync(
        string userId,
        int dekGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return AsVoid(database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Execute(
                    connection,
                    transaction,
                    """
                    UPDATE sync_state
                       SET user_id = $user, dek_generation = $generation, locked = 0
                     WHERE id = 1;
                    """,
                    ("$user", userId),
                    ("$generation", dekGeneration));
            },
            cancellationToken));
    }

    /// <summary>
    /// Clears the account and the pull cursor, but deliberately leaves the outbox and tombstones
    /// alone: local content is still local content, and signing back in should push whatever never
    /// made it out rather than losing track of it.
    /// </summary>
    public ValueTask SignOutAsync(CancellationToken cancellationToken = default) =>
        AsVoid(database.WriteAsync(
            static (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Execute(
                    connection,
                    transaction,
                    """
                    UPDATE sync_state
                       SET user_id = NULL, server_cursor = 0, dek_generation = 0,
                           locked = 0, last_sync_utc = NULL
                     WHERE id = 1;
                    """);
            },
            cancellationToken));

    /// <summary>
    /// Marks the account as unable to decrypt — the state after a password reset, until the recovery
    /// key or another device re-wraps the data key (docs/CLOUD_SYNC.md §4.8).
    /// </summary>
    public ValueTask SetLockedAsync(bool locked, CancellationToken cancellationToken = default) =>
        AsVoid(database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Execute(
                    connection,
                    transaction,
                    "UPDATE sync_state SET locked = $locked WHERE id = 1;",
                    ("$locked", locked ? 1 : 0));
            },
            cancellationToken));

    /// <summary>
    /// Records how far the pull got. Never moves backwards: a stale response arriving out of order
    /// must not make us re-read, and re-reading is not harmless — it would re-apply merges and
    /// re-emit conflict backups.
    /// </summary>
    public ValueTask AdvanceCursorAsync(long cursor, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cursor);
        DateTimeOffset now = utcNow();
        return AsVoid(database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Execute(
                    connection,
                    transaction,
                    """
                    UPDATE sync_state
                       SET server_cursor = MAX(server_cursor, $cursor), last_sync_utc = $now
                     WHERE id = 1;
                    """,
                    ("$cursor", cursor),
                    ("$now", SyncTimestamps.ToLocal(now)));
            },
            cancellationToken));
    }

    private static HashSet<string> ReadQueuedNoteIds(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT entity_id FROM sync_outbox WHERE entity = 'note';";
        var queued = new HashSet<string>(StringComparer.Ordinal);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            queued.Add(reader.GetString(0));
        }

        return queued;
    }

    private static void ParkDate(SqliteConnection connection, SqliteTransaction transaction, string date)
    {
        // A uniform shift preserves uniqueness within the date and cannot collide with an unshifted
        // row, because sort orders are dense 0..n-1 at rest and never approach the offset.
        Execute(
            connection,
            transaction,
            "UPDATE notes SET sort_order = sort_order + $offset WHERE local_date = $date;",
            ("$offset", ParkingOffset),
            ("$date", date));
    }

    private static void Resequence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string date,
        HashSet<string> mergedIds,
        HashSet<string> alreadyQueued,
        Dictionary<string, List<SyncNote>> incomingByDate,
        DateTimeOffset mergeInstant)
    {
        Dictionary<string, int> desired = new(StringComparer.Ordinal);
        if (incomingByDate.TryGetValue(date, out List<SyncNote>? incoming))
        {
            foreach (SyncNote note in incoming)
            {
                desired[note.Id] = note.SortOrder;
            }
        }

        var rows = new List<(string Id, int Desired, DateTimeOffset Created, DateTimeOffset Updated)>();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "SELECT id, sort_order, created_utc, updated_utc FROM notes WHERE local_date = $date;";
            command.Parameters.AddWithValue("$date", date);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                int parked = reader.GetInt32(1);
                rows.Add((
                    id,
                    // A merged note keeps the slot the other device chose; an untouched local note
                    // keeps its own, recovered by undoing the park.
                    desired.TryGetValue(id, out int claimed) ? claimed : parked - ParkingOffset,
                    ReadTimestamp(reader.GetString(2)),
                    ReadTimestamp(reader.GetString(3))));
            }
        }

        // Order by the slot each side claims, then by creation and id. Sorting purely by creation
        // time would silently undo the user's manual reordering on every merge; the tie-breakers are
        // only there so two devices resolve a contested slot identically.
        rows.Sort(static (left, right) =>
        {
            int bySlot = left.Desired.CompareTo(right.Desired);
            if (bySlot != 0)
            {
                return bySlot;
            }

            int byCreated = left.Created.CompareTo(right.Created);
            return byCreated != 0 ? byCreated : string.CompareOrdinal(left.Id, right.Id);
        });

        for (int finalOrder = 0; finalOrder < rows.Count; finalOrder += 1)
        {
            (string id, int claimed, _, DateTimeOffset updated) = rows[finalOrder];
            bool moved = claimed != finalOrder;

            // A sort order that ends up different from what either side had has to carry a newer
            // timestamp, or the server rejects the push as stale and the change can never propagate
            // — the note would stay queued forever. Rows that did not move keep their timestamp so a
            // merge does not manufacture edits.
            DateTimeOffset stamp = moved ? mergeInstant : updated;
            Execute(
                connection,
                transaction,
                "UPDATE notes SET sort_order = $order, updated_utc = $utc WHERE id = $id;",
                ("$order", finalOrder),
                ("$utc", SyncTimestamps.ToLocal(stamp)),
                ("$id", id));

            RefreshSearch(connection, transaction, id);

            if (!moved && !mergedIds.Contains(id) && !alreadyQueued.Contains(id))
            {
                // Parking and unparking this row fired the outbox trigger, but nothing about it
                // actually changed, so the queue entry would be a push of identical content. Notes
                // that were already queued keep their entry: that is a real pending local edit.
                DeleteOutbox(connection, transaction, SyncEntityKind.Note, id);
            }
        }
    }

    private static void WriteMergedNote(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncNote incoming,
        int parkedSlot)
    {
        NoteId id = NoteId.Create(Guid.Parse(incoming.Id)).Value;
        string date = incoming.LocalDate.ToString();
        string storedTitle = incoming.HasCustomTitle
            ? incoming.Title
            : UntitledNote.TitleFor(incoming.SortOrder + 1);
        string created = SyncTimestamps.ToLocal(incoming.CreatedUtc);
        string updated = SyncTimestamps.ToLocal(incoming.UpdatedUtc);

        // Bumping revision is deliberate: an editor window still holding the pre-merge revision must
        // fail its next save with a conflict rather than overwrite what just arrived.
        Execute(
            connection,
            transaction,
            """
            INSERT INTO notes(id, local_date, title, body, sort_order, revision, created_utc, updated_utc, is_favorite)
            VALUES ($id, $date, $title, $body, $order, 0, $created, $updated, $favorite)
            ON CONFLICT(id) DO UPDATE SET
                local_date = excluded.local_date,
                title = excluded.title,
                body = excluded.body,
                sort_order = excluded.sort_order,
                revision = notes.revision + 1,
                created_utc = excluded.created_utc,
                updated_utc = excluded.updated_utc,
                is_favorite = excluded.is_favorite;
            """,
            ("$id", incoming.Id),
            ("$date", date),
            ("$title", storedTitle),
            ("$body", incoming.Body),
            ("$order", parkedSlot),
            ("$created", created),
            ("$updated", updated),
            ("$favorite", incoming.IsFavorite ? 1 : 0));

        Execute(
            connection,
            transaction,
            "DELETE FROM note_tags WHERE note_id = $id;",
            ("$id", incoming.Id));
        for (int order = 0; order < incoming.Tags.Count; order += 1)
        {
            Execute(
                connection,
                transaction,
                "INSERT INTO note_tags(note_id, tag, sort_order) VALUES ($id, $tag, $order);",
                ("$id", incoming.Id),
                ("$tag", incoming.Tags[order]),
                ("$order", order));
        }

        // The custom-title flag lives in the settings table, so it has to travel with the note or a
        // second device silently loses the note's name (docs/CLOUD_SYNC.md §3).
        SqliteNoteStatements.SetCustomTitle(connection, transaction, id, incoming.HasCustomTitle, updated);
    }

    /// <summary>
    /// Rebuilds the search row for a note from whatever is now stored, reusing the repository's own
    /// indexing so folding, normalisation, and tag handling cannot drift between the two writers.
    /// </summary>
    private static void RefreshSearch(SqliteConnection connection, SqliteTransaction transaction, string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT local_date, title, body, sort_order,
                   EXISTS(SELECT 1 FROM settings WHERE key = 'note.custom-title.' || id)
              FROM notes WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return;
        }

        bool hasCustomTitle = reader.GetInt32(4) != 0;
        int sortOrder = reader.GetInt32(3);
        SqliteNoteStatements.UpsertSearch(
            connection,
            transaction,
            NoteId.Create(Guid.Parse(id)).Value,
            LocalDate.Parse(reader.GetString(0)).Value,
            hasCustomTitle ? reader.GetString(1) : UntitledNote.TitleFor(sortOrder + 1),
            reader.GetString(2));
    }

    private static void DeleteNote(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        DateTimeOffset deletedUtc) =>
        SqliteNoteStatements.Delete(
            connection,
            transaction,
            NoteId.Create(Guid.Parse(id)).Value,
            SyncTimestamps.ToLocal(deletedUtc));

    private static void DeleteOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncEntityKind kind,
        string id) =>
        Execute(
            connection,
            transaction,
            "DELETE FROM sync_outbox WHERE entity = $entity AND entity_id = $id;",
            ("$entity", WireKind(kind)),
            ("$id", id));

    private static void DeleteTombstone(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncEntityKind kind,
        string id) =>
        Execute(
            connection,
            transaction,
            "DELETE FROM sync_tombstones WHERE entity = $entity AND entity_id = $id;",
            ("$entity", WireKind(kind)),
            ("$id", id));

    private static DateTimeOffset? ReadTombstone(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncEntityKind kind,
        string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT deleted_utc FROM sync_tombstones WHERE entity = $entity AND entity_id = $id;";
        command.Parameters.AddWithValue("$entity", WireKind(kind));
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is string value ? ReadTimestamp(value) : null;
    }

    private static LocalNoteRow? ReadNoteRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT local_date, title, body, sort_order, updated_utc,
                   EXISTS(SELECT 1 FROM settings WHERE key = 'note.custom-title.' || id)
              FROM notes WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        bool hasCustomTitle = reader.GetInt32(5) != 0;
        int sortOrder = reader.GetInt32(3);
        return new LocalNoteRow(
            id,
            reader.GetString(0),
            hasCustomTitle ? reader.GetString(1) : UntitledNote.TitleFor(sortOrder + 1),
            reader.GetString(2),
            ReadTimestamp(reader.GetString(4)));
    }

    private static Dictionary<string, List<string>> ReadTags(SqliteConnection connection, List<string> ids)
    {
        using SqliteCommand command = connection.CreateCommand();
        string[] placeholders = new string[ids.Count];
        for (int i = 0; i < ids.Count; i += 1)
        {
            placeholders[i] = $"$id{i}";
            command.Parameters.AddWithValue(placeholders[i], ids[i]);
        }

        command.CommandText =
            $"SELECT note_id, tag FROM note_tags WHERE note_id IN ({string.Join(",", placeholders)}) " +
            "ORDER BY note_id, sort_order;";

        var tags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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

        return tags;
    }

    private static DisplacedNote ToDisplaced(LocalNoteRow row) =>
        new(row.Id, LocalDate.Parse(row.LocalDate).Value, row.EffectiveTitle, row.Body, row.UpdatedUtc);

    private static DateTimeOffset ReadTimestamp(string value) =>
        SyncTimestamps.TryParseLocal(value, out DateTimeOffset parsed)
            ? parsed
            : throw new InvalidOperationException($"A stored timestamp could not be read: '{value}'.");

    private static string WireKind(SyncEntityKind kind) => kind == SyncEntityKind.Note ? "note" : "file";

    private static SyncEntityKind ParseKind(string value) =>
        value switch
        {
            "note" => SyncEntityKind.Note,
            "file" => SyncEntityKind.File,
            _ => throw new InvalidOperationException($"Unknown sync entity '{value}'."),
        };

    private static int Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteNonQuery();
    }

    private static async ValueTask AsVoid(ValueTask<int> operation) => await operation.ConfigureAwait(false);

    private readonly record struct LocalNoteRow(
        string Id,
        string LocalDate,
        string EffectiveTitle,
        string Body,
        DateTimeOffset UpdatedUtc);
}
