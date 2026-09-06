using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Search;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The unified titlebar search: debounced, stale-guarded queries against <see cref="SearchService"/>
/// producing 노트/파일 rows, plus VM-derived 날짜 rows grouped from the matched items' dates (with a
/// per-date note count). Activating a row navigates exactly (note → date+note;
/// file → date+파일; date → date) through the shell callback.
/// </summary>
public sealed partial class SearchDropdownViewModel : ObservableObject
{
    // Actual content matches (notes/clips/files) get a generous cap so a body match is never crowded
    // out by the derived 날짜 rows; the overlay scrolls when the combined list overflows. (Fixes a
    // reported miss where a real body match sat below the old 12-row combined cap.)
    private const int ItemRowCap = 40;
    private const int DateRowCap = 8;

    private readonly SearchService _search;
    private readonly INoteRepository _repository;
    private readonly Func<SearchNavigation, Task> _onNavigate;
    private readonly TimeSpan _debounce;
    private int _sequence;
    private CancellationTokenSource? _cts;

    public SearchDropdownViewModel(
        SearchService search,
        INoteRepository repository,
        Func<SearchNavigation, Task> onNavigate,
        TimeSpan? debounce = null)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onNavigate = onNavigate ?? throw new ArgumentNullException(nameof(onNavigate));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(180);
    }

    public ObservableCollection<SearchResultRowViewModel> Results { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _noResults;

    partial void OnQueryChanged(string value)
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(value);
        IsOpen = hasQuery;
        if (!hasQuery)
        {
            _cts?.Cancel();
            Results.Clear();
            NoResults = false;
            return;
        }

        _ = RunQueryAsync(value);
    }

    [RelayCommand]
    private void Clear() => Query = string.Empty;

    /// <summary>Runs the query immediately (no debounce); used by tests and Enter.</summary>
    public async Task SearchNowAsync(string text, CancellationToken cancellationToken = default)
    {
        int sequence = ++_sequence;
        await ExecuteAsync(text, sequence, cancellationToken).ConfigureAwait(true);
    }

    private async Task RunQueryAsync(string text)
    {
        int sequence = ++_sequence;
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            if (_debounce > TimeSpan.Zero)
            {
                await Task.Delay(_debounce, cts.Token).ConfigureAwait(true);
            }

            await ExecuteAsync(text, sequence, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ExecuteAsync(string text, int sequence, CancellationToken cancellationToken)
    {
        SearchPage page = await _search.SearchAsync(text, 0, cancellationToken).ConfigureAwait(true);
        if (sequence != _sequence)
        {
            return; // A newer query superseded this one.
        }

        var itemRows = new List<SearchResultRowViewModel>();
        var dateCounts = new Dictionary<LocalDate, int>();
        foreach (SearchResult result in page.Results)
        {
            dateCounts.TryAdd(result.LocalDate, -1);
            switch (result.SourceType)
            {
                case SearchSourceType.Note:
                    itemRows.Add(Row(AppStrings.SearchKindNote, result.Title, result.Snippet, result.LocalDate, text,
                        new SearchNavigation(result.LocalDate, result.SourceId, null)));
                    break;
                case SearchSourceType.File:
                    itemRows.Add(Row(AppStrings.SearchKindFile, result.Title, result.Snippet, result.LocalDate, text,
                        new SearchNavigation(result.LocalDate, null, RightTab.Files)));
                    break;
                default:
                    // Unknown/legacy source types (e.g. the removed clipboard capture) produce no row.
                    break;
            }
        }

        // Derive 날짜 rows from the dates that matched, with the total note count on each (design "노트 N개").
        var dateRows = new List<SearchResultRowViewModel>();
        foreach (LocalDate date in dateCounts.Keys.OrderByDescending(d => d).Take(DateRowCap).ToArray())
        {
            int count = await NoteCountAsync(date, cancellationToken).ConfigureAwait(true);
            if (sequence != _sequence)
            {
                return;
            }

            dateRows.Add(Row(
                AppStrings.SearchKindDate,
                Composition.LocalDates.DisplayLong(date),
                string.Format(CultureInfo.CurrentCulture, AppStrings.SearchDateNoteCountFormat, count),
                date,
                text,
                new SearchNavigation(date, null, null),
                dateLabelEmpty: true));
        }

        // Content matches first (capped generously), then the derived date rows; the overlay scrolls.
        Results.Clear();
        foreach (SearchResultRowViewModel row in itemRows.Take(ItemRowCap))
        {
            Results.Add(row);
        }

        foreach (SearchResultRowViewModel row in dateRows)
        {
            Results.Add(row);
        }

        NoResults = Results.Count == 0;
    }

    private async Task<int> NoteCountAsync(LocalDate date, CancellationToken cancellationToken)
    {
        DayWorkspace workspace = await _repository.GetDayWorkspaceStateAsync(date, cancellationToken).ConfigureAwait(true);
        return workspace.Notes.IsProjectionOnly ? 0 : workspace.Notes.Notes.Count(n => !n.IsProjection);
    }

    private SearchResultRowViewModel Row(
        string kind, string title, string sub, LocalDate date, string query, SearchNavigation navigation, bool dateLabelEmpty = false) =>
        new(kind, title, sub,
            dateLabelEmpty ? string.Empty : string.Create(CultureInfo.CurrentCulture, $"{date.Month}/{date.Day}"),
            query, navigation, ActivateAsync);

    private async Task ActivateAsync(SearchNavigation navigation)
    {
        Query = string.Empty;
        await _onNavigate(navigation).ConfigureAwait(true);
    }
}
