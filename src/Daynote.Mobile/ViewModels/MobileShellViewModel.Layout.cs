using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The layout the window is in (<see cref="MobileLayout"/>), which changes what navigation means:
/// on a phone the editor covers the page and a tab change closes it; with the note beside the
/// panel it stays open while the panel changes.
/// </summary>
/// <remarks>
/// Set by the view, which is the only thing that knows the window's width. Nothing about the data
/// changes with it - the selected date, the open note, the draft and the @ being typed carry across
/// a fold or a Split View resize untouched (Foldables "접힘↔펼침 상태 유지").
/// </remarks>
public sealed partial class MobileShellViewModel
{
    [ObservableProperty]
    private MobileLayout _layout = MobileLayout.Phone;

    /// <summary>The tablet's sidebar, open over the day when it is folded away (840-1099).</summary>
    [ObservableProperty]
    private bool _isSidebarOpen;

    public bool IsPhoneLayout => Layout == MobileLayout.Phone;

    public bool IsTwoPaneLayout => Layout == MobileLayout.TwoPane;

    /// <summary>Either tablet layout: Desktop B's structure, with or without its sidebar showing.</summary>
    public bool IsTabletLayout => Layout is MobileLayout.Tablet or MobileLayout.TabletCompact;

    /// <summary>The sidebar as a column, beside the day rather than over it.</summary>
    public bool IsSidebarDocked => Layout == MobileLayout.Tablet;

    /// <summary>
    /// The note stands beside the panel whether or not one was opened: the two-pane layout's right
    /// half is always the selected note (F1-F4).
    /// </summary>
    public bool IsNoteBesidePanel => Layout == MobileLayout.TwoPane;

    /// <summary>The menu button that opens the folded-away sidebar: on the compact tablet, over the day only.</summary>
    public bool IsSidebarButtonShown => Layout == MobileLayout.TabletCompact && IsDayPage;

    /// <summary>The page column shows, which on a tablet means the editor is not standing in for it.</summary>
    public bool ShowPageColumn => !IsTabletLayout || !IsEditorOpen;

    /// <summary>The editor shows: over the page on a phone, beside it in two panes, in its place on a tablet.</summary>
    public bool ShowEditor => IsEditorOpen || IsNoteBesidePanel;

    partial void OnLayoutChanged(MobileLayout value)
    {
        if (value != MobileLayout.TabletCompact)
        {
            IsSidebarOpen = false;
        }

        OnPropertyChanged(nameof(IsPhoneLayout));
        OnPropertyChanged(nameof(IsTwoPaneLayout));
        OnPropertyChanged(nameof(IsTabletLayout));
        OnPropertyChanged(nameof(IsSidebarDocked));
        OnPropertyChanged(nameof(IsSidebarButtonShown));
        OnPropertyChanged(nameof(IsNoteBesidePanel));
        OnPropertyChanged(nameof(ShowPageColumn));
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(ShowDock));
    }

    /// <summary>The sidebar's 즐겨찾기 row is the view on screen.</summary>
    public bool IsSidebarFavorites => IsListsPage && IsFavoritesList;

    /// <summary>The sidebar's 태그 row is the view on screen.</summary>
    public bool IsSidebarTags => IsListsPage && IsTagsList;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    /// <summary>A list row in the sidebar: the to-dos, narrowed to that list.</summary>
    [RelayCommand]
    private Task ShowAgendaList(Guid id)
    {
        ShowList(Daynote.App.Shell.Product.RightTab.Todo);
        return id == Todo.SelectedListId ? Task.CompletedTask : SelectAgendaList(id);
    }

    /// <summary>
    /// A sidebar row: the page it names, with the list it names chosen there (즐겨찾기, 태그, a
    /// to-do list), and the overlay closed behind it.
    /// </summary>
    [RelayCommand]
    private void ShowList(Daynote.App.Shell.Product.RightTab tab)
    {
        GoToPage(MobilePage.Lists);
        ActiveList = tab;
        IsSidebarOpen = false;
    }
}
