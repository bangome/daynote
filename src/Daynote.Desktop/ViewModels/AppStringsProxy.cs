using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// Bindable view over the static <see cref="AppStrings"/> catalog. Avalonia's compiled bindings want an
/// instance path, and one <c>PropertyChanged(string.Empty)</c> here re-reads every label after a
/// language switch — the Avalonia counterpart of the WPF <c>{loc:Tr}</c> markup extension.
/// </summary>
public sealed class AppStringsProxy : ObservableObject
{
    /// <summary>One shared instance; it re-reads itself on every language switch.</summary>
    public static AppStringsProxy Instance { get; } = new();

    public AppStringsProxy()
    {
        LocalizationService.Instance.LanguageChanged += (_, _) => Refresh();
    }

    public string Today => AppStrings.Today;
    public string AddNote => AppStrings.AddNote;
    public string SearchHint => AppStrings.UnifiedSearchHint;
    public string ClearSearch => AppStrings.ClearSearch;
    public string ThemeToggle => AppStrings.ThemeToggle;
    public string TimelineToggle => AppStrings.TimelineToggle;
    public string TimelineEmpty => AppStrings.TimelineEmpty;
    public string DayNotesEmpty => AppStrings.DayNotesEmpty;
    public string EditorEmptyHint => AppStrings.EditorEmptyHint;
    public string Favorite => AppStrings.Favorite;
    public string TagAddPlaceholder => AppStrings.TagAddPlaceholder;
    public string TabTodoName => AppStrings.TabTodoName;
    public string TabFavoritesName => AppStrings.TabFavoritesName;
    public string TabTagsName => AppStrings.TabTagsName;
    public string TabFilesName => AppStrings.TabFilesName;
    public string TodoEmpty => AppStrings.TodoEmptyPrefix + AppStrings.TodoEmptyCode + AppStrings.TodoEmptySuffix;
    public string FavoritesPanelEmpty => AppStrings.FavoritesPanelEmpty;
    public string TagPanelEmpty => AppStrings.TagPanelEmpty;
    public string FileTabEmpty => AppStrings.FileTabEmpty;
    public string AddFile => AppStrings.AddFile;
    public string SearchNoResultsRow => AppStrings.SearchNoResultsRow;
    public string WeekdaySun => AppStrings.WeekdaySun;
    public string WeekdayMon => AppStrings.WeekdayMon;
    public string WeekdayTue => AppStrings.WeekdayTue;
    public string WeekdayWed => AppStrings.WeekdayWed;
    public string WeekdayThu => AppStrings.WeekdayThu;
    public string WeekdayFri => AppStrings.WeekdayFri;
    public string WeekdaySat => AppStrings.WeekdaySat;
    public string Settings => AppStrings.Settings;
    public string OpenStickyNote => AppStrings.OpenStickyNote;

    public string TutorialBack => AppStrings.TutorialBack;
    public string TutorialNext => AppStrings.TutorialNext;
    public string TutorialSkip => AppStrings.TutorialSkip;
    public string TutorialFinish => AppStrings.TutorialFinish;
    public string SettingsStartupLabel => AppStrings.SettingsStartupLabel;

    /// <summary>Any catalog key by name, for views with many labels: <c>{Binding Strings[AccountSignInTitle]}</c>.</summary>
    public string this[string key] => LocalizationService.Instance[key];

    public void Refresh() => OnPropertyChanged(string.Empty);
}
