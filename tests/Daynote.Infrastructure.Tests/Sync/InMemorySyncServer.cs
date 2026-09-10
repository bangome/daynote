using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// An in-memory stand-in for the Cloudflare Worker, deliberately mirroring
/// <c>cloud/worker/src/sync.ts</c> and <c>files.ts</c>: same last-write-wins rule, same append-only
/// change log, same grouped paging, same placement of the paywall. It exists so two real local
/// databases can be synced against each other in-process.
/// </summary>
/// <remarks>
/// This is a mirror, not the real thing, so the two can drift. The Worker's own behaviour is pinned
/// by <c>cloud/worker/test/sync.test.ts</c> and <c>files.test.ts</c>; the value here is exercising
/// the client engine, the crypto, and the merge against something that behaves like the server.
/// <para>
/// Like the real server it stores only the envelope, the id, and the clock — see
/// <see cref="StoredBlobs"/>, which the tests use to prove no plaintext ever reaches it.
/// </para>
/// </remarks>
internal sealed class InMemorySyncServer
{
    private readonly Dictionary<SyncEntityRef, Row> rows = [];
    private readonly Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);
    private readonly List<Entry> log = [];
    private long sequence;

    internal InMemorySyncServer(Func<DateTimeOffset> utcNow)
    {
        UtcNow = utcNow;
    }

    internal Func<DateTimeOffset> UtcNow { get; set; }

    internal int PushCount { get; private set; }

    internal int PullCount { get; private set; }

    /// <summary>
    /// Whether the account may move attachment bytes. False is a lapsed subscription: text still
    /// syncs, deletes still propagate, and nothing already stored is touched.
    /// </summary>
    internal bool EntitledToFiles { get; set; } = true;

    /// <summary>Everything the server holds that could conceivably carry content.</summary>
    internal IReadOnlyList<string> StoredBlobs => [.. rows.Values.Select(row => row.Payload).OfType<string>()];

    /// <summary>The sealed attachment objects, by blinded key. Never plaintext.</summary>
    internal IReadOnlyDictionary<string, byte[]> StoredObjects => objects;

    /// <summary>
    /// Makes every download answer "not there yet", without discarding anything.
    /// </summary>
    /// <remarks>
    /// Models the one gap the design accepts: metadata and bytes are two calls, so a device can
    /// pull a file row whose object it cannot fetch. Hiding rather than deleting is the point —
    /// the test then turns it off and checks that the next run finishes the job.
    /// </remarks>
    internal bool ObjectsHidden { get; set; }

    /// <summary>
    /// Removes the attachment routes entirely, as a service deployed before Phase 7 has them.
    /// Answers 404 the way the Worker answers an unknown path.
    /// </summary>
    internal bool SupportsAttachments { get; set; } = true;

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
            var key = new SyncEntityRef(SyncEntityKind.Note, note.Id);

            // Equal is a reject: re-storing an identical version would append a log row and echo to
            // every device for nothing.
            if (rows.TryGetValue(key, out Row stored) && stored.UpdatedUtc >= note.UpdatedUtc)
            {
                rejectedNotes.Add(note.Id);
                continue;
            }

            rows[key] = new Row(note.Payload, note.UpdatedUtc, null, null);
            Append(key);
            acceptedNotes.Add(note.Id);
        }

        foreach (EncryptedTombstone tombstone in request.Tombstones)
        {
            if (tombstone.Kind != SyncEntityKind.Note)
            {
                throw new InvalidOperationException("Attachment tombstones go to /v1/files/push.");
            }

            var key = new SyncEntityRef(SyncEntityKind.Note, tombstone.Id);
            if (rows.TryGetValue(key, out Row stored) && stored.UpdatedUtc >= tombstone.DeletedUtc)
            {
                rejectedTombstones.Add(tombstone.Id);
                continue;
            }

            // A delete is the row with its blob dropped and the clock set to the deletion instant, so
            // one comparison orders deletes against edits.
            rows[key] = new Row(null, tombstone.DeletedUtc, tombstone.DeletedUtc, null);
            Append(key);
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

    /// <summary>
    /// Attachment metadata and deletes, with the paywall exactly where the Worker puts it: upserts
    /// are withheld from a lapsed account, tombstones never are.
    /// </summary>
    private FilePushResult PushFiles(FilePushRequest request)
    {
        RequireAttachmentRoutes();
        PushCount += 1;
        var acceptedFiles = new List<string>();
        var rejectedFiles = new List<string>();
        var acceptedTombstones = new List<string>();
        var rejectedTombstones = new List<string>();
        bool blocked = !EntitledToFiles && request.Files.Count > 0;

        foreach (EncryptedFile file in blocked ? [] : request.Files)
        {
            var key = new SyncEntityRef(SyncEntityKind.File, file.Id);
            if (rows.TryGetValue(key, out Row stored) && stored.UpdatedUtc >= file.UpdatedUtc)
            {
                rejectedFiles.Add(file.Id);
                continue;
            }

            rows[key] = new Row(file.Payload, file.UpdatedUtc, null, file.BlindedKey);
            Append(key);
            acceptedFiles.Add(file.Id);
        }

        foreach (EncryptedTombstone tombstone in request.Tombstones)
        {
            var key = new SyncEntityRef(SyncEntityKind.File, tombstone.Id);
            if (rows.TryGetValue(key, out Row stored) && stored.UpdatedUtc > tombstone.DeletedUtc)
            {
                rejectedTombstones.Add(tombstone.Id);
                continue;
            }

            if (stored.BlindedKey is { } released)
            {
                objects.Remove(released);
            }

            rows[key] = new Row(null, tombstone.DeletedUtc, tombstone.DeletedUtc, null);
            Append(key);
            acceptedTombstones.Add(tombstone.Id);
        }

        return new FilePushResult(
            acceptedFiles,
            rejectedFiles,
            acceptedTombstones,
            rejectedTombstones,
            blocked,
            UtcNow());
    }

    private PullResult Pull(long since, int limit)
    {
        PullCount += 1;

        // Collapse the log to the newest entry per entity, exactly as the server's GROUP BY does.
        var newest = new Dictionary<SyncEntityRef, long>();
        foreach (Entry entry in log.Where(entry => entry.Seq > since))
        {
            newest[entry.Key] = Math.Max(newest.GetValueOrDefault(entry.Key), entry.Seq);
        }

        var changes = new List<PullChange>();
        foreach ((SyncEntityRef key, long seq) in newest.OrderBy(pair => pair.Value).Take(limit))
        {
            Row row = rows[key];
            // Notes and files share one page and one cursor, as the Worker's pull does.
            changes.Add(new PullChange(seq, key.Kind, key.Id, row.Payload, row.UpdatedUtc, row.DeletedUtc));
        }

        return new PullResult(
            changes,
            // Holding still on an empty page matters: jumping to the global maximum would skip
            // whatever a concurrent push is mid-write.
            changes.Count > 0 ? changes[^1].Seq : since,
            changes.Count == limit,
            UtcNow());
    }

    private void Upload(string blindedKey, ReadOnlyMemory<byte> body)
    {
        RequireAttachmentRoutes();
        RequireEntitlement();
        objects[blindedKey] = body.ToArray();
    }

    private byte[]? Download(string blindedKey)
    {
        RequireAttachmentRoutes();
        RequireEntitlement();
        return !ObjectsHidden && objects.TryGetValue(blindedKey, out byte[]? stored) ? stored : null;
    }

    private void RequireAttachmentRoutes()
    {
        if (!SupportsAttachments)
        {
            throw new SyncTransportException("No such endpoint.", 404);
        }
    }

    private void RequireEntitlement()
    {
        if (!EntitledToFiles)
        {
            throw new SyncTransportException("Syncing attachments needs a subscription.", 402);
        }
    }

    private void Append(SyncEntityRef key)
    {
        sequence += 1;
        log.Add(new Entry(sequence, key));
    }

    private readonly record struct Row(
        string? Payload,
        DateTimeOffset UpdatedUtc,
        DateTimeOffset? DeletedUtc,
        string? BlindedKey);

    private readonly record struct Entry(long Seq, SyncEntityRef Key);

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

        public ValueTask<FilePushResult> PushFilesAsync(
            FilePushRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(server.PushFiles(request));
        }

        public ValueTask UploadAssetAsync(
            string blindedKey,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            server.Upload(blindedKey, body);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> DownloadAssetAsync(
            string blindedKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(server.Download(blindedKey));
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
