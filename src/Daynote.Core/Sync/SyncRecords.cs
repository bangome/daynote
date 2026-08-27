using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

public enum SyncEntityKind
{
    Note,
    File,
}

public readonly record struct SyncEntityRef(SyncEntityKind Kind, string Id);

/// <summary>
/// Everything about one note that the server needs, as plaintext. The sync engine serialises this to
/// JSON and encrypts it as a single blob (docs/CLOUD_SYNC.md §5.1); the server sees only the blob and
/// <see cref="UpdatedUtc"/>.
/// </summary>
public sealed record SyncNote(
    string Id,
    LocalDate LocalDate,
    string Title,
    string Body,
    int SortOrder,
    bool IsFavorite,
    bool HasCustomTitle,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record SyncTombstone(SyncEntityKind Kind, string Id, DateTimeOffset DeletedUtc);

/// <summary>
/// An outbox entry. <see cref="QueuedUtc"/> is carried separately from the entity so the push
/// acknowledgement can be conditional on it: if the user edits the same note while the push is in
/// flight, the queue entry has moved on and must survive, or that edit is never sent.
/// </summary>
public sealed record PendingNote(SyncNote Note, DateTimeOffset QueuedUtc);

public sealed record PendingFile(SyncFile File, DateTimeOffset QueuedUtc);

public readonly record struct PendingAck(SyncEntityKind Kind, string Id, DateTimeOffset QueuedUtc);

/// <summary>Attachment metadata. Bytes travel separately, keyed by <see cref="AssetHash"/>.</summary>
public sealed record SyncFile(
    string Id,
    LocalDate LocalDate,
    string DisplayName,
    long ByteLength,
    string AssetHash,
    DateTimeOffset CreatedUtc);

/// <summary>
/// A local note version that last-write-wins discarded. Handed to the caller so it can be written to
/// <c>%LocalAppData%\Daynote\conflicts\</c> before it is gone (docs/CLOUD_SYNC.md §7.4). Losing an
/// edit silently is not an acceptable outcome of a background sync.
/// </summary>
public sealed record DisplacedNote(
    string Id,
    LocalDate LocalDate,
    string Title,
    string Body,
    DateTimeOffset UpdatedUtc);

public sealed record MergeOutcome(
    int Applied,
    int Ignored,
    int Deleted,
    IReadOnlyList<DisplacedNote> Displaced)
{
    public static MergeOutcome Empty { get; } = new(0, 0, 0, []);
}

public sealed record SyncStateSnapshot(
    string? UserId,
    long ServerCursor,
    int DekGeneration,
    bool IsLocked,
    DateTimeOffset? LastSyncUtc)
{
    public bool IsSignedIn => UserId is not null;
}
