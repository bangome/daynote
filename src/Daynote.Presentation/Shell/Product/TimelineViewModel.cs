using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The read-only timeline: a single vertically scrolling list of every note across all dates, newest
/// day first, with prominent date boundaries and lazily loaded pages for infinite scroll. Bodies are
/// truncated via <see cref="NoteBodySummary"/> with a per-card expand/collapse toggle. Activating a
/// card asks the shell to leave timeline mode and open that note in the normal editor on its date.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject, ILanguageAware
{
    private const int PageSize = 20;

    private readonly INoteRepository _repository;
    private readonly Func<Guid, LocalDate, Task> _openNote;
    private readonly Dictionary<LocalDate, int> _countByDate = [];

    private IReadOnlyList<NoteSummary> _summaries = [];
    private int _nextIndex;
    private LocalDate? _lastHeaderDate;

    public TimelineViewModel(INoteRepository repository, Func<Guid, LocalDate, Task> openNote)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _openNote = openNote ?? throw new ArgumentNullException(nameof(openNote));
        LocalizationService.Instance.Observe(this);
    }

    public ObservableCollection<TimelineRow> Rows { get; } = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasMore;

    /// <summary>Headers are catalog-derived; rebuild the rows so day headings and counts re-localize.</summary>
    void ILanguageAware.OnLanguageChanged()
    {
        foreach (TimelineRow row in Rows)
        {
            if (row is TimelineNoteRow note)
            {
                note.OnLanguageChanged();
            }
        }

        RebuildHeaders();
    }

    /// <summary>Loads every note (newest date first), resets paging, and appends the first page.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            _summaries = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }

        _countByDate.Clear();
        foreach (NoteSummary summary in _summaries)
        {
            _countByDate[summary.LocalDate] = _countByDate.GetValueOrDefault(summary.LocalDate) + 1;
        }

        Rows.Clear();
        _nextIndex = 0;
        _lastHeaderDate = null;
        IsEmpty = _summaries.Count == 0;
        HasMore = _summaries.Count > 0;
        LoadMore();
    }

    /// <summary>Appends the next page of note rows, inserting a date header on each date boundary.</summary>
    [RelayCommand]
    private void LoadMore()
    {
        if (_nextIndex >= _summaries.Count)
        {
            HasMore = false;
            return;
        }

        int end = Math.Min(_nextIndex + PageSize, _summaries.Count);
        for (; _nextIndex < end; _nextIndex++)
        {
            NoteSummary summary = _summaries[_nextIndex];
            if (_lastHeaderDate != summary.LocalDate)
            {
                Rows.Add(new TimelineDateHeaderRow(summary.LocalDate, _countByDate.GetValueOrDefault(summary.LocalDate)));
                _lastHeaderDate = summary.LocalDate;
            }

            (string body, bool truncated) = NoteBodySummary.Summarize(summary.Body);
            Rows.Add(new TimelineNoteRow(
                summary.Id, summary.LocalDate, summary.Title, summary.IsFavorite, summary.Body, body, truncated, _openNote));
        }

        HasMore = _nextIndex < _summaries.Count;
    }

    /// <summary>Replaces the current header rows in place so their localized text refreshes.</summary>
    private void RebuildHeaders()
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i] is TimelineDateHeaderRow header)
            {
                Rows[i] = new TimelineDateHeaderRow(header.Date, _countByDate.GetValueOrDefault(header.Date));
            }
        }
    }
}
