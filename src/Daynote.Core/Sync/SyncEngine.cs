using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

public enum SyncOutcome
{
    Completed,

    /// <summary>Nothing to do: signed out.</summary>
    SignedOut,

    /// <summary>
    /// Signed in but unable to decrypt — the state after a password reset until the recovery key or
    /// another device re-wraps the data key. Sync stays put rather than pushing or overwriting.
    /// </summary>
    Locked,

    /// <summary>
    /// The local clock disagrees with the server's by more than the allowance. Syncing under a wrong
    /// clock would let last-write-wins pick the wrong winner, which is worse than not syncing.
    /// </summary>
    ClockSkew,

    /// <summary>
    /// The service could not be reached. Ordinary and temporary, so callers show it as a state rather
    /// than an error and let the next cycle retry.
    /// </summary>
    Offline,

    /// <summary>The session is gone; only a fresh sign-in will help.</summary>
    SignInRequired,
}

public sealed record SyncReport(
    SyncOutcome Outcome,
    int Pushed,
    int RejectedAsStale,
    int TombstonesPushed,
    int Pulled,
    int Applied,
    int Ignored,
    int Deleted,
    int Undecryptable,
    int Malformed,
    int ConflictsSaved,
    long Cursor)
{
    public static SyncReport For(SyncOutcome outcome, long cursor = 0) =>
        new(outcome, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, cursor);

    /// <summary>True when a record arrived that we could not read. Never silently ignorable.</summary
    public bool HasUnreadableRecords => Undecryptable > 0 || Malformed > 0;
}

/// <summary>The signed-in account and the key that opens its content, for one sync run.</summary>
public sealed record SyncSession(string UserId, KeyMaterial DataKey);

/// <summary>
/// Drives one sync cycle: push deletes, push edits, pull, merge (docs/CLOUD_SYNC.md §7.2).
/// </summary>
/// <remarks>
/// Order matters. Deletes go first so a delete never loses to its own stale upsert. Pulling last
/// means a push rejected as stale is resolved in the same run rather than a run later.
/// <para>
/// Encryption happens here and nowhere below: <see cref="ISyncApiClient"/> only ever sees envelopes.
/// </para>
/// </remarks>
public sealed class SyncEngine
{
    /// <summary>
    /// How far the clocks may disagree before we refuse. Generous enough for ordinary drift, tight
    /// enough that a badly wrong clock cannot win a last-write-wins race it should lose.
    /// </summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private const int PushBatch = 200;
    private const int PullBatch = 200;

    /// <summary>Bounds one run so a large backlog cannot loop indefinitely inside a single sync.</summary>
    private const int MaxPullPages = 50;

    private readonly ISyncApiClient api;
    private readonly ISyncCrypto crypto;
    private readonly ISyncStore store;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly ISyncConflictSink? conflicts;

    public SyncEngine(
        ISyncApiClient api,
        ISyncCrypto crypto,
        ISyncStore store,
        Func<DateTimeOffset>? utcNow = null,
        ISyncConflictSink? conflicts = null)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
        this.conflicts = conflicts;
    }

    public async ValueTask<SyncReport> SyncAsync(
        SyncSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        SyncStateSnapshot state = await store.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsSignedIn)
        {
            return SyncReport.For(SyncOutcome.SignedOut, state.ServerCursor);
        }

        if (state.IsLocked)
        {
            return SyncReport.For(SyncOutcome.Locked, state.ServerCursor);
        }

        var tally = new Tally { Cursor = state.ServerCursor };

        try
        {
            return await RunAsync(session, tally, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncTransportException transport)
        {
            // Being offline is the normal state of a laptop, not a fault to report. Anything the
            // engine already wrote stays written; the queue and cursor make the next run resume.
            return tally.ToReport(
                transport.RequiresSignIn ? SyncOutcome.SignInRequired : SyncOutcome.Offline);
        }
    }

    private async ValueTask<SyncReport> RunAsync(
        SyncSession session,
        Tally tally,
        CancellationToken cancellationToken)
    {
        // Establish the server's clock before writing anything. Checking skew only on the push
        // response would be too late: the data is already stored by then. A pull is read-only and
        // does not move the cursor by itself, so it is safe to spend one on the question.
        PullResult probe = await api
            .PullAsync(tally.Cursor, 1, cancellationToken)
            .ConfigureAwait(false);
        if (!WithinSkew(probe.ServerUtc))
        {
            return tally.ToReport(SyncOutcome.ClockSkew);
        }

        // Deletes before edits: a tombstone that arrived after an edit must not be overtaken by that
        // edit's own upsert in the same batch.
        if (!await PushTombstonesAsync(tally, cancellationToken).ConfigureAwait(false))
        {
            return tally.ToReport(SyncOutcome.ClockSkew);
        }

        if (!await PushNotesAsync(session, tally, cancellationToken).ConfigureAwait(false))
        {
            return tally.ToReport(SyncOutcome.ClockSkew);
        }

        if (!await PullAsync(session, tally, cancellationToken).ConfigureAwait(false))
        {
            return tally.ToReport(SyncOutcome.ClockSkew);
        }

        return tally.ToReport(SyncOutcome.Completed);
    }

    private async ValueTask<bool> PushTombstonesAsync(Tally tally, CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<SyncTombstone> pending = await store
                .ReadPendingTombstonesAsync(PushBatch, cancellationToken)
                .ConfigureAwait(false);
            if (pending.Count == 0)
            {
                return true;
            }

            // Only notes reach the server so far; file tombstones stay queued for the R2 phase rather
            // than being dropped, which would lose the delete entirely.
            var notes = pending.Where(static t => t.Kind == SyncEntityKind.Note).ToArray();
            if (notes.Length == 0)
            {
                return true;
            }

            PushResult result = await api
                .PushAsync(
                    new PushRequest(
                        [],
                        [.. notes.Select(t => new EncryptedTombstone(t.Kind, t.Id, t.DeletedUtc))]),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!WithinSkew(result.ServerUtc))
            {
                return false;
            }

            // Both outcomes clear the queue entry: accepted means done, and rejected means the server
            // holds something newer, which the pull below brings down. Leaving it queued would retry
            // the same rejection forever.
            var settled = new HashSet<string>(result.AcceptedTombstoneIds, StringComparer.Ordinal);
            settled.UnionWith(result.RejectedTombstoneIds);
            var acknowledged = notes.Where(t => settled.Contains(t.Id)).ToArray();

            tally.TombstonesPushed += result.AcceptedTombstoneIds.Count;

            if (acknowledged.Length == 0)
            {
                // The server settled nothing, so retrying would spin.
                return true;
            }

            await store.AcknowledgeTombstonesAsync(acknowledged, cancellationToken).ConfigureAwait(false);
            if (notes.Length < PushBatch)
            {
                return true;
            }
        }
    }

    private async ValueTask<bool> PushNotesAsync(
        SyncSession session,
        Tally tally,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            IReadOnlyList<PendingNote> pending = await store
                .ReadPendingNotesAsync(PushBatch, cancellationToken)
                .ConfigureAwait(false);
            if (pending.Count == 0)
            {
                return true;
            }

            var encrypted = new List<EncryptedNote>(pending.Count);
            foreach (PendingNote entry in pending)
            {
                string payload = crypto.Encrypt(
                    NotePayloadCodec.Serialize(entry.Note),
                    session.DataKey,
                    CipherScope.Note(session.UserId, entry.Note.Id));
                encrypted.Add(new EncryptedNote(entry.Note.Id, payload, entry.Note.UpdatedUtc));
            }

            PushResult result = await api
                .PushAsync(new PushRequest(encrypted, []), cancellationToken)
                .ConfigureAwait(false);

            if (!WithinSkew(result.ServerUtc))
            {
                return false;
            }

            var settled = new HashSet<string>(result.AcceptedNoteIds, StringComparer.Ordinal);
            settled.UnionWith(result.RejectedNoteIds);

            tally.Pushed += result.AcceptedNoteIds.Count;
            tally.RejectedAsStale += result.RejectedNoteIds.Count;

            // Acknowledging by queued_utc is what makes an edit landing mid-push safe: if the note
            // changed while in flight, its queue entry moved on and survives this call.
            PendingAck[] acknowledged =
            [
                .. pending
                    .Where(entry => settled.Contains(entry.Note.Id))
                    .Select(entry => new PendingAck(SyncEntityKind.Note, entry.Note.Id, entry.QueuedUtc)),
            ];

            if (acknowledged.Length == 0)
            {
                return true;
            }

            await store.AcknowledgePushAsync(acknowledged, cancellationToken).ConfigureAwait(false);
            if (pending.Count < PushBatch)
            {
                return true;
            }
        }
    }

    private async ValueTask<bool> PullAsync(
        SyncSession session,
        Tally tally,
        CancellationToken cancellationToken)
    {
        for (int page = 0; page < MaxPullPages; page += 1)
        {
            PullResult result = await api
                .PullAsync(tally.Cursor, PullBatch, cancellationToken)
                .ConfigureAwait(false);

            if (!WithinSkew(result.ServerUtc))
            {
                return false;
            }

            tally.Pulled += result.Changes.Count;

            var notes = new List<SyncNote>();
            var tombstones = new List<SyncTombstone>();
            foreach (PullChange change in result.Changes)
            {
                if (change.Kind != SyncEntityKind.Note)
                {
                    continue;
                }

                if (change.DeletedUtc is { } deletedAt || change.Payload is null)
                {
                    tombstones.Add(new SyncTombstone(
                        SyncEntityKind.Note,
                        change.Id,
                        change.DeletedUtc ?? change.UpdatedUtc));
                    continue;
                }

                DomainResult<string> opened = crypto.Decrypt(
                    change.Payload,
                    session.DataKey,
                    CipherScope.Note(session.UserId, change.Id));
                if (!opened.IsSuccess)
                {
                    // Tampering, corruption, or the wrong key. Counted and surfaced, never treated as
                    // "skip this note": that would look exactly like the note having been deleted.
                    tally.Undecryptable += 1;
                    continue;
                }

                DomainResult<SyncNote> note = NotePayloadCodec.Deserialize(
                    change.Id,
                    opened.Value,
                    change.UpdatedUtc);
                if (!note.IsSuccess)
                {
                    tally.Malformed += 1;
                    continue;
                }

                notes.Add(note.Value);
            }

            if (notes.Count > 0 || tombstones.Count > 0)
            {
                MergeOutcome outcome = await store
                    .MergeNotesAsync(notes, tombstones, cancellationToken)
                    .ConfigureAwait(false);

                tally.Applied += outcome.Applied;
                tally.Ignored += outcome.Ignored;
                tally.Deleted += outcome.Deleted;

                if (outcome.Displaced.Count > 0 && conflicts is not null)
                {
                    // Before the cursor advances: if saving the losing versions fails, the run fails
                    // and the same page is pulled again, rather than the edits being gone with no copy.
                    await conflicts.SaveAsync(outcome.Displaced, cancellationToken).ConfigureAwait(false);
                    tally.ConflictsSaved += outcome.Displaced.Count;
                }
            }

            // Only a pull moves the cursor, and only forwards. The push response also carries a
            // cursor, but it is the log head: adopting it would skip every change already sitting in
            // the log below it that this device has not read.
            tally.Cursor = Math.Max(tally.Cursor, result.Cursor);
            await store.AdvanceCursorAsync(tally.Cursor, cancellationToken).ConfigureAwait(false);

            if (!result.HasMore)
            {
                return true;
            }
        }

        // Backlog longer than one run allows. The cursor has advanced, so the next run continues.
        return true;
    }

    private bool WithinSkew(DateTimeOffset serverUtc) =>
        (serverUtc - utcNow()).Duration() <= MaxClockSkew;

    private sealed class Tally
    {
        internal int Pushed;
        internal int RejectedAsStale;
        internal int TombstonesPushed;
        internal int Pulled;
        internal int Applied;
        internal int Ignored;
        internal int Deleted;
        internal int Undecryptable;
        internal int Malformed;
        internal int ConflictsSaved;
        internal long Cursor;

        internal SyncReport ToReport(SyncOutcome outcome) => new(
            outcome,
            Pushed,
            RejectedAsStale,
            TombstonesPushed,
            Pulled,
            Applied,
            Ignored,
            Deleted,
            Undecryptable,
            Malformed,
            ConflictsSaved,
            Cursor);
    }
}
