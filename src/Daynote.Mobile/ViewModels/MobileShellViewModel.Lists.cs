using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The Lists tab's chip row: the lists as a filter over the cross-date to-dos, and the sheet that
/// makes, renames and deletes one (phone §03).
/// </summary>
/// <remarks>
/// Chips rather than a second level of navigation, because a list is a filter and not a
/// destination — the desktop says the same thing with rows in its sidebar. A chip's number is the
/// desktop's number: both read <see cref="TodoPanelViewModel.Lists"/>, which counts what is owed
/// rather than what is stored.
/// <para>
/// The leading 전체 chip is not a list. It is how the filter comes off on a touch screen, and its
/// count is the panel's own total, so "전체 17" here and "할 일 17" on the desktop cannot drift.
/// </para>
/// </remarks>
public sealed partial class MobileShellViewModel
{
    /// <summary>The list the sheet is about, or null while it is making a new one.</summary>
    private Guid? _sheetListId;

    /// <summary>Whether the 전체 chip is the one lit: no filter, everything owed.</summary>
    public bool IsAllAgendaListsSelected => Todo.SelectedListId is null;

    /// <summary>The list sheet, in either of its two faces.</summary>
    [ObservableProperty]
    private bool _isAgendaListSheetOpen;

    /// <summary>
    /// The naming face. A phone cannot rename in place inside a chip the way the sidebar renames a
    /// row, so both making and renaming come down to the same one field.
    /// </summary>
    [ObservableProperty]
    private bool _isNamingAgendaList;

    [ObservableProperty]
    private string _agendaListDraftName = string.Empty;

    /// <summary>The sheet's heading: the list's name on the menu, 새 리스트 or 이름 변경 on the field.</summary>
    [ObservableProperty]
    private string _agendaListSheetTitle = string.Empty;

    /// <summary>False for the built-in list, whose menu has no delete item at all (§04).</summary>
    [ObservableProperty]
    private bool _canDeleteSheetList;

    /// <summary>
    /// Shown under the built-in list's name, because it is the one list the user did not make and
    /// its contents are not obvious: it is where an undated, unfiled to-do lands.
    /// </summary>
    public bool IsSheetListDefault => IsAgendaListSheetOpen && !IsNamingAgendaList && !CanDeleteSheetList;

    partial void OnIsAgendaListSheetOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDock));
        OnPropertyChanged(nameof(IsSheetListDefault));
    }

    partial void OnIsNamingAgendaListChanged(bool value) => OnPropertyChanged(nameof(IsSheetListDefault));

    partial void OnCanDeleteSheetListChanged(bool value) => OnPropertyChanged(nameof(IsSheetListDefault));

    /// <summary>
    /// Narrows the to-dos to one list, or widens them again. Null is the 전체 chip; tapping the
    /// chip already lit also clears, which is how the filter comes off everywhere else.
    /// </summary>
    [RelayCommand]
    private Task SelectAgendaList(Guid? id)
    {
        Todo.SelectedListId = id is null || id == Todo.SelectedListId ? null : id;
        return RefreshTodosAsync();
    }

    /// <summary>A long press on a chip: rename, and delete unless it is the built-in list.</summary>
    public void OpenAgendaListMenu(AgendaListRowViewModel list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _sheetListId = list.Id;
        AgendaListSheetTitle = list.Name;
        AgendaListDraftName = list.Name;
        CanDeleteSheetList = list.CanDelete;
        IsNamingAgendaList = false;
        IsAgendaListSheetOpen = true;
    }

    /// <summary>The chip at the end of the row.</summary>
    [RelayCommand]
    private void NewAgendaList()
    {
        _sheetListId = null;
        AgendaListSheetTitle = AppStrings.AgendaListNewName;
        AgendaListDraftName = string.Empty;
        CanDeleteSheetList = false;
        IsNamingAgendaList = true;
        IsAgendaListSheetOpen = true;
    }

    [RelayCommand]
    private void RenameAgendaListFromSheet()
    {
        AgendaListSheetTitle = AppStrings.AgendaListRename;
        IsNamingAgendaList = true;
    }

    [RelayCommand]
    private void CloseAgendaListSheet() => IsAgendaListSheetOpen = false;

    /// <summary>
    /// Keeps the typed name. Blank is a cancel rather than an error: a list with no label is a row
    /// nobody can identify, and the built-in one reads its name from the catalogue when it has none.
    /// </summary>
    [RelayCommand]
    private async Task CommitAgendaListName()
    {
        string name = AgendaListDraftName.Trim();
        IsAgendaListSheetOpen = false;
        if (name.Length == 0)
        {
            return;
        }

        if (_sheetListId is { } id)
        {
            await Todo.RenameListAsync(id, name).ConfigureAwait(true);
        }
        else
        {
            // Selected as it is made, so the next thing filed lands where the user was looking.
            await Todo.CreateListAsync(name).ConfigureAwait(true);
        }

        await RefreshTodosAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Deletes the list. Not confirmed: its to-dos move to the built-in one rather than going with
    /// it (§3), so nothing is lost and a question would only be asking the user to approve a move.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAgendaListFromSheet()
    {
        IsAgendaListSheetOpen = false;
        if (_sheetListId is { } id)
        {
            await Todo.DeleteListAsync(id).ConfigureAwait(true);
            await RefreshTodosAsync().ConfigureAwait(true);
        }
    }
}
