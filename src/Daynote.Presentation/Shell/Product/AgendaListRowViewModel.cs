using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Agenda;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One list in the sidebar, or one chip on the phone (design §04 4e, phone §03).
/// </summary>
/// <remarks>
/// A list is a filter, not a destination — §04 and the phone's §03 agree, which is why the phone
/// draws chips rather than a second level of navigation. Selecting one narrows the cross-date
/// 할 일 view; it does not take the user anywhere.
/// <para>
/// Renaming happens in place, the way a note's title does, rather than in a dialog. There is no
/// general-purpose prompt in this app and a list's name is one short field; a box that appears
/// where the name already is says what is being changed without having to name it again.
/// </para>
/// </remarks>
public sealed partial class AgendaListRowViewModel : ObservableObject
{
    private readonly Func<Guid?, Task> onSelect;
    private readonly Func<Guid, string, Task> onRename;
    private readonly Func<AgendaListRowViewModel, Task> onDelete;

    public AgendaListRowViewModel(
        AgendaListRow row,
        bool isSelected,
        Func<Guid?, Task> onSelect,
        Func<Guid, string, Task> onRename,
        Func<AgendaListRowViewModel, Task> onDelete)
    {
        this.onSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
        this.onRename = onRename ?? throw new ArgumentNullException(nameof(onRename));
        this.onDelete = onDelete ?? throw new ArgumentNullException(nameof(onDelete));

        Id = row.List.Id;
        Name = row.List.HasBuiltInName ? AppStrings.AgendaListDefaultName : row.List.Name;
        CountText = row.Count.ToString(CultureInfo.CurrentCulture);
        IsSelected = isSelected;
        CanDelete = !row.List.IsDefault;
        DraftName = Name;
    }

    public Guid Id { get; }

    /// <summary>
    /// What to call it. The built-in list stores no name so that it can be read in whichever
    /// language the app is in; a rename writes a real one and it stops being translated, which is
    /// what a rename should mean.
    /// </summary>
    public string Name { get; }

    public string CountText { get; }

    public bool IsSelected { get; }

    /// <summary>
    /// False for the built-in list. §04 is specific: its menu has no delete item at all, rather
    /// than a greyed one — there is nowhere for its contents to go, so the question is not put.
    /// </summary>
    public bool CanDelete { get; }

    [ObservableProperty]
    public partial bool IsRenaming { get; private set; }

    [ObservableProperty]
    public partial string DraftName { get; set; }

    [RelayCommand]
    private Task Select() => onSelect(Id);

    [RelayCommand]
    private void Rename()
    {
        DraftName = Name;
        IsRenaming = true;
    }

    /// <summary>
    /// Keeps the typed name, unless it is blank or unchanged.
    /// </summary>
    /// <remarks>
    /// Blank is a cancel rather than an error: the built-in list reads its name from the
    /// catalogue when it has none, and giving any list an empty label would leave a row nobody
    /// can identify.
    /// </remarks>
    [RelayCommand]
    private async Task CommitRename()
    {
        IsRenaming = false;
        string name = DraftName.Trim();
        if (name.Length == 0 || string.Equals(name, Name, StringComparison.Ordinal))
        {
            DraftName = Name;
            return;
        }

        await onRename(Id, name).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelRename()
    {
        DraftName = Name;
        IsRenaming = false;
    }

    /// <summary>
    /// Deletes the list. Not confirmed, and deliberately: its to-dos move to the built-in list
    /// rather than going with it (§3), so nothing is lost and a dialog would be asking the user
    /// to approve a reshuffle.
    /// </summary>
    [RelayCommand]
    private Task Delete() => onDelete(this);
}
