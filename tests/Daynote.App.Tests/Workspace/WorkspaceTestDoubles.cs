using Daynote.App.Search;
using Daynote.App.Shell;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Time;

namespace Daynote.App.Tests.Workspace;

/// <summary>Records the applied theme without touching a live WPF application.</summary>
internal sealed class NoOpThemeApplier : IThemeApplier
{
    public bool? LastDark { get; private set; }

    public void Apply(bool dark) => LastDark = dark;
}

/// <summary>Returns a scripted set of file paths for the files-panel add flow.</summary>
internal sealed class FakeFilePicker : IFilePicker
{
    public List<string> Paths { get; } = [];

    public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Paths);
}

/// <summary>Deterministic clock; the instant and offset are settable for date-scoped fixtures.</summary>
internal sealed class MutableClock : IClock
{
    public MutableClock(DateTimeOffset utcInstant, TimeSpan offset)
    {
        UtcInstant = utcInstant;
        Offset = offset;
    }

    public DateTimeOffset UtcInstant { get; set; }

    public TimeSpan Offset { get; set; }

    public ClockSnapshot Read() => new(UtcInstant, Offset);
}

/// <summary>Never completes a scheduled autosave, so tests drive persistence through explicit flush.</summary>
internal sealed class InfiniteScheduler : IAutosaveScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken);
}

/// <summary>Runs the debounce delay immediately (respecting cancellation).</summary>
internal sealed class ImmediateSearchScheduler : ISearchScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
}

/// <summary>Gates every debounce delay until <see cref="ReleaseAll"/>; cancels honor the token.</summary>
internal sealed class ManualSearchScheduler : ISearchScheduler
{
    private readonly List<TaskCompletionSource> _gates = [];

    public int DelayCount { get; private set; }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gates)
        {
            _gates.Add(gate);
            DelayCount++;
        }

        cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
        return gate.Task;
    }

    public void ReleaseAll()
    {
        TaskCompletionSource[] gates;
        lock (_gates)
        {
            gates = [.. _gates];
            _gates.Clear();
        }

        foreach (TaskCompletionSource gate in gates)
        {
            gate.TrySetResult();
        }
    }
}

/// <summary>Counts how many searches actually reached the repository (debounce coalescing proof).</summary>
internal sealed class CountingSearchRepository(ISearchRepository inner) : ISearchRepository
{
    private int _count;

    public int SearchCount => Volatile.Read(ref _count);

    public ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query, int offset, int limit, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return inner.SearchAsync(query, offset, limit, cancellationToken);
    }
}

/// <summary>Holds each query's result until the test completes it, so ordering can be interleaved.</summary>
internal sealed class GatedSearchRepository : ISearchRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<SearchResult>>> _pending =
        new(StringComparer.Ordinal);

    public async ValueTask<IReadOnlyList<SearchResult>> SearchAsync(
        SearchQuery query, int offset, int limit, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<IReadOnlyList<SearchResult>> gate = GateFor(query.NormalizedText);
        using (cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken)))
        {
            return await gate.Task.ConfigureAwait(false);
        }
    }

    public void Complete(string query, params SearchResult[] results) =>
        GateFor(query).TrySetResult(results);

    private TaskCompletionSource<IReadOnlyList<SearchResult>> GateFor(string query)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(query, out TaskCompletionSource<IReadOnlyList<SearchResult>>? gate))
            {
                gate = new TaskCompletionSource<IReadOnlyList<SearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[query] = gate;
            }

            return gate;
        }
    }
}

/// <summary>Records deep-link activations and returns a configurable outcome.</summary>
internal sealed class RecordingSearchActivation : ISearchActivation
{
    public SearchActivationOutcome Outcome { get; set; } = SearchActivationOutcome.Success;

    public List<Guid> Activated { get; } = [];

    public Task<SearchActivationOutcome> ActivateAsync(
        SearchResultViewModel result, CancellationToken cancellationToken = default)
    {
        Activated.Add(result.SourceId);
        return Task.FromResult(Outcome);
    }
}

/// <summary>Wraps a real note repository and injects recoverable save failures on demand.</summary>
internal sealed class FailingNoteRepository(INoteRepository inner) : INoteRepository
{
    public bool FailSaves { get; set; }

    public ValueTask<NoteSet> GetDayWorkspaceAsync(LocalDate localDate, CancellationToken cancellationToken = default) =>
        inner.GetDayWorkspaceAsync(localDate, cancellationToken);

    public ValueTask<DayWorkspace> GetDayWorkspaceStateAsync(LocalDate localDate, CancellationToken cancellationToken = default) =>
        inner.GetDayWorkspaceStateAsync(localDate, cancellationToken);

    public ValueTask<DayWorkspace> CreateNoteAsync(LocalDate localDate, NoteId projectionId, NoteId newNoteId, CancellationToken cancellationToken = default) =>
        inner.CreateNoteAsync(localDate, projectionId, newNoteId, cancellationToken);

    public ValueTask<DayWorkspace> ReorderNotesAsync(LocalDate localDate, IReadOnlyList<NoteId> orderedIds, CancellationToken cancellationToken = default) =>
        inner.ReorderNotesAsync(localDate, orderedIds, cancellationToken);

    public ValueTask<DayWorkspace> DeleteNoteAsync(LocalDate localDate, NoteId noteId, CancellationToken cancellationToken = default) =>
        inner.DeleteNoteAsync(localDate, noteId, cancellationToken);

    public ValueTask<DayWorkspace> ToggleFavoriteAsync(LocalDate localDate, NoteId noteId, CancellationToken cancellationToken = default) =>
        inner.ToggleFavoriteAsync(localDate, noteId, cancellationToken);

    public ValueTask<DayWorkspace> SetTagsAsync(LocalDate localDate, NoteId noteId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) =>
        inner.SetTagsAsync(localDate, noteId, tags, cancellationToken);

    public ValueTask<IReadOnlyList<DateContentSummary>> GetMonthContentSummaryAsync(int year, int month, CancellationToken cancellationToken = default) =>
        inner.GetMonthContentSummaryAsync(year, month, cancellationToken);

    public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(CancellationToken cancellationToken = default) =>
        inner.GetAllNotesAsync(cancellationToken);

    public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(LocalDate from, LocalDate to, CancellationToken cancellationToken = default) =>
        inner.GetAllNotesAsync(from, to, cancellationToken);

    public ValueTask<NoteSaveReceipt> SaveNoteAsync(NoteSaveRequest request, CancellationToken cancellationToken = default) =>
        FailSaves
            ? throw new RecoverableNoteException(NoteFailureCode.StorageUnavailable)
            : inner.SaveNoteAsync(request, cancellationToken);
}
