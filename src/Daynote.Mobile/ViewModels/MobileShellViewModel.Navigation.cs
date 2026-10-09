using Daynote.Core.Agenda;
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
    /// <summary>
    /// Goes to the day a to-do falls on, and opens the note it was captured from if there is one.
    /// A to-do made in the list view has none, and §3 lets a captured one's note be deleted out
    /// from under it, so the day is the half of the jump that always works.
    /// </summary>
    private async Task JumpToTodoAsync(AgendaDayRow row)
    {
        DateOnly day = DateOnly.FromDateTime(row.At?.Value ?? row.Item.Anchor?.Value ?? DateTime.Today);
        Guid? note = row.Item.SourceNoteId;
        if (await SelectDateAsync(Daynote.App.Composition.LocalDates.FromDateOnly(day)).ConfigureAwait(true)
            && note is { } id)
        {
            await OpenByIdAsync(id).ConfigureAwait(true);
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
        RememberSearch();
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

    /// <summary>
    /// Selects a note by raw id and brings the editor up on it, over whichever tab it was opened
    /// from, so the back arrow returns to the list the note was found in.
    /// </summary>
    private async Task OpenByIdAsync(Guid rawId)
    {
        DomainResult<NoteId> id = NoteId.Create(rawId);
        if (id.IsSuccess && await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true))
        {
            IsEditorOpen = true;
        }
    }

    /// <summary>Toggles a checkbox line in the note that owns it and reloads the editor if it is open on it.</summary>
    /// <summary>
    /// Ticks a to-do. It used to rewrite the <c>-[]</c> line in the note body; on an occurrence of
    /// a rule this now writes an override rather than touching the rule (docs/TODOS.md §5).
    /// </summary>
    private async Task ToggleTodoAsync(AgendaDayRow row)
    {
        await _toggleAgenda.ToggleAsync(row).ConfigureAwait(true);
        await RefreshTodosAsync().ConfigureAwait(true);
    }

}
