namespace Daynote.Core.Sync;

/// <summary>A note as it travels: an opaque envelope plus the clock the server orders it by.</summary>
public sealed record EncryptedNote(string Id, string Payload, DateTimeOffset UpdatedUtc);

public sealed record EncryptedTombstone(SyncEntityKind Kind, string Id, DateTimeOffset DeletedUtc);

public sealed record PushRequest(
    IReadOnlyList<EncryptedNote> Notes,
    IReadOnlyList<EncryptedTombstone> Tombstones);

/// <summary>
/// What the server did with a push. Rejections are ordinary, not errors: they mean the server holds
/// something newer, which the next pull will bring down.
/// </summary>
public sealed record PushResult(
    IReadOnlyList<string> AcceptedNoteIds,
    IReadOnlyList<string> RejectedNoteIds,
    IReadOnlyList<string> AcceptedTombstoneIds,
    IReadOnlyList<string> RejectedTombstoneIds,
    long Cursor,
    DateTimeOffset ServerUtc);

/// <summary>
/// One entity's current state. <see cref="Payload"/> is null exactly when
/// <see cref="DeletedUtc"/> is set — the server stores a delete as the row with its blob dropped.
/// </summary>
public sealed record PullChange(
    long Seq,
    SyncEntityKind Kind,
    string Id,
    string? Payload,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? DeletedUtc);

public sealed record PullResult(
    IReadOnlyList<PullChange> Changes,
    long Cursor,
    bool HasMore,
    DateTimeOffset ServerUtc);

/// <summary>
/// The transport could not complete the exchange: offline, a timeout, or a server fault. Distinct
/// from a protocol disagreement, because the caller's response is different — wait and retry rather
/// than tell the user something is wrong.
/// </summary>
public sealed class SyncTransportException(string message, int? status = null, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>HTTP status when there was one; null for "never reached the server".</summary>
    public int? Status { get; } = status;

    /// <summary>True when the session is gone and only a fresh sign-in will help.</summary>
    public bool RequiresSignIn => Status == 401;

    /// <summary>
    /// True when the account is simply not paying for sync. Distinct from 401 on purpose: the
    /// session is valid, so signing out and back in would fix nothing and would lose the cached
    /// data key for no reason.
    /// </summary>
    public bool RequiresSubscription => Status == 402;
}

/// <summary>An attachment's metadata as it travels: a sealed payload plus what the quota counts.</summary>
public sealed record EncryptedFile(
    string Id,
    string Payload,
    string BlindedKey,
    long StoredBytes,
    DateTimeOffset UpdatedUtc);

public sealed record FilePushRequest(
    IReadOnlyList<EncryptedFile> Files,
    IReadOnlyList<EncryptedTombstone> Tombstones);

/// <summary>
/// What the server did with an attachment push.
/// </summary>
/// <remarks>
/// <see cref="FilesBlocked"/> is not a rejection and must not be handled as one. A rejection is
/// settled — the server holds something newer — so the queue entry goes. This means the opposite:
/// the upload is still owed and has to stay queued until there is a subscription. Treating the two
/// alike would discard the attachment silently (docs/CLOUD_SYNC.md §14).
/// </remarks>
public sealed record FilePushResult(
    IReadOnlyList<string> AcceptedFileIds,
    IReadOnlyList<string> RejectedFileIds,
    IReadOnlyList<string> AcceptedTombstoneIds,
    IReadOnlyList<string> RejectedTombstoneIds,
    bool FilesBlocked,
    DateTimeOffset ServerUtc);

/// <summary>
/// The transport contract. Deliberately free of HTTP types so the engine above it can be tested
/// against an in-memory server, and free of anything that could carry plaintext.
/// </summary>
public interface ISyncApiClient
{
    ValueTask<PushResult> PushAsync(PushRequest request, CancellationToken cancellationToken = default);

    ValueTask<PullResult> PullAsync(long since, int limit, CancellationToken cancellationToken = default);

    ValueTask<FilePushResult> PushFilesAsync(
        FilePushRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores one sealed attachment. Idempotent, because the name is a hash of the content: a retry
    /// after a dropped connection rewrites the same bytes rather than duplicating them.
    /// </summary>
    ValueTask UploadAssetAsync(
        string blindedKey,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one sealed attachment, or null when the object is not there yet. Null is ordinary:
    /// metadata and bytes are two calls, so another device's file row can arrive first.
    /// </summary>
    ValueTask<byte[]?> DownloadAssetAsync(
        string blindedKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Receives the local note versions that last-write-wins discarded, so they can be written somewhere
/// the user can find them. Sync must never destroy an edit without leaving a copy
/// (docs/CLOUD_SYNC.md §7.4).
/// </summary>
public interface ISyncConflictSink
{
    ValueTask SaveAsync(IReadOnlyList<DisplacedNote> displaced, CancellationToken cancellationToken = default);
}

/// <summary>
/// The local store, as the engine needs it. Mirrors <c>SqliteSyncStore</c>; exists so the merge and
/// the engine can be exercised without a database when that is the clearer test.
/// </summary>
public interface ISyncStore
{
    ValueTask<int> EnrollExistingContentAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PendingNote>> ReadPendingNotesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SyncTombstone>> ReadPendingTombstonesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<int> AcknowledgePushAsync(
        IReadOnlyList<PendingAck> acknowledged,
        CancellationToken cancellationToken = default);

    ValueTask<int> AcknowledgeTombstonesAsync(
        IReadOnlyList<SyncTombstone> acknowledged,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PendingFile>> ReadPendingFilesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<MergeOutcome> MergeNotesAsync(
        IReadOnlyList<SyncNote> notes,
        IReadOnlyList<SyncTombstone> tombstones,
        CancellationToken cancellationToken = default);

    ValueTask<SyncStateSnapshot> ReadStateAsync(CancellationToken cancellationToken = default);

    ValueTask AdvanceCursorAsync(long cursor, CancellationToken cancellationToken = default);

    ValueTask SignInAsync(string userId, int dekGeneration, CancellationToken cancellationToken = default);

    ValueTask SignOutAsync(CancellationToken cancellationToken = default);

    ValueTask SetLockedAsync(bool locked, CancellationToken cancellationToken = default);

    ValueTask<FileMergeOutcome> MergeFilesAsync(
        IReadOnlyList<SyncFile> files,
        IReadOnlyList<SyncTombstone> tombstones,
        CancellationToken cancellationToken = default);

    /// <summary>Content hashes whose bytes still have to move, oldest failure last.</summary>
    ValueTask<IReadOnlyList<string>> ReadAssetQueueAsync(
        AssetDirection direction,
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask EnqueueAssetAsync(
        string assetHash,
        AssetDirection direction,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a queue entry once its bytes have arrived at the other end.</summary>
    ValueTask DequeueAssetAsync(string assetHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a transfer failed, so the next run can order by attempts and a permanently
    /// broken asset cannot stall the ones behind it.
    /// </summary>
    ValueTask RecordAssetFailureAsync(
        string assetHash,
        string error,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The attachment bytes on this device, as the sync engine needs them.
/// </summary>
/// <remarks>
/// A narrow view over the content-addressed store and <c>file_assets</c> rather than a second copy
/// of either: the engine only ever needs "give me the bytes for this hash" and "here are the bytes
/// for this hash". Keeping it this small is what lets the engine be tested with a dictionary.
/// </remarks>
public interface ISyncAssetStore
{
    /// <summary>True when this device already holds the bytes, so nothing has to be downloaded.</summary>
    ValueTask<bool> ContainsAsync(string contentHash, CancellationToken cancellationToken = default);

    /// <summary>The plaintext bytes, or null if the asset has gone missing since it was queued.</summary>
    ValueTask<byte[]?> ReadAsync(string contentHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores downloaded bytes under the hash they were fetched for, verifying that they hash to
    /// it. The verification is not ceremony: it is what stops a substituted object from being
    /// written into a content-addressed store under the wrong name.
    /// </summary>
    ValueTask<bool> SaveAsync(
        string contentHash,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default);
}
