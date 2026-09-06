using CommunityToolkit.Mvvm.Input;
using Daynote.Core.Notes;

namespace Daynote.App.Notes;

/// <summary>
/// Per-note metadata operations for the redesign: favorite toggle and tag replace-set. Both flush the
/// autosave pipeline first (so a dirty body is never lost), invoke the existing use case, then rebuild the
/// tabs from the returned workspace preserving the current selection. Projection (unsaved) notes are
/// skipped — there is no persisted identity to attach favorites or tags to yet.
/// </summary>
public sealed partial class NoteWorkspaceViewModel
{
    [RelayCommand]
    public Task<bool> ToggleFavoriteAsync(NoteTabViewModel? tab) => ToggleFavoriteAsync(tab, CancellationToken.None);

    public async Task<bool> ToggleFavoriteAsync(NoteTabViewModel? tab, CancellationToken cancellationToken)
    {
        if (tab is null || tab.IsProjection || _dependencies.ToggleFavorite is not { } toggle)
        {
            return false;
        }

        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        DayWorkspace workspace = await toggle.ExecuteAsync(SelectedDate, tab.Id, cancellationToken).ConfigureAwait(true);
        RebuildTabs(workspace, tab.Id);
        return true;
    }

    public async Task<bool> AddTagAsync(NoteTabViewModel? tab, string tag, CancellationToken cancellationToken = default)
    {
        if (tab is null || tab.IsProjection)
        {
            return false;
        }

        string trimmed = (tag ?? string.Empty).Trim().TrimStart('#').Trim();
        if (trimmed.Length == 0 || tab.Tags.Contains(trimmed))
        {
            return false;
        }

        var next = new List<string>(tab.Tags) { trimmed };
        return await ReplaceTagsAsync(tab, next, cancellationToken).ConfigureAwait(true);
    }

    public async Task<bool> RemoveTagAsync(NoteTabViewModel? tab, string tag, CancellationToken cancellationToken = default)
    {
        if (tab is null || tab.IsProjection || !tab.Tags.Contains(tag))
        {
            return false;
        }

        var next = tab.Tags.Where(t => !string.Equals(t, tag, StringComparison.Ordinal)).ToList();
        return await ReplaceTagsAsync(tab, next, cancellationToken).ConfigureAwait(true);
    }

    private async Task<bool> ReplaceTagsAsync(
        NoteTabViewModel tab,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        if (_dependencies.SetTags is not { } setTags)
        {
            return false;
        }

        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        DayWorkspace workspace = await setTags.ExecuteAsync(SelectedDate, tab.Id, tags, cancellationToken).ConfigureAwait(true);
        RebuildTabs(workspace, tab.Id);
        return true;
    }
}
