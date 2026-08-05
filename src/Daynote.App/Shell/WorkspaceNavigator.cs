using Daynote.App.Notes;
using Daynote.App.Search;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Search;

namespace Daynote.App.Shell;

public enum SearchActivationStatus
{
    /// <summary>The exact source was selected and revealed.</summary>
    Navigated,

    /// <summary>The source no longer exists (deleted / moved); no navigation occurred.</summary>
    Stale,
}

public readonly record struct SearchActivationOutcome(SearchActivationStatus Status)
{
    public bool Navigated => Status == SearchActivationStatus.Navigated;

    public static SearchActivationOutcome Success { get; } = new(SearchActivationStatus.Navigated);

    public static SearchActivationOutcome Stale { get; } = new(SearchActivationStatus.Stale);
}

/// <summary>Deep-link activation seam so the search view model can be tested without the shell.</summary>
public interface ISearchActivation
{
    Task<SearchActivationOutcome> ActivateAsync(
        SearchResultViewModel result,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates search-result activation: select the exact local date first, then select the exact
/// stable note (by <see cref="NoteId"/>), opening the notes view so the item is visible. Deep links
/// resolve by stable id against current state, so they survive note reorder and app restart. A
/// stale/deleted source never crashes and never navigates to the wrong item; it reports
/// <see cref="SearchActivationStatus.Stale"/> (plan Todo 9).
/// </summary>
public sealed class WorkspaceNavigator : ISearchActivation
{
    private readonly MainWindowViewModel _host;
    private readonly NoteWorkspaceViewModel _notes;

    public WorkspaceNavigator(
        MainWindowViewModel host,
        NoteWorkspaceViewModel notes)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _notes = notes ?? throw new ArgumentNullException(nameof(notes));
    }

    public async Task<SearchActivationOutcome> ActivateAsync(
        SearchResultViewModel result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        bool dateSelected = await _host.SelectDateAsync(result.LocalDate, cancellationToken).ConfigureAwait(true);
        if (!dateSelected)
        {
            // A save failure canceled the transition; the source is not stale, but we cannot navigate.
            return SearchActivationOutcome.Stale;
        }

        // Only notes remain navigable; any legacy non-note result resolves as stale.
        return result.SourceType == SearchSourceType.Note
            ? await ActivateNoteAsync(result.SourceId, cancellationToken).ConfigureAwait(true)
            : SearchActivationOutcome.Stale;
    }

    private async Task<SearchActivationOutcome> ActivateNoteAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        DomainResult<NoteId> id = NoteId.Create(sourceId);
        if (!id.IsSuccess)
        {
            return SearchActivationOutcome.Stale;
        }

        bool selected = await _notes.SelectNoteByIdAsync(id.Value, cancellationToken).ConfigureAwait(true);
        if (!selected)
        {
            return SearchActivationOutcome.Stale;
        }

        _host.RevealNotes();
        return SearchActivationOutcome.Success;
    }
}
