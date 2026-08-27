using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.App.Shell;
using Daynote.Core.Search;

namespace Daynote.App.Search;

public enum SearchLoadState
{
    Idle,
    Searching,
    Populated,
    Empty,
    Error,
}

/// <summary>Debounce timer seam so tests can drive the 200 ms coalescing deterministically.</summary>
public interface ISearchScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemSearchScheduler : ISearchScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

/// <summary>
/// The unified search surface. Debounces the query (200 ms), cancels stale searches so a newer query
/// always supersedes an in-flight older one, pages deterministically through the SearchService
/// contract, and passes the query through literally. Activation deep-links to the exact source; a
/// stale result shows a payload-free message and refreshes (DESIGN Section 5; plan Todo 9).
/// </summary>
public sealed partial class SearchViewModel : ObservableObject, IDisposable, ILanguageAware
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(200);

    private readonly SearchService _service;
    private readonly ISearchActivation _activation;
    private readonly ISearchScheduler _scheduler;
    private readonly TimeSpan _debounce;
    private readonly SynchronizationContext? _sync;

    private CancellationTokenSource? _pending;
    private long _generation;
    private string _activeQuery = string.Empty;
    private int _loadedPage = -1;
    private bool _hasMore;
    private bool _disposed;

    public SearchViewModel(
        SearchService service,
        ISearchActivation activation,
        ISearchScheduler? scheduler = null,
        TimeSpan? debounce = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _scheduler = scheduler ?? new SystemSearchScheduler();
        _debounce = debounce ?? DefaultDebounce;
        if (_debounce < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
        _sync = SynchronizationContext.Current;
        LocalizationService.Instance.Observe(this);
    }

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private SearchLoadState _loadState = SearchLoadState.Idle;

    [ObservableProperty]
    private string? _staleMessage;

    [ObservableProperty]
    private SearchResultViewModel? _selectedResult;

    public bool HasResults => Results.Count > 0;

    public bool HasMore => _hasMore;

    /// <summary>Fixed scope label shown in the overlay header (query/count/scope stay fixed).</summary>
    /// <summary>Everything visible here is catalog-derived, so re-read every binding.</summary>
    void ILanguageAware.OnLanguageChanged() => OnPropertyChanged(string.Empty);

    public string ScopeDisplay => Localization.AppStrings.SearchScopeValue;

    public string ResultCountDisplay => LoadState switch
    {
        SearchLoadState.Searching => Localization.AppStrings.SearchLoading,
        SearchLoadState.Empty => Localization.AppStrings.SearchNoResults,
        SearchLoadState.Error => Localization.AppStrings.SearchUnavailableShort,
        _ => _hasMore
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Localization.AppStrings.SearchResultsShownFormat, Results.Count)
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, Localization.AppStrings.SearchResultsCountFormat, Results.Count),
    };

    /// <summary>Completes when the current debounced/in-flight search has applied or been superseded.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    partial void OnQueryChanged(string value) => ScheduleSearch(value);

    /// <summary>Opens the overlay and focuses search input (Ctrl+F / SearchBox activation).</summary>
    [RelayCommand]
    public void Open() => IsOpen = true;

    /// <summary>Escape from an empty query, or successful navigation, closes and clears the overlay.</summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        StaleMessage = null;
    }

    /// <summary>Clears the query (first Escape). Results clear; the overlay stays open.</summary>
    public void ClearQuery() => Query = string.Empty;

    private void ScheduleSearch(string? text)
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
        StaleMessage = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            _generation++;
            ResetResults();
            LoadState = SearchLoadState.Idle;
            Pending = Task.CompletedTask;
            return;
        }

        IsOpen = true;
        var cts = new CancellationTokenSource();
        _pending = cts;
        long generation = ++_generation;
        LoadState = SearchLoadState.Searching;
        RaiseHeader();
        Pending = RunSearchAsync(text, generation, cts.Token);
    }

    private async Task RunSearchAsync(string text, long generation, CancellationToken token)
    {
        try
        {
            if (_debounce > TimeSpan.Zero)
            {
                await _scheduler.DelayAsync(_debounce, token).ConfigureAwait(false);
            }

            SearchPage page = await _service.SearchAsync(text, 0, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Post(() => ApplyFirstPage(text, page, generation));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Post(() => FailSearch(generation));
        }
    }

    private void ApplyFirstPage(string text, SearchPage page, long generation)
    {
        if (generation != _generation)
        {
            return;
        }

        Results.Clear();
        SelectedResult = null;
        _activeQuery = text;
        _loadedPage = page.PageNumber;
        _hasMore = page.HasMore;
        foreach (SearchResult result in page.Results)
        {
            Results.Add(new SearchResultViewModel(result));
        }

        LoadState = Results.Count == 0 ? SearchLoadState.Empty : SearchLoadState.Populated;
        RaiseHeader();
    }

    private void FailSearch(long generation)
    {
        if (generation != _generation)
        {
            return;
        }

        ResetResults();
        LoadState = SearchLoadState.Error;
        RaiseHeader();
    }

    /// <summary>Loads the next deterministic page and appends it in contract order.</summary>
    [RelayCommand]
    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (!_hasMore || _activeQuery.Length == 0)
        {
            return;
        }

        long generation = _generation;
        SearchPage page = await _service
            .SearchAsync(_activeQuery, _loadedPage + 1, cancellationToken).ConfigureAwait(false);
        Post(() =>
        {
            if (generation != _generation)
            {
                return;
            }

            _loadedPage = page.PageNumber;
            _hasMore = page.HasMore;
            foreach (SearchResult result in page.Results)
            {
                Results.Add(new SearchResultViewModel(result));
            }

            RaiseHeader();
        });
    }

    /// <summary>Activates a result: deep-link to the exact source, or show a stale message and refresh.</summary>
    [RelayCommand]
    public async Task ActivateAsync(SearchResultViewModel? result, CancellationToken cancellationToken = default)
    {
        if (result is null)
        {
            return;
        }

        SearchActivationOutcome outcome = await _activation
            .ActivateAsync(result, cancellationToken).ConfigureAwait(true);
        if (outcome.Navigated)
        {
            Close();
            return;
        }

        StaleMessage = Localization.AppStrings.StaleSearchResult;
        RefreshAfterStale();
    }

    private void RefreshAfterStale()
    {
        if (_activeQuery.Length == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = cts;
        long generation = ++_generation;
        LoadState = SearchLoadState.Searching;
        RaiseHeader();
        Pending = RunSearchAsync(_activeQuery, generation, cts.Token);
    }

    private void ResetResults()
    {
        Results.Clear();
        _hasMore = false;
        _loadedPage = -1;
        _activeQuery = string.Empty;
        SelectedResult = null;
    }

    private void RaiseHeader()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(ResultCountDisplay));
    }

    partial void OnLoadStateChanged(SearchLoadState value) => RaiseHeader();

    private void Post(Action action)
    {
        if (_sync is null)
        {
            action();
        }
        else
        {
            _sync.Post(_ => action(), null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }
}
