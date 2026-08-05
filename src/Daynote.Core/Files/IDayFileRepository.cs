using Daynote.Core.Domain;

namespace Daynote.Core.Files;

public interface IDayFileRepository
{
    ValueTask<DayFile> AddAsync(
        Guid id,
        LocalDate localDate,
        string displayName,
        PreparedFileAsset asset,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<DayFile>> GetForDateAsync(
        LocalDate localDate,
        CancellationToken cancellationToken = default);

    ValueTask<DayFileDeleteResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlySet<string>> GetReferencedAssetPathsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsAssetReferencedAsync(
        string hash,
        CancellationToken cancellationToken = default);
}
