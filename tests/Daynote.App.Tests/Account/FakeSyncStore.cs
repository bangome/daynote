using Daynote.Core.Sync;

namespace Daynote.App.Tests.Account;

/// <summary>
/// An in-memory sync store. Only the account-facing state matters here — the merge and queue
/// behaviour is covered against a real database in the infrastructure suite.
/// </summary>
internal sealed class FakeSyncStore : ISyncStore
{
    internal SyncStateSnapshot State { get; set; } = new(null, 0, 0, false, null);

    public ValueTask<SyncStateSnapshot> ReadStateAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(State);

    public ValueTask<int> EnrollExistingContentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(0);

    public ValueTask<IReadOnlyList<PendingNote>> ReadPendingNotesAsync(int limit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PendingNote>>([]);

    public ValueTask<IReadOnlyList<SyncTombstone>> ReadPendingTombstonesAsync(int limit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<SyncTombstone>>([]);

    public ValueTask<int> AcknowledgePushAsync(IReadOnlyList<PendingAck> acknowledged, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(0);

    public ValueTask<int> AcknowledgeTombstonesAsync(IReadOnlyList<SyncTombstone> acknowledged, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(0);

    public ValueTask<MergeOutcome> MergeNotesAsync(IReadOnlyList<SyncNote> notes, IReadOnlyList<SyncTombstone> tombstones, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(MergeOutcome.Empty);

    public ValueTask AdvanceCursorAsync(long cursor, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask SignInAsync(string userId, int dekGeneration, CancellationToken cancellationToken = default)
    {
        State = State with { UserId = userId, DekGeneration = dekGeneration };
        return ValueTask.CompletedTask;
    }

    public ValueTask SignOutAsync(CancellationToken cancellationToken = default)
    {
        State = new SyncStateSnapshot(null, 0, 0, false, null);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetLockedAsync(bool locked, CancellationToken cancellationToken = default)
    {
        State = State with { IsLocked = locked };
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<PendingFile>> ReadPendingFilesAsync(int limit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PendingFile>>([]);

    public ValueTask<FileMergeOutcome> MergeFilesAsync(IReadOnlyList<SyncFile> files, IReadOnlyList<SyncTombstone> tombstones, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(FileMergeOutcome.Empty);

    public ValueTask<IReadOnlyList<string>> ReadAssetQueueAsync(AssetDirection direction, int limit, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<string>>([]);

    public ValueTask EnqueueAssetAsync(string assetHash, AssetDirection direction, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask DequeueAssetAsync(string assetHash, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask RecordAssetFailureAsync(string assetHash, string error, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
