using Daynote.Core.Files;

namespace Daynote.Infrastructure.Assets;

/// <summary>
/// Startup reconciliation for the day-file store: it deletes physical assets no longer referenced by any
/// <c>day_files</c> row and any stale temp files left by an interrupted write.
/// </summary>
public sealed class FileAssetReconciler
{
    private readonly IDayFileRepository repository;
    private readonly IFileAssetStore assetStore;

    public FileAssetReconciler(IDayFileRepository repository, IFileAssetStore assetStore)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
    }

    public async ValueTask<AssetReconciliationResult> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> referenced = await repository.GetReferencedAssetPathsAsync(
            cancellationToken).ConfigureAwait(false);
        return await assetStore.ReconcileAsync(referenced, cancellationToken).ConfigureAwait(false);
    }
}
