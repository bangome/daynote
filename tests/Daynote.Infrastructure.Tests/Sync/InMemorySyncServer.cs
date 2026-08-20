using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// An in-memory stand-in for the Cloudflare Worker, deliberately mirroring
/// <c>cloud/worker/src/sync.ts</c>: same last-write-wins rule, same append-only change log, same
/// grouped paging. It exists so two real local databases can be synced against each other in-process.
/// </summary>
/// <remarks>
/// This is a mirror, not the real thing, so the two can drift. The Worker's own behaviour is pinned
/// by <c>cloud/worker/test/sync.test.ts</c>; the value here is exercising the client engine, the
/// crypto, and the merge against something that behaves like the server.
/// <para>
/// Like the real server it stores only the envelope, the id, and the clock — see
/// <see cref="StoredBlobs"/>, which the tests use to prove no plaintext ever reaches it.
/// </para>
/// </remarks>
internal sealed class InMemorySyncServer
{
    private readonly Dictionary<string, Row> rows = new(StringComparer.Ordinal);
    private readonly List<Entry> log = [];
    private long sequence;

    internal InMemorySyncServer(Func<DateTimeOffset> utcNow)
    {
        UtcNow = utcNow;
    }

    internal Func<DateTimeOffset> UtcNow { get; set; }

    internal int PushCount { get; private set; }

    internal int PullCount { get; private set; }

    /// <summary>Everything the server holds that could conceivably carry content.</summary>
    internal IReadOnlyList<string> StoredBlobs => [.. rows.Values.Select(row => row.Payload).OfType<string>()];

    internal ISyncApiClient ClientFor(string label) => new Client(this, label);

    private PushResult Push(PushRequest request)
    {
        PushCount += 1;
        var acceptedNotes = new List<string>();
        var rejectedNotes = new List<string>();
        var acceptedTombstones = new List<string>();
        var rejectedTombstones = new List<string>();

        foreach (EncryptedNote note in request.Notes)
        {
            // Equal is a reject: re-storing an identical version would append a log row and echo to
            // every device for nothing.
            if (rows.TryGetValue(note.Id, out Row stored) && stored.UpdatedUtc >= note.UpdatedUtc)
            {
                rejectedNotes.Add(note.Id);
                continue;
            }

            rows[note.Id] = new Row(note.Payload, note.UpdatedUtc, null);
            Append(note.Id);
            acceptedNotes.Add(note.Id);
        }

        foreach (EncryptedTombstone tombstone in request.Tombstones)
        {
            if (tombstone.Kind != SyncEntityKind.Note)
            {
                throw new InvalidOperationException("Only note tombstones are supported yet.");
            }

            if (rows.TryGetValue(tombstone.Id, out Row stored) && stored.UpdatedUtc >= tombstone.DeletedUtc)
            {
                rejectedTombstones.Add(tombstone.Id);
                continue;
            }

            // A delete is the row with its blob dropped and the clock set to the deletion instant, so
            // one comparison orders deletes against edits.
            rows[tombstone.Id] = new Row(null, tombstone.DeletedUtc, tombstone.DeletedUtc);
            Append(tombstone.Id);
            acceptedTombstones.Add(tombstone.Id);
        }

        return new PushResult(
            acceptedNotes,
            rejectedNotes,
            acceptedTombstones,
            rejectedTombstones,
            sequence,
            UtcNow());
    }

    private PullResult Pull(long since, int limit)
    {
        PullCount += 1;

        // Collapse the log to the newest entry per entity, exactly as the server's GROUP BY does.
        var newest = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (Entry entry in log.Where(entry => entry.Seq > since))
        {
            newest[entry.Id] = Math.Max(newest.GetValueOrDefault(entry.Id), entry.Seq);
        }

        var changes = new List<PullChange>();
        foreach ((string id, long seq) in newest.OrderBy(pair => pair.Value).Take(limit))
        {
            Row row = rows[id];
            changes.Add(new PullChange(seq, SyncEntityKind.Note, id, row.Payload, row.UpdatedUtc, row.DeletedUtc));
        }

        return new PullResult(
            changes,
            // Holding still on an empty page matters: jumping to the global maximum would skip
            // whatever a concurrent push is mid-write.
            changes.Count > 0 ? changes[^1].Seq : since,
            changes.Count == limit,
            UtcNow());
    }

    private void Append(string id)
    {
        sequence += 1;
        log.Add(new Entry(sequence, id));
    }

    private readonly record struct Row(string? Payload, DateTimeOffset UpdatedUtc, DateTimeOffset? DeletedUtc);

    private readonly record struct Entry(long Seq, string Id);

    private sealed class Client(InMemorySyncServer server, string label) : ISyncApiClient
    {
        public ValueTask<PushResult> PushAsync(PushRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = label;
            return ValueTask.FromResult(server.Push(request));
        }

        public ValueTask<PullResult> PullAsync(long since, int limit, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(server.Pull(since, limit));
        }
    }
}

/// <summary>Collects the losing versions so tests can assert nothing was destroyed silently.</summary>
internal sealed class RecordingConflictSink : ISyncConflictSink
{
    internal List<DisplacedNote> Saved { get; } = [];

    public ValueTask SaveAsync(
        IReadOnlyList<DisplacedNote> displaced,
        CancellationToken cancellationToken = default)
    {
        Saved.AddRange(displaced);
        return ValueTask.CompletedTask;
    }
}
