using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Deletes the given note after a safe flush; contiguous orders are restored by the repository.</summary>
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

        DayWorkspace workspace = await _dependencies.DeleteNote
            .ExecuteAsync(SelectedDate, tab.Id, cancellationToken).ConfigureAwait(true);
        RebuildTabs(workspace, selectId: null);
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
