namespace Daynote.Core.Files;

/// <summary>
/// The outcome of a content-addressed store startup reconciliation: how many stale temp files and how
/// many orphaned (unreferenced) assets were deleted.
/// </summary>
public sealed record AssetReconciliationResult(int TemporaryFilesDeleted, int OrphanFilesDeleted);
