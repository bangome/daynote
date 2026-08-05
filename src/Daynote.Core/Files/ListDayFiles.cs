using Daynote.Core.Domain;

namespace Daynote.Core.Files;

/// <summary>
/// Lists a date's attachments newest-first, resolving each row's availability against the file store so
/// the UI can distinguish a present asset from one whose bytes went missing.
/// </summary>
public sealed class ListDayFiles
{
    private readonly IDayFileRepository repository;
    private readonly IFileAssetStore assetStore;

    public ListDayFiles(IDayFileRepository repository, IFileAssetStore assetStore)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
    }

    public async ValueTask<IReadOnlyList<DayFile>> ExecuteAsync(
        LocalDate localDate,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DayFile> files = await repository.GetForDateAsync(localDate, cancellationToken).ConfigureAwait(false);
        var result = new DayFile[files.Count];
        for (int index = 0; index < files.Count; index++)
        {
            bool available = await assetStore.ExistsAsync(files[index].RelativePath, cancellationToken).ConfigureAwait(false);
            result[index] = files[index] with { IsAvailable = available };
        }

        return result;
    }
}
