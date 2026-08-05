namespace Daynote.Core.Search;

public interface ISearchRepository
{
    ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
