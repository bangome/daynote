using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Cross-surface navigation: every "jump to this note" path funnels through <c>SelectDateAsync</c>
/// (autosave-safe flush) and then selects the note in the editor. Split from the main file for
/// reviewability; state and commands stay in ProductShellViewModel.cs.
/// </summary>
public sealed partial class ProductShellViewModel
{
    /// <summary>Leaves timeline mode and opens the picked note in the editor on its own date.</summary>
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

    /// <summary>Navigates to the tag occurrence's note and selects the '#tag' span in the editor.</summary>
    private async Task JumpToTagAsync(TagOccurrence occ)
    {
        if (await SelectDateAsync(occ.Date).ConfigureAwait(true))
        {
            DomainResult<NoteId> id = NoteId.Create(occ.NoteId);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);

                // +1 covers the leading '#' that Tag omits.
                EditorSelectRequested?.Invoke(occ.CharIndex, occ.Tag.Length + 1);
            }
        }
    }

    /// <summary>Opens a starred note from the 즐겨찾기 tab: navigate to its date and select it.</summary>
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
            ActiveTab = tab;
        }
    }
}
