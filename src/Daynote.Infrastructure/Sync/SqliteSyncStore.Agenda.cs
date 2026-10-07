using Daynote.Core.Agenda;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Agenda;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The to-do and event side of the local sync store (docs/TODOS.md §9).
/// </summary>
/// <remarks>
/// Two entities, not one. Lists are small and rarely edited, but they are the container items
/// depend on, so a pull applies <see cref="MergeAgendaListsAsync"/> first and a push drains
/// <see cref="ReadPendingAgendaListsAsync"/> first. An item naming a list this device has never
/// heard of lands in the default rather than being dropped: a container is not a reason to lose a
/// task.
/// <para>
/// No displaced-version list, unlike the note merge. A to-do is a handful of fields the user can
/// see and re-enter; writing every losing version to the conflicts folder would fill it with noise
/// nobody reads.
/// </para>
/// </remarks>
public sealed partial class SqliteSyncStore
{
    public async ValueTask<IReadOnlyList<PendingAgendaList>> ReadPendingAgendaListsAsync(
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
            SELECT l.id, l.name, l.sort_order, l.is_default, l.created_utc, l.updated_utc,
                   o.queued_utc
              FROM sync_outbox o
              JOIN agenda_lists l ON l.id = o.entity_id
             WHERE o.entity = 'agenda_list'
             ORDER BY o.queued_utc, l.id
             LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var pending = new List<PendingAgendaList>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            pending.Add(new PendingAgendaList(
                new AgendaList(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3) != 0,
                    ReadTimestamp(reader.GetString(4)),
                    ReadTimestamp(reader.GetString(5))),
                ReadTimestamp(reader.GetString(6))));
        }

        return pending;
    }

    public async ValueTask<IReadOnlyList<PendingAgendaItem>> ReadPendingAgendaItemsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        using SqliteConnection connection = database.OpenReadConnection();

        // Ordered so a series is pushed before any override of it. The server does not care, but a
        // receiving device applies a page in the order it is given and an override is a foreign key
        // onto its series.
        var queued = new List<(Guid Id, DateTimeOffset QueuedUtc)>();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT i.id, o.queued_utc
                  FROM sync_outbox o
                  JOIN agenda_items i ON i.id = o.entity_id
                 WHERE o.entity = 'agenda_item'
                 ORDER BY (i.series_id IS NOT NULL), o.queued_utc, i.id
                 LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                queued.Add((Guid.Parse(reader.GetString(0)), ReadTimestamp(reader.GetString(1))));
            }
        }

        var pending = new List<PendingAgendaItem>(queued.Count);
        foreach ((Guid id, DateTimeOffset queuedUtc) in queued)
        {
            // Read whole: exdates and alarms are separate tables, and the repository's reader is
            // the only place that knows how to put an item back together.
            if (SqliteAgendaStatements.ReadItem(connection, id) is { } item)
            {
                pending.Add(new PendingAgendaItem(item, queuedUtc));
            }
        }

        return pending;
    }

    public ValueTask<AgendaMergeOutcome> MergeAgendaListsAsync(
        IReadOnlyList<AgendaList> lists,
        IReadOnlyList<SyncTombstone> tombstones,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(tombstones);
        if (lists.Count == 0 && tombstones.Count == 0)
        {
            return ValueTask.FromResult(AgendaMergeOutcome.Empty);
        }

        DateTimeOffset mergeInstant = utcNow();

        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();

                int applied = 0;
                int ignored = 0;
                int removed = 0;

                foreach (SyncTombstone tombstone in tombstones)
                {
                    if (tombstone.Kind != SyncEntityKind.AgendaList ||
                        !Guid.TryParse(tombstone.Id, out Guid id))
                    {
                        continue;
                    }

                    if (id == AgendaList.DefaultId)
                    {
                        // The default is where everything else lands, so there is nowhere to put
                        // its contents. A device that somehow deleted it is wrong, not ahead.
                        ignored += 1;
                        continue;
                    }

                    DateTimeOffset? local =
                        SqliteAgendaStatements.ReadUpdatedUtc(connection, transaction, "agenda_lists", id);
                    if (local is null)
                    {
                        DeleteTombstone(connection, transaction, SyncEntityKind.AgendaList, tombstone.Id);
                        continue;
                    }

                    if (local.Value > tombstone.DeletedUtc)
                    {
                        // A local rename outlives the remote delete. The outbox still holds it, so
                        // the next push re-creates the list on the server.
                        ignored += 1;
                        continue;
                    }

                    // Moves the items to the default on the way out, and bumps each of them, so the
                    // other device learns where they went rather than inferring it.
                    SqliteAgendaStatements.DeleteList(
                        connection, transaction, id, SqliteAgendaStatements.FormatUtc(mergeInstant));
                    // The AFTER DELETE trigger just wrote a tombstone of its own. Remove it: this
                    // delete came *from* the server and must not be pushed back to it.
                    DeleteTombstone(connection, transaction, SyncEntityKind.AgendaList, tombstone.Id);
                    removed += 1;
                }

                foreach (AgendaList incoming in lists)
                {
                    string wireId = SqliteAgendaStatements.Format(incoming.Id);
                    DateTimeOffset? localDelete =
                        ReadTombstone(connection, transaction, SyncEntityKind.AgendaList, wireId);
                    if (localDelete is { } deletedAt && deletedAt > incoming.UpdatedUtc)
                    {
                        ignored += 1;
                        continue;
                    }

                    DateTimeOffset? local = SqliteAgendaStatements.ReadUpdatedUtc(
                        connection, transaction, "agenda_lists", incoming.Id);
                    if (local is { } current && current >= incoming.UpdatedUtc)
                    {
                        // Equal timestamps mean the same version of the same id: keeping local is
                        // the deterministic answer.
                        ignored += 1;
                        continue;
                    }

                    if (localDelete is not null)
                    {
                        DeleteTombstone(connection, transaction, SyncEntityKind.AgendaList, wireId);
                    }

                    SqliteAgendaStatements.SaveList(connection, transaction, incoming);
                    DeleteOutbox(connection, transaction, SyncEntityKind.AgendaList, wireId);
                    applied += 1;
                }

                return new AgendaMergeOutcome(applied, ignored, removed);
            },
            cancellationToken);
    }

    public ValueTask<AgendaMergeOutcome> MergeAgendaItemsAsync(
        IReadOnlyList<AgendaItem> items,
        IReadOnlyList<SyncTombstone> tombstones,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(tombstones);
        if (items.Count == 0 && tombstones.Count == 0)
        {
            return ValueTask.FromResult(AgendaMergeOutcome.Empty);
        }

        return database.WriteAsync(
            (connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();

                int applied = 0;
                int ignored = 0;
                int removed = 0;

                foreach (SyncTombstone tombstone in tombstones)
                {
                    if (tombstone.Kind != SyncEntityKind.AgendaItem ||
                        !Guid.TryParse(tombstone.Id, out Guid id))
                    {
                        continue;
                    }

                    DateTimeOffset? local =
                        SqliteAgendaStatements.ReadUpdatedUtc(connection, transaction, "agenda_items", id);
                    if (local is null)
                    {
                        DeleteTombstone(connection, transaction, SyncEntityKind.AgendaItem, tombstone.Id);
                        continue;
                    }

                    if (local.Value > tombstone.DeletedUtc)
                    {
                        ignored += 1;
                        continue;
                    }

                    // Deleting a series cascades to its overrides, and each cascaded row leaves a
                    // tombstone of its own that must not be pushed back either. The server sent us
                    // those tombstones too; this just gets there first.
                    SqliteAgendaStatements.Delete(connection, transaction, id);
                    DeleteTombstone(connection, transaction, SyncEntityKind.AgendaItem, tombstone.Id);
                    removed += 1;
                }

                // Series and plain items before overrides. An override is a foreign key onto the
                // row carrying the rule, so applying one first fails — and within a single page the
                // two can arrive in any order.
                foreach (AgendaItem incoming in items.Where(static item => !item.IsOverride)
                    .Concat(items.Where(static item => item.IsOverride)))
                {
                    string wireId = SqliteAgendaStatements.Format(incoming.Id);
                    DateTimeOffset? localDelete =
                        ReadTombstone(connection, transaction, SyncEntityKind.AgendaItem, wireId);
                    if (localDelete is { } deletedAt && deletedAt > incoming.UpdatedUtc)
                    {
                        ignored += 1;
                        continue;
                    }

                    DateTimeOffset? local = SqliteAgendaStatements.ReadUpdatedUtc(
                        connection, transaction, "agenda_items", incoming.Id);
                    if (local is { } current && current >= incoming.UpdatedUtc)
                    {
                        ignored += 1;
                        continue;
                    }

                    if (incoming.SeriesId is { } series &&
                        SqliteAgendaStatements.ReadUpdatedUtc(
                            connection, transaction, "agenda_items", series) is null)
                    {
                        // The series has not arrived. Pages come back in cursor order and a series
                        // is always written before any override of it, so this means the series was
                        // deleted — in which case the override is already gone there too and its
                        // tombstone is on its way.
                        ignored += 1;
                        continue;
                    }

                    AgendaItem landed =
                        SqliteAgendaStatements.ListExists(connection, transaction, incoming.ListId)
                            ? incoming
                            // §9: fall back to the default rather than dropping it. The list may
                            // simply be later in the pull, and a container is not a reason to lose
                            // a task. Whoever owns the list pushes it; this device does not claim
                            // the row by inventing one.
                            : incoming with { ListId = AgendaList.DefaultId };

                    if (localDelete is not null)
                    {
                        DeleteTombstone(connection, transaction, SyncEntityKind.AgendaItem, wireId);
                    }

                    SqliteAgendaStatements.Save(connection, transaction, landed);

                    // The insert/update trigger just queued this. Drop it: the row is the server's
                    // own version, so pushing it back is an echo. An item that *did* have a pending
                    // local edit got here only by losing last-write-wins, which means that edit is
                    // superseded — queueing it again would undo what just arrived.
                    DeleteOutbox(connection, transaction, SyncEntityKind.AgendaItem, wireId);
                    applied += 1;
                }

                return new AgendaMergeOutcome(applied, ignored, removed);
            },
            cancellationToken);
    }
}
