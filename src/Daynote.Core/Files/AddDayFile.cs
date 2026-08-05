using Daynote.Core.Domain;

namespace Daynote.Core.Files;

/// <summary>
/// Stores a user attachment for a date: hashes and writes the bytes into the content-addressed file
/// store, then records the metadata row (and search document) atomically. If the database write fails
/// after a brand-new asset was created, the orphaned asset is removed so no partial state survives.
/// </summary>
public sealed class AddDayFile
{
    private readonly IDayFileRepository repository;
    private readonly IFileAssetStore assetStore;
    private readonly Func<Guid> idFactory;

    public AddDayFile(IDayFileRepository repository, IFileAssetStore assetStore, Func<Guid>? idFactory = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
        this.idFactory = idFactory ?? Guid.NewGuid;
    }

    public async ValueTask<DayFile> ExecuteAsync(
        LocalDate localDate,
        string displayName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string name = (displayName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("A file display name is required.", nameof(displayName));
        }

        Guid id = idFactory();
        if (id == Guid.Empty)
        {
            throw new InvalidOperationException("The day file ID factory returned an empty ID.");
        }

        PreparedFileAsset asset = await assetStore.PrepareAsync(
            content, FileCapturePolicy.NormalizeExtension(name), cancellationToken).ConfigureAwait(false);
        try
        {
            return await repository.AddAsync(id, localDate, name, asset, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryRemoveFailedNewAssetAsync(asset).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask TryRemoveFailedNewAssetAsync(PreparedFileAsset asset)
    {
        if (!asset.CreatedNew || await repository.IsAssetReferencedAsync(asset.Hash).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await assetStore.DeleteAsync(asset.RelativePath).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
