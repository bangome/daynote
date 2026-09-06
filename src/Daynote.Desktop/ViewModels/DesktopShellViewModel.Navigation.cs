using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// Cross-surface navigation: every "jump to this note" path funnels through
/// <see cref="DesktopShellViewModel.SelectDateAsync"/> (autosave-safe flush) and then selects the note.
/// Copied from the WPF shell's navigation partial so both apps land on the same note the same way.
/// </summary>
public sealed partial class DesktopShellViewModel
{
    private async Task OpenFromTimelineAsync(Guid id, LocalDate date)
    {
        IsTimelineMode = false;
        if (await SelectDateAsync(date).ConfigureAwait(true))
        {
            DomainResult<NoteId> nid = NoteId.Create(id);
            if (nid.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(nid.Value).ConfigureAwait(true);
            }
        }
    }

    private async Task JumpToTodoAsync(TodoLine line)
    {
        if (await SelectDateAsync(line.Date).ConfigureAwait(true))
        {
            DomainResult<NoteId> id = NoteId.Create(line.NoteId);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
            }
        }
    }

    private async Task JumpToTagAsync(TagOccurrence occ)
    {
        if (await SelectDateAsync(occ.Date).ConfigureAwait(true))
        {
            DomainResult<NoteId> id = NoteId.Create(occ.NoteId);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
                EditorSelectRequested?.Invoke(occ.CharIndex, occ.Tag.Length + 1);
            }
        }
    }

    private async Task OpenFavoriteAsync(NoteSummary note)
    {
        if (await SelectDateAsync(note.LocalDate).ConfigureAwait(true))
        {
            DomainResult<NoteId> id = NoteId.Create(note.Id);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
            }
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
            DomainResult<NoteId> id = NoteId.Create(noteId);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
            }
        }

        if (navigation.Tab is { } tab)
        {
            RightCollapsed = false;
            ActiveTab = tab;
        }
    }

    /// <summary>Toggles a checkbox line in the note that owns it and reloads the editor if it is on screen.</summary>
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
