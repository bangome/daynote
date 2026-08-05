using Daynote.Core.Domain;

namespace Daynote.Core.Search;

public enum SearchSourceType
{
    Note = 1,
    ClipboardText = 2,
    File = 3,
}

public sealed record SearchResult(
    SearchSourceType SourceType,
    Guid SourceId,
    LocalDate LocalDate,
    string Title,
    string Snippet,
    double Score);

public sealed record SearchPage(
    IReadOnlyList<SearchResult> Results,
    int PageNumber,
    bool HasMore)
{
    public static SearchPage Empty(int pageNumber) => new([], pageNumber, HasMore: false);
}
