using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// The attachment half of a sync cycle (docs/CLOUD_SYNC.md §5, Phase 7).
/// </summary>
/// <remarks>
/// An attachment is two things that move separately: a sealed metadata row, and the bytes. The
/// order between them is the one rule that matters, and it is the same in both directions —
/// **bytes first, metadata second**. A metadata row that arrives before its object leaves the other
/// devices pointing at something that is not there; the reverse leaves an unreferenced object,
/// which the server sweeps and which costs nothing but a retry.
/// <para>
/// Nothing here runs when the engine was built without an <see cref="ISyncAssetStore"/>. That is
/// how a caller that does not do attachments — and every test that is about notes — stays
/// unaffected.
/// </para>
/// </remarks>
public sealed partial class SyncEngine
{
    /// <summary>Attachments are far larger than notes, so a page of them is far smaller.</summary>
    private const int FilePushBatch = 50;

    /// <summary>
    /// How many objects one run will move in each direction. A bound rather than "everything":
    /// a first sync of a folder of photographs should make visible progress and come back, not
    /// occupy the connection until it finishes.
    /// </summary>
    private const int AssetBatch = 20;

    /// <summary>
    /// Pushes attachment deletes and metadata, uploading the bytes each new row will refer to.
    /// </summary>
    /// <returns>False when the server's clock has drifted out of range; the run stops.</returns>
    private async ValueTask<bool> PushFilesAsync(
        SyncSession session,
        Tally tally,
        CancellationToken cancellationToken)
    {
        if (assets is null)
        {
            return true;
        }

        IReadOnlyList<SyncTombstone> tombstones = await ReadFileTombstonesAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<PendingFile> pending = await store
            .ReadPendingFilesAsync(FilePushBatch, cancellationToken)
            .ConfigureAwait(false);

        if (tombstones.Count == 0 && pending.Count == 0)
        {
            return true;
        }

        // Bytes before metadata. An attachment whose upload fails is simply left out of this push:
        // its outbox entry stays, and the next run tries again. Sending its metadata anyway would
        // publish a row every other device would fail to resolve.
        var sendable = new List<PendingFile>(pending.Count);
        var encrypted = new List<EncryptedFile>(pending.Count);
        foreach (PendingFile entry in pending)
        {
            string? blindedKey = await UploadAssetAsync(session, tally, entry.File.AssetHash, cancellationToken)
                .ConfigureAwait(false);
            if (blindedKey is null)
            {
                continue;
            }

            sendable.Add(entry);
            encrypted.Add(new EncryptedFile(
                entry.File.Id,
                crypto.Encrypt(
                    FilePayloadCodec.Serialize(entry.File),
                    session.DataKey,
                    CipherScope.File(session.UserId, entry.File.Id)),
                blindedKey,
                // What the server counts against the quota is the sealed length, not the plaintext.
                SealedLength(entry.File.ByteLength),
                // The queue time is the attachment's clock. `day_files` has no updated_utc because
                // an attachment is never edited — it is added or removed — so the instant it was
                // enqueued is the only monotonic thing to order it by.
                entry.QueuedUtc));
        }

        if (encrypted.Count == 0 && tombstones.Count == 0)
        {
            return true;
        }

        FilePushResult result = await api
            .PushFilesAsync(
                new FilePushRequest(
                    encrypted,
                    [.. tombstones.Select(t => new EncryptedTombstone(t.Kind, t.Id, t.DeletedUtc))]),
                cancellationToken)
            .ConfigureAwait(false);

        if (!WithinSkew(result.ServerUtc))
        {
            return false;
        }

        tally.FilesPushed += result.AcceptedFileIds.Count;
        // Not an outcome of its own: text synced fine, and this run is a success with one part of
        // it withheld. The caller shows a chip, not an error (docs/CLOUD_SYNC.md §14).
        tally.FileSyncBlocked |= result.FilesBlocked;

        var settledFiles = new HashSet<string>(result.AcceptedFileIds, StringComparer.Ordinal);
        settledFiles.UnionWith(result.RejectedFileIds);
        PendingAck[] acknowledged =
        [
            .. sendable
                .Where(entry => settledFiles.Contains(entry.File.Id))
                .Select(entry => new PendingAck(SyncEntityKind.File, entry.File.Id, entry.QueuedUtc)),
        ];
        if (acknowledged.Length > 0)
        {
            await store.AcknowledgePushAsync(acknowledged, cancellationToken).ConfigureAwait(false);
        }

        var settledTombstones = new HashSet<string>(result.AcceptedTombstoneIds, StringComparer.Ordinal);
        settledTombstones.UnionWith(result.RejectedTombstoneIds);
        SyncTombstone[] buried = [.. tombstones.Where(t => settledTombstones.Contains(t.Id))];
        if (buried.Length > 0)
        {
            await store.AcknowledgeTombstonesAsync(buried, cancellationToken).ConfigureAwait(false);
            tally.TombstonesPushed += result.AcceptedTombstoneIds.Count;
        }

        return true;
    }

    /// <summary>
    /// Ensures one attachment's bytes are in the cloud, and answers with the key they are under.
    /// </summary>
    /// <returns>
    /// The blinded key, or null when the bytes could not be sent — the asset has gone missing
    /// locally, or the upload failed. Null means "leave this attachment queued", never "give up".
    /// </returns>
    private async ValueTask<string?> UploadAssetAsync(
        SyncSession session,
        Tally tally,
        string assetHash,
        CancellationToken cancellationToken)
    {
        string blindedKey = crypto.BlindAssetKey(session.DataKey, assetHash);

        byte[]? plaintext = await assets!.ReadAsync(assetHash, cancellationToken).ConfigureAwait(false);
        if (plaintext is null)
        {
            // The row survived but the file behind it did not — a restore from a backup that lost
            // the assets folder, or a manual deletion. Recorded and skipped: there is nothing to
            // upload, and failing the whole run over it would stop every other attachment too.
            await store
                .RecordAssetFailureAsync(assetHash, "The attachment is missing on this device.", cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        try
        {
            await api
                .UploadAssetAsync(
                    blindedKey,
                    crypto.EncryptAsset(plaintext, session.DataKey, CipherScope.Asset(session.UserId, assetHash)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SyncTransportException transport) when (transport.RequiresSubscription)
        {
            // The paywall, reached by the upload rather than the metadata push. Same handling: the
            // queue keeps the attachment, and the run reports that file sync is withheld.
            tally.FileSyncBlocked = true;
            return null;
        }

        await store.DequeueAssetAsync(assetHash, cancellationToken).ConfigureAwait(false);
        tally.AssetsUploaded += 1;
        return blindedKey;
    }

    /// <summary>
    /// Fetches the bytes for attachments this device learned about but does not hold.
    /// </summary>
    /// <remarks>
    /// Runs after the merge, not during it: the merge is one database transaction, and holding it
    /// open across a series of network fetches would lock the database for as long as the slowest
    /// of them.
    /// </remarks>
    private async ValueTask DownloadAssetsAsync(
        SyncSession session,
        Tally tally,
        CancellationToken cancellationToken)
    {
        if (assets is null)
        {
            return;
        }

        IReadOnlyList<string> queued = await store
            .ReadAssetQueueAsync(AssetDirection.Down, AssetBatch, cancellationToken)
            .ConfigureAwait(false);

        foreach (string assetHash in queued)
        {
            if (await assets.ContainsAsync(assetHash, cancellationToken).ConfigureAwait(false))
            {
                // Another attachment of identical content already brought it down.
                await store.DequeueAssetAsync(assetHash, cancellationToken).ConfigureAwait(false);
                continue;
            }

            byte[]? sealedBytes;
            try
            {
                sealedBytes = await api
                    .DownloadAssetAsync(
                        crypto.BlindAssetKey(session.DataKey, assetHash),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SyncTransportException transport) when (transport.RequiresSubscription)
            {
                tally.FileSyncBlocked = true;
                return;
            }

            if (sealedBytes is null)
            {
                // The metadata outran the bytes. Ordinary between two devices, and a retry: the
                // queue entry stays and the attempt is counted so it cannot spin unnoticed.
                await store
                    .RecordAssetFailureAsync(assetHash, "The attachment has not been uploaded yet.", cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            DomainResult<byte[]> opened = crypto.DecryptAsset(
                sealedBytes,
                session.DataKey,
                CipherScope.Asset(session.UserId, assetHash));
            if (!opened.IsSuccess)
            {
                tally.Undecryptable += 1;
                await store
                    .RecordAssetFailureAsync(assetHash, opened.Error.Message, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            // The store hashes what it is given and refuses a mismatch, so an object substituted
            // for another cannot be written under this name even if it decrypted.
            if (!await assets.SaveAsync(assetHash, opened.Value, cancellationToken).ConfigureAwait(false))
            {
                tally.Malformed += 1;
                await store
                    .RecordAssetFailureAsync(assetHash, "The downloaded bytes do not match their hash.", cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            await store.DequeueAssetAsync(assetHash, cancellationToken).ConfigureAwait(false);
            tally.AssetsDownloaded += 1;
        }
    }

    /// <summary>Applies one pulled page's attachment changes and queues the bytes they need.</summary>
    private async ValueTask MergeFilesAsync(
        SyncSession session,
        Tally tally,
        IReadOnlyList<PullChange> changes,
        CancellationToken cancellationToken)
    {
        if (assets is null || changes.Count == 0)
        {
            return;
        }

        var files = new List<SyncFile>();
        var tombstones = new List<SyncTombstone>();
        foreach (PullChange change in changes)
        {
            if (change.DeletedUtc is not null || change.Payload is null)
            {
                tombstones.Add(new SyncTombstone(
                    SyncEntityKind.File,
                    change.Id,
                    change.DeletedUtc ?? change.UpdatedUtc));
                continue;
            }

            DomainResult<string> opened = crypto.Decrypt(
                change.Payload,
                session.DataKey,
                CipherScope.File(session.UserId, change.Id));
            if (!opened.IsSuccess)
            {
                tally.Undecryptable += 1;
                continue;
            }

            DomainResult<SyncFile> file = FilePayloadCodec.Deserialize(
                change.Id,
                opened.Value,
                change.UpdatedUtc);
            if (!file.IsSuccess)
            {
                tally.Malformed += 1;
                continue;
            }

            files.Add(file.Value);
        }

        if (files.Count == 0 && tombstones.Count == 0)
        {
            return;
        }

        FileMergeOutcome outcome = await store
            .MergeFilesAsync(files, tombstones, cancellationToken)
            .ConfigureAwait(false);

        tally.Applied += outcome.Applied;
        tally.Ignored += outcome.Ignored;
        tally.Deleted += outcome.Deleted;
        tally.FilesPulled += files.Count;

        foreach (string assetHash in outcome.Downloadable)
        {
            await store
                .EnqueueAssetAsync(assetHash, AssetDirection.Down, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The attachment deletes waiting to be sent, taken from the same queue the notes use.
    /// </summary>
    private async ValueTask<IReadOnlyList<SyncTombstone>> ReadFileTombstonesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SyncTombstone> pending = await store
            .ReadPendingTombstonesAsync(FilePushBatch, cancellationToken)
            .ConfigureAwait(false);
        return [.. pending.Where(static t => t.Kind == SyncEntityKind.File)];
    }

    /// <summary>
    /// What an attachment of this size occupies once sealed: a 12-byte nonce and a 16-byte tag on
    /// top of the plaintext, because AES-GCM is a stream cipher and does not pad.
    /// </summary>
    private static long SealedLength(long plaintextBytes) => plaintextBytes + 12 + 16;
}
