namespace Daynote.Core.Search;

public sealed class SearchService
{
    public const int PageSize = 50;
    private readonly ISearchRepository repository;

    public SearchService(ISearchRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async ValueTask<SearchPage> SearchAsync(
        string? text,
        int pageNumber = 0,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 0) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        cancellationToken.ThrowIfCancellationRequested();
        SearchQuery query = SearchQuery.Create(text);
        if (query.IsEmpty) return SearchPage.Empty(pageNumber);

        int offset = checked(pageNumber * PageSize);
        IReadOnlyList<SearchResult> fetched = await repository.SearchAsync(
            query, offset, PageSize + 1, cancellationToken).ConfigureAwait(false);
        bool hasMore = fetched.Count > PageSize;
        return new SearchPage(
            hasMore ? fetched.Take(PageSize).ToArray() : fetched,
            pageNumber,
            hasMore);
    }
}
