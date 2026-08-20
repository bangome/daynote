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
/// The transport contract. Deliberately free of HTTP types so the engine above it can be tested
/// against an in-memory server, and free of anything that could carry plaintext.
/// </summary>
public interface ISyncApiClient
{
    ValueTask<PushResult> PushAsync(PushRequest request, CancellationToken cancellationToken = default);

    ValueTask<PullResult> PullAsync(long since, int limit, CancellationToken cancellationToken = default);
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

    ValueTask<MergeOutcome> MergeNotesAsync(
        IReadOnlyList<SyncNote> notes,
        IReadOnlyList<SyncTombstone> tombstones,
        CancellationToken cancellationToken = default);

    ValueTask<SyncStateSnapshot> ReadStateAsync(CancellationToken cancellationToken = default);

    ValueTask AdvanceCursorAsync(long cursor, CancellationToken cancellationToken = default);
}
