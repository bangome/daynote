namespace Daynote.Core.Files;

/// <summary>
/// Deletes an attachment reference-safely: the database row (and its search document) are removed first,
/// and the physical asset is deleted only when this was its last reference, and only after the commit. A
/// failed physical delete leaves the committed deletion intact and flags cleanup as pending for startup
/// reconciliation.
/// </summary>
public sealed class DeleteDayFile
{
    private readonly IDayFileRepository repository;
    private readonly IFileAssetStore assetStore;

    public DeleteDayFile(IDayFileRepository repository, IFileAssetStore assetStore)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
    }

    public async ValueTask<DayFileDeleteReceipt> ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        DayFileDeleteResult result = await repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!result.Deleted || result.ReleasedAssetPath is null)
        {
            return new DayFileDeleteReceipt(result.Deleted, CleanupPending: false);
        }

        try
        {
            await assetStore.DeleteAsync(result.ReleasedAssetPath, CancellationToken.None).ConfigureAwait(false);
            return new DayFileDeleteReceipt(Deleted: true, CleanupPending: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DayFileDeleteReceipt(Deleted: true, CleanupPending: true);
        }
    }
}
