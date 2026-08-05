using Daynote.App.Composition;
using Daynote.Core.Domain;
using Daynote.Core.Search;

namespace Daynote.App.Search;

/// <summary>
/// One search result row. Wraps a <see cref="SearchResult"/> and carries the STABLE source id
/// (note id or clipboard item id) used for deep-link navigation, never a mutable display number.
/// Payload beyond the returned title/snippet is never surfaced (DESIGN Section 5).
/// </summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        SourceType = result.SourceType;
        SourceId = result.SourceId;
        LocalDate = result.LocalDate;
        Title = result.Title;
        Snippet = result.Snippet;
        DateDisplay = LocalDates.DisplayLong(result.LocalDate);
    }

    /// <summary>The stable source identity (note id / clipboard item id) for deep linking.</summary>
    public Guid SourceId { get; }

    public SearchSourceType SourceType { get; }

    public LocalDate LocalDate { get; }

    public string Title { get; }

    public string Snippet { get; }

    public string DateDisplay { get; }

    public bool IsNote => SourceType == SearchSourceType.Note;

    public bool IsClipboard => SourceType == SearchSourceType.ClipboardText;

    public string KindDisplay => IsNote ? Localization.AppStrings.SearchKindNote : Localization.AppStrings.SearchKindClipboard;

    /// <summary>Style key for the source badge; distinct geometry survives color loss.</summary>
    public string SourceStyleKey => IsNote
        ? "Daynote.Style.SearchSource.Note"
        : "Daynote.Style.SearchSource.Clipboard";
}
