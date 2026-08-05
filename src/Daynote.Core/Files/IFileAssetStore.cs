namespace Daynote.Core.Files;

public sealed record PreparedFileAsset(
    string Hash,
    string RelativePath,
    long ByteLength,
    bool CreatedNew);

/// <summary>
/// Thrown when an attachment exceeds <see cref="FileCapturePolicy.MaxFileBytes"/>. The store enforces the
/// cap while streaming and rejects before any physical file or database row is committed.
/// </summary>
public sealed class DayFileTooLargeException(long byteLength)
    : Exception("The attachment exceeds the maximum supported size.")
{
    public long ByteLength { get; } = byteLength;
}

/// <summary>
/// Content-addressed store for day-file bytes: temp-write then atomic rename, shared references per
/// content hash, physical delete only after the database commits, and startup reconciliation of orphans
/// and stale temp files. Reports via <see cref="AssetReconciliationResult"/>.
/// </summary>
public interface IFileAssetStore
{
    ValueTask<PreparedFileAsset> PrepareAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ExistsAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask<byte[]?> ReadAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    ValueTask<AssetReconciliationResult> ReconcileAsync(
        IReadOnlySet<string> referencedPaths,
        CancellationToken cancellationToken = default);
}
