using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The header's second line in Desktop B v2: in the editor, the day's note tabs and the "1 / 8 ▾"
/// menu of every note of the day; in any other view, the view's name and a word about it.
/// </summary>
public sealed partial class DesktopShellViewModel
{
    /// <summary>The menu under "1 / 8 ▾" is open.</summary>
    [ObservableProperty]
    private bool _isTabMenuOpen;

    /// <summary>"3 / 8": where the open note is among the day's, or just how many when none is open.</summary>
    public string TabPositionText
    {
        get
        {
            NoteTabViewModel[] tabs = [.. Notes.Tabs.Where(static tab => !tab.IsProjection)];
            int index = Array.IndexOf(tabs, Notes.SelectedTab);
            return index >= 0
                ? string.Create(CultureInfo.CurrentCulture, $"{index + 1} / {tabs.Length}")
                : tabs.Length.ToString(CultureInfo.CurrentCulture);
        }
    }

    /// <summary>The menu's heading: the day, "10월 13일 (화)".</summary>
    public string TabMenuHeading =>
        LocalDates.ToDateOnly(SelectedDate).ToString(AppStrings.DeskTabMenuDateFormat, LocalizationService.Instance.Culture);

    public string TabMenuCountText => NoteCountText;

    /// <summary>The 할 일 view is on screen, where the header carries its + for the full card.</summary>
    public bool IsNavTodoView => IsListMode && TabIsTodo;

    /// <summary>Outside the editor, the header names the view: 타임라인, or the list being looked at.</summary>
    public string ViewTitle => IsTimelineMode ? AppStrings.DeskNavTimeline : ListTitle;

    public string ViewSubtitle => IsTimelineMode ? AppStrings.DeskTimelineViewSub : ListSubtitle;

    [RelayCommand]
    private void ToggleTabMenu() => IsTabMenuOpen = !IsTabMenuOpen;

    [RelayCommand]
    private void CloseTabMenu() => IsTabMenuOpen = false;

    /// <summary>A note picked from the menu: it opens, and the menu goes.</summary>
    [RelayCommand]
    private async Task PickTabMenuNote(NoteTabViewModel? tab)
    {
        IsTabMenuOpen = false;
        await SelectDayNoteCommand.ExecuteAsync(tab).ConfigureAwait(true);
    }

    private void RaiseHeaderLine()
    {
        OnPropertyChanged(nameof(TabPositionText));
        OnPropertyChanged(nameof(TabMenuHeading));
        OnPropertyChanged(nameof(TabMenuCountText));
        OnPropertyChanged(nameof(IsNavTodoView));
        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(ViewSubtitle));
    }
}
