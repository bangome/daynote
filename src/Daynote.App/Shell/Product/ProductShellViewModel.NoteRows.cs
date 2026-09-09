using CommunityToolkit.Mvvm.Input;
using Daynote.App.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Managing notes from their sidebar rows: the context menu (rename, duplicate, delete) and the
/// Delete key act on the row under the pointer, not on the open note.
/// </summary>
public sealed partial class ProductShellViewModel
{
    /// <summary>
    /// Deletes a specific row — the context menu's and the Delete key's version of
    /// <see cref="DeleteSelectedNote"/>, which act on the row under the pointer rather than the open note.
    /// </summary>
    [RelayCommand]
    private async Task DeleteDayNote(NoteTabViewModel? tab)
    {
        if (tab is not null && await Notes.DeleteNoteAsync(tab).ConfigureAwait(true))
        {
            await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DuplicateDayNote(NoteTabViewModel? tab)
    {
        IsTimelineMode = false;
        if (await Notes.DuplicateNoteAsync(tab).ConfigureAwait(true))
        {
            await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Raised when a row asks to be renamed; the window puts the caret in the title box.</summary>
    public event EventHandler? TitleRenameRequested;

    [RelayCommand]
    private async Task RenameDayNote(NoteTabViewModel? tab)
    {
        IsTimelineMode = false;
        if (tab is not null && await Notes.SelectNoteAsync(tab).ConfigureAwait(true))
        {
            TitleRenameRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Everything that counts or lists notes, after one was added, copied or removed.</summary>
    private async Task RefreshAfterStructureChangeAsync()
    {
        RefreshHeader();
        await Calendar.LoadAsync().ConfigureAwait(true);
        await Todo.RefreshAsync().ConfigureAwait(true);
        await Favorites.RefreshAsync().ConfigureAwait(true);
        await TagPanel.RefreshAsync().ConfigureAwait(true);
    }
}
