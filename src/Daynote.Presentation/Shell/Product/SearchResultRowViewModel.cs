using CommunityToolkit.Mvvm.Input;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One row in the titlebar search dropdown: a kind badge (노트/클립/파일/날짜), a title, a muted sub line,
/// a right-aligned date label, and the exact navigation it performs when activated.
/// </summary>
public sealed partial class SearchResultRowViewModel
{
    private readonly SearchNavigation _navigation;
    private readonly Func<SearchNavigation, Task> _onActivate;

    public SearchResultRowViewModel(
        string kind,
        string title,
        string sub,
        string dateLabel,
        string query,
        SearchNavigation navigation,
        Func<SearchNavigation, Task> onActivate)
    {
        Kind = kind;
        Title = title;
        Sub = sub;
        DateLabel = dateLabel;
        Query = query;
        _navigation = navigation;
        _onActivate = onActivate;
    }

    public string Kind { get; }

    public string Title { get; }

    public string Sub { get; }

    public string DateLabel { get; }

    /// <summary>The search term, so the row can highlight it within the title/snippet.</summary>
    public string Query { get; }

    [RelayCommand]
    private Task Activate() => _onActivate(_navigation);
}
