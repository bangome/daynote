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
        IsListMode = false;
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
    /// Goes to the day a to-do falls on, and opens the note it came from when there is one.
    /// </summary>
    private async Task JumpToTodoAsync(Daynote.Core.Agenda.AgendaDayRow row)
    {
        DateOnly day = DateOnly.FromDateTime(row.Falls?.Value ?? DateTime.Today);
        Guid? note = row.Item.SourceNoteId;
        if (await SelectDateAsync(Daynote.App.Composition.LocalDates.FromDateOnly(day)).ConfigureAwait(true)
            && note is { } id
            && Daynote.Core.Domain.Notes.NoteId.Create(id) is { IsSuccess: true } parsed)
        {
            await Notes.SelectNoteByIdAsync(parsed.Value).ConfigureAwait(true);
        }
    }

    private async Task JumpToTagAsync(TagOccurrence occ)
    {
        IsListMode = false;
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

    private async Task OpenFavoriteAsync(NoteSummary note)
    {
        IsListMode = false;
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
        IsPaletteOpen = false;
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

        // A file result opens the day's files list, where the attachment is; anything else is a note
        // or a day, which the editor shows.
        if (navigation.Tab is { } tab)
        {
            ActiveTab = tab;
            IsTimelineMode = false;
            IsListMode = true;
        }
        else
        {
            IsListMode = false;
        }
    }

    /// <summary>Toggles a checkbox line in the note that owns it and reloads the editor if it is on screen.</summary>
    /// <summary>
    /// Ticks a to-do; an occurrence of a rule gets an override rather than the rule being
    /// changed (docs/TODOS.md §5).
    /// </summary>
    private async Task ToggleTodoAsync(Daynote.Core.Agenda.AgendaDayRow row)
    {
        await _toggleAgenda.ToggleAsync(row).ConfigureAwait(true);
        await Todo.RefreshAsync().ConfigureAwait(true);
    }

}
