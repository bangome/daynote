using Daynote.Core.Agenda;
using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// The to-do and event half of one sync cycle (docs/TODOS.md §9).
/// </summary>
/// <remarks>
/// Lists before items, in both directions and for the same reason: an item names the list it lives
/// in, and a device that learns about the item first has to put it somewhere. The local merge does
/// fall back to the default list rather than dropping it (SqliteSyncStore.Agenda), but that
/// fallback is a safety net for a page boundary, not a plan — a to-do that arrives in the wrong
/// list and stays there is a user noticing their lists have reorganised themselves.
/// <para>
/// Nothing here writes to the conflicts folder. Last-write-wins discards a losing version of a
/// to-do silently, which would be unacceptable for a note and is right here: the losing version is
/// a title, a date and a checkbox, all of them visible, and a folder filling with them would be
/// noise that trains the user to ignore the folder that also holds their prose.
/// </para>
/// </remarks>
public sealed partial class SyncEngine
{
    private async ValueTask<bool> PushAgendaAsync(
        SyncSession session,
        Tally tally,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<PendingAgendaList> lists = await store
                .ReadPendingAgendaListsAsync(PushBatch, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<PendingAgendaItem> items = await store
                .ReadPendingAgendaItemsAsync(PushBatch, cancellationToken)
                .ConfigureAwait(false);

            if (lists.Count == 0 && items.Count == 0)
            {
                return true;
            }

            var encryptedLists = new List<EncryptedAgenda>(lists.Count);
            foreach (PendingAgendaList entry in lists)
            {
                string id = entry.List.Id.ToString();
                encryptedLists.Add(new EncryptedAgenda(
                    id,
                    crypto.Encrypt(
                        AgendaPayloadCodec.Serialize(entry.List),
                        session.DataKey,
                        CipherScope.AgendaList(session.UserId, id)),
                    entry.List.UpdatedUtc));
            }

            var encryptedItems = new List<EncryptedAgenda>(items.Count);
            foreach (PendingAgendaItem entry in items)
            {
                string id = entry.Item.Id.ToString();
                encryptedItems.Add(new EncryptedAgenda(
                    id,
                    crypto.Encrypt(
                        AgendaPayloadCodec.Serialize(entry.Item),
                        session.DataKey,
                        CipherScope.AgendaItem(session.UserId, id)),
                    entry.Item.UpdatedUtc));
            }

            // One request, lists first. The server writes the batch in order, so a list takes the
            // lower sequence number and a device pulling from zero meets it before its contents.
            PushResult result = await api
                .PushAsync(
                    new PushRequest([], [], encryptedLists, encryptedItems),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!WithinSkew(result.ServerUtc))
            {
                return false;
            }

            if (!result.AgendaSupported)
            {
                // A deployment older than this feature. Everything stays queued — reading its
                // silence as a rejection would clear the queue and lose the to-dos entirely.
                tally.AgendaUnsupported = true;
                return true;
            }

            var settledLists = new HashSet<string>(result.SettledAgendaListIds, StringComparer.Ordinal);
            var settledItems = new HashSet<string>(result.SettledAgendaItemIds, StringComparer.Ordinal);

            tally.AgendaPushed +=
                (result.AcceptedAgendaListIds?.Count ?? 0) + (result.AcceptedAgendaItemIds?.Count ?? 0);
            tally.RejectedAsStale +=
                (result.RejectedAgendaListIds?.Count ?? 0) + (result.RejectedAgendaItemIds?.Count ?? 0);

            // Acknowledged against the queued stamp, exactly as notes are: an edit that lands while
            // the push is in flight has moved the queue entry on, and that entry has to survive.
            PendingAck[] acknowledged =
            [
                .. lists
                    .Where(entry => settledLists.Contains(entry.List.Id.ToString()))
                    .Select(entry => new PendingAck(
                        SyncEntityKind.AgendaList, entry.List.Id.ToString(), entry.QueuedUtc)),
                .. items
                    .Where(entry => settledItems.Contains(entry.Item.Id.ToString()))
                    .Select(entry => new PendingAck(
                        SyncEntityKind.AgendaItem, entry.Item.Id.ToString(), entry.QueuedUtc)),
            ];

            if (acknowledged.Length == 0)
            {
                // The server settled nothing, so retrying would spin.
                return true;
            }

            await store.AcknowledgePushAsync(acknowledged, cancellationToken).ConfigureAwait(false);

            if (lists.Count < PushBatch && items.Count < PushBatch)
            {
                return true;
            }
        }
    }

    private async ValueTask MergeAgendaAsync(
        SyncSession session,
        Tally tally,
        IReadOnlyList<PullChange> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var lists = new List<AgendaList>();
        var items = new List<AgendaItem>();
        var listTombstones = new List<SyncTombstone>();
        var itemTombstones = new List<SyncTombstone>();

        foreach (PullChange change in changes)
        {
            bool isList = change.Kind == SyncEntityKind.AgendaList;

            if (change.DeletedUtc is not null || change.Payload is null)
            {
                var tombstone = new SyncTombstone(
                    change.Kind,
                    change.Id,
                    change.DeletedUtc ?? change.UpdatedUtc);
                (isList ? listTombstones : itemTombstones).Add(tombstone);
                continue;
            }

            CipherScope scope = isList
                ? CipherScope.AgendaList(session.UserId, change.Id)
                : CipherScope.AgendaItem(session.UserId, change.Id);

            DomainResult<string> opened = crypto.Decrypt(change.Payload, session.DataKey, scope);
            if (!opened.IsSuccess)
            {
                // Tampering, corruption, or the wrong key. Counted and surfaced, never skipped
                // quietly: a to-do that vanishes looks exactly like one that was completed.
                tally.Undecryptable += 1;
                continue;
            }

            if (isList)
            {
                DomainResult<AgendaList> list =
                    AgendaPayloadCodec.DeserializeList(change.Id, opened.Value, change.UpdatedUtc);
                if (list.IsSuccess)
                {
                    lists.Add(list.Value);
                }
                else
                {
                    tally.Malformed += 1;
                }

                continue;
            }

            DomainResult<AgendaItem> item =
                AgendaPayloadCodec.DeserializeItem(change.Id, opened.Value, change.UpdatedUtc);
            if (item.IsSuccess)
            {
                items.Add(item.Value);
            }
            else
            {
                tally.Malformed += 1;
            }
        }

        tally.AgendaPulled += changes.Count;

        // Lists first, and as two calls rather than one: the item merge checks that the list it
        // names exists, and that check has to see the lists from this same page already committed.
        if (lists.Count > 0 || listTombstones.Count > 0)
        {
            AgendaMergeOutcome outcome = await store
                .MergeAgendaListsAsync(lists, listTombstones, cancellationToken)
                .ConfigureAwait(false);
            tally.Applied += outcome.Applied;
            tally.Ignored += outcome.Ignored;
            tally.Deleted += outcome.Deleted;
        }

        if (items.Count > 0 || itemTombstones.Count > 0)
        {
            AgendaMergeOutcome outcome = await store
                .MergeAgendaItemsAsync(items, itemTombstones, cancellationToken)
                .ConfigureAwait(false);
            tally.Applied += outcome.Applied;
            tally.Ignored += outcome.Ignored;
            tally.Deleted += outcome.Deleted;
        }
    }
}
