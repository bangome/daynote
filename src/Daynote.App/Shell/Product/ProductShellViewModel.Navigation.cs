using Daynote.App.Notes;
using Daynote.App.Composition;
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
        // SelectDateAsync leaves the timeline for every caller, including this one.
        if (await SelectDateAsync(date).ConfigureAwait(true))
        {
            DomainResult<NoteId> nid = NoteId.Create(id);
            if (nid.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(nid.Value).ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// Goes to the day a to-do falls on, and opens the note it was captured from if there is one.
    /// </summary>
    /// <remarks>
    /// There often is not: a to-do made in the list view has no note behind it, and one whose
    /// note was deleted keeps a <c>source_note_id</c> that no longer resolves — §3 allows it to
    /// dangle precisely so deleting a note cannot take tasks with it. Either way the day is the
    /// useful half of the jump.
    /// </remarks>
    private async Task JumpToTodoAsync(Daynote.Core.Agenda.AgendaDayRow row)
    {
        DateOnly day = DateOnly.FromDateTime(
            row.At?.Value ?? row.Item.Anchor?.Value ?? DateTime.Today);
        if (!await SelectDateAsync(LocalDates.FromDateOnly(day)).ConfigureAwait(true))
        {
            return;
        }

        if (row.Item.SourceNoteId is { } note && NoteId.Create(note) is { IsSuccess: true } id)
        {
            await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
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
                // No editor selection: a tag is on the note, not at a position in its prose.
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
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
