using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;

namespace Daynote.App.Notes;

public sealed partial class NoteWorkspaceViewModel
{
    /// <summary>
    /// Adds a note after a safe flush. On an empty day this creates exactly ONE real "Note 1"
    /// (the virtual projection becomes the single note the user asked for, never Note 1 + Note 2);
    /// on a day that already has notes it appends the next note.
    /// </summary>
    [RelayCommand]
    public async Task<bool> AddNoteAsync(CancellationToken cancellationToken = default)
    {
        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        DayWorkspace workspace = await _dependencies.CreateNote
            .ExecuteAsync(SelectedDate, cancellationToken).ConfigureAwait(true);
        NoteId? added = workspace.Notes.Notes[^1].Id;
        RebuildTabs(workspace, added);
        return true;
    }

    /// <summary>
    /// Deletes the given note after a safe flush; contiguous orders are restored by the repository.
    /// The selection moves to the note above the deleted one — the row the eye is already resting on
    /// — or to the one below when the first note goes. It used to fall to the top of the list, which
    /// meant deleting the fifth note jumped the editor to the first.
    /// </summary>
    [RelayCommand]
    public async Task<bool> DeleteNoteAsync(NoteTabViewModel? tab, CancellationToken cancellationToken = default)
    {
        if (tab is null || tab.IsProjection)
        {
            return false;
        }

        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        int index = Tabs.IndexOf(tab);
        NoteTabViewModel? neighbour = index > 0 ? Tabs[index - 1] : index + 1 < Tabs.Count ? Tabs[index + 1] : null;

        DayWorkspace workspace = await _dependencies.DeleteNote
            .ExecuteAsync(SelectedDate, tab.Id, cancellationToken).ConfigureAwait(true);
        RebuildTabs(workspace, neighbour?.Id);
        return true;
    }

    /// <summary>
    /// Copies a note — title, body and tags — into a new note placed directly after it, and selects
    /// the copy. The title carries a suffix so the two rows can be told apart until one is renamed.
    /// </summary>
    [RelayCommand]
    public async Task<bool> DuplicateNoteAsync(NoteTabViewModel? tab, CancellationToken cancellationToken = default)
    {
        if (tab is null || tab.IsProjection)
        {
            return false;
        }

        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        int index = Tabs.IndexOf(tab);
        DayWorkspace created = await _dependencies.CreateNote
            .ExecuteAsync(SelectedDate, cancellationToken).ConfigureAwait(true);
        NoteId copyId = created.Notes.Notes[^1].Id!.Value;

        // The repository appends an empty note; give it the source's content. Always a custom title:
        // a copied "노트 3" is not the third note, so it must not be renumbered as one.
        await _dependencies.Repository.SaveNoteAsync(
            new NoteSaveRequest(
                copyId,
                SelectedDate,
                tab.Title + AppStrings.NoteDuplicateSuffix,
                tab.Body,
                created.RevisionOf(copyId),
                IsNew: false,
                HasCustomTitle: true),
            cancellationToken).ConfigureAwait(true);

        DayWorkspace workspace = created;
        if (tab.Tags.Count > 0 && _dependencies.SetTags is { } setTags)
        {
            workspace = await setTags.ExecuteAsync(SelectedDate, copyId, tab.Tags.ToList(), cancellationToken).ConfigureAwait(true);
        }

        // Appended at the end by the repository; a copy belongs next to its original.
        List<NoteId> order = workspace.Notes.Notes.Where(static n => !n.IsProjection).Select(static n => n.Id!.Value).ToList();
        order.Remove(copyId);
        order.Insert(Math.Min(index + 1, order.Count), copyId);
        workspace = await _dependencies.ReorderNotes
            .ExecuteAsync(SelectedDate, order, cancellationToken).ConfigureAwait(true);

        RebuildTabs(workspace, copyId);
        return true;
    }

    public async Task<bool> ReorderAsync(
        IReadOnlyList<NoteId> orderedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        DayWorkspace workspace = await _dependencies.ReorderNotes
            .ExecuteAsync(SelectedDate, orderedIds, cancellationToken).ConfigureAwait(true);
        RebuildTabs(workspace, SelectedTab?.Id);
        return true;
    }

    [RelayCommand]
    private Task MoveUp(NoteTabViewModel? tab) => tab is null ? Task.CompletedTask : MoveNoteAsync(tab, -1);

    [RelayCommand]
    private Task MoveDown(NoteTabViewModel? tab) => tab is null ? Task.CompletedTask : MoveNoteAsync(tab, 1);

    /// <summary>Moves a note by one position and persists the new contiguous order.</summary>
    public Task<bool> MoveNoteAsync(NoteTabViewModel tab, int delta, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tab);
        int index = Tabs.IndexOf(tab);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Tabs.Count || _projectionOnly)
        {
            return Task.FromResult(false);
        }

        List<NoteId> order = Tabs.Select(static t => t.Id).ToList();
        (order[index], order[target]) = (order[target], order[index]);
        return ReorderAsync(order, cancellationToken);
    }

    /// <summary>
    /// Selects the note with the given stable id in the current date's set (search deep link). The
    /// current persisted state is re-queried so the link survives reorder and app restart; it returns
    /// false when no such persisted note exists, so a stale result never misnavigates.
    /// </summary>
    public async Task<bool> SelectNoteByIdAsync(NoteId id, CancellationToken cancellationToken = default)
    {
        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        DayWorkspace workspace = await _dependencies.GetDayWorkspace
            .ExecuteAsync(SelectedDate, cancellationToken).ConfigureAwait(true);
        bool exists = workspace.Notes.Notes.Any(note => !note.IsProjection && note.Id is { } noteId && noteId == id);
        if (!exists)
        {
            return false;
        }

        RebuildTabs(workspace, id);
        return SelectedTab is { } selected && selected.Id == id;
    }

    /// <summary>Renames a note (materializing a projection) and persists the custom title.</summary>
    public async Task<bool> RenameAsync(
        NoteTabViewModel? tab,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (tab is null)
        {
            return false;
        }

        string trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        tab.Title = trimmed;
        tab.HasCustomTitle = true;
        return await MaterializeAsync(tab, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Writes the tab to the database and reloads the day, turning a projection into a real note.
    /// </summary>
    /// <remarks>
    /// A day starts with one projected tab — a note the user can type into that does not exist yet.
    /// Anything that has to attach itself to a real row (a custom title, a tag) has to bring the note
    /// into being first, which is what this does: mark the pending text dirty, flush it, and rebuild
    /// the tabs from the day that now has the note in it.
    /// </remarks>
    private async Task<bool> MaterializeAsync(NoteTabViewModel tab, CancellationToken cancellationToken)
    {
        _autosave.MarkDirty(BuildRequest(tab));
        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        DayWorkspace workspace = await _dependencies.GetDayWorkspace
            .ExecuteAsync(SelectedDate, cancellationToken).ConfigureAwait(true);
        RebuildTabs(workspace, tab.Id);
        return true;
    }
}
