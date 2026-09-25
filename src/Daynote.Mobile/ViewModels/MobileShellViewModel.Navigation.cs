using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// Cross-surface navigation: every "jump to this note" path funnels through
/// <see cref="MobileShellViewModel.SelectDateAsync"/> (autosave-safe flush), selects the note, and
/// then — unlike the desktop, where the editor is always on screen — opens the editor over the day.
/// </summary>
public sealed partial class MobileShellViewModel
{
    private async Task JumpToTodoAsync(TodoLine line)
    {
        if (await SelectDateAsync(line.Date).ConfigureAwait(true))
        {
            await OpenByIdAsync(line.NoteId).ConfigureAwait(true);
        }
    }

    private async Task JumpToTagAsync(TagOccurrence occ)
    {
        if (await SelectDateAsync(occ.Date).ConfigureAwait(true))
        {
            await OpenByIdAsync(occ.NoteId).ConfigureAwait(true);
        }
    }

    private async Task OpenFavoriteAsync(NoteSummary note)
    {
        if (await SelectDateAsync(note.LocalDate).ConfigureAwait(true))
        {
            await OpenByIdAsync(note.Id).ConfigureAwait(true);
        }
    }

    private async Task NavigateAsync(SearchNavigation navigation)
    {
        Search.Query = string.Empty;
        if (!await SelectDateAsync(navigation.Date).ConfigureAwait(true))
        {
            return;
        }

        if (navigation.NoteId is { } noteId)
        {
            await OpenByIdAsync(noteId).ConfigureAwait(true);
            return;
        }

        // A day hit with no note: show the day itself rather than an editor with nothing in it.
        Page = MobilePage.Day;
        IsEditorOpen = false;
    }

    /// <summary>Selects a note by raw id and brings the editor up on it.</summary>
    private async Task OpenByIdAsync(Guid rawId)
    {
        DomainResult<NoteId> id = NoteId.Create(rawId);
        if (id.IsSuccess && await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true))
        {
            Page = MobilePage.Day;
            IsEditorOpen = true;
        }
    }

    /// <summary>Toggles a checkbox line in the note that owns it and reloads the editor if it is open on it.</summary>
    private async Task ToggleTodoAsync(TodoLine line)
    {
        DomainResult<NoteId> id = NoteId.Create(line.NoteId);
        if (!id.IsSuccess)
        {
            return;
        }

        DayWorkspace workspace = await _repository.GetDayWorkspaceStateAsync(line.Date).ConfigureAwait(true);
        Note? note = workspace.Notes.Notes.FirstOrDefault(n => !n.IsProjection && n.Id == id.Value);
        if (note is null)
        {
            return;
        }

        string newBody = TodoParsing.ToggleLine(note.Body, line.LineIndex);
        if (string.Equals(newBody, note.Body, StringComparison.Ordinal))
        {
            return;
        }

        var request = new NoteSaveRequest(
            id.Value, line.Date, note.Title, newBody, workspace.RevisionOf(id.Value), IsNew: false, note.HasCustomTitle);
        try
        {
            await _repository.SaveNoteAsync(request).ConfigureAwait(true);
        }
        catch (RecoverableNoteException)
        {
            return;
        }

        if (line.Date == SelectedDate)
        {
            await Notes.LoadAsync(SelectedDate).ConfigureAwait(true);
        }

        await Todo.RefreshAsync().ConfigureAwait(true);
        await TagPanel.RefreshAsync().ConfigureAwait(true);
    }
}
