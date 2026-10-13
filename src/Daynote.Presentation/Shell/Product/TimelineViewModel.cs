using Daynote.Core.Agenda;
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
    private readonly IAgendaRepository _agenda;
    private readonly Func<Guid, LocalDate, Task> _openNote;
    private readonly Dictionary<LocalDate, int> _countByDate = [];

    private IReadOnlyList<NoteSummary> _summaries = [];
    private IReadOnlyList<AgendaItem> _todos = [];
    private IReadOnlyList<TimelineRowData> _mergedRows = [];
    private int _nextIndex;
    private LocalDate? _lastHeaderDate;

    public TimelineViewModel(INoteRepository repository, IAgendaRepository agenda, Func<Guid, LocalDate, Task> openNote)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
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
            var todos = await _agenda.GetAllAsync(cancellationToken).ConfigureAwait(true);
            _todos = todos.Where(t => t.ShowsInTimeline).ToList();
        }
        finally
        {
            IsLoading = false;
        }

        var list = new List<TimelineRowData>();
        foreach (NoteSummary summary in _summaries)
        {
            list.Add(new TimelineRowData(summary.LocalDate, summary));
        }
        foreach (AgendaItem todo in _todos)
        {
            DateTime dt = todo.DueAt?.Value.Date ?? todo.StartsAt?.Value.Date ?? todo.CreatedUtc.LocalDateTime.Date;
            LocalDate date = LocalDate.Parse(dt.ToString("yyyy-MM-dd")).Value;
            list.Add(new TimelineRowData(date, null, todo));
        }

        _mergedRows = [.. list.OrderByDescending(x => x.Date)];

        _countByDate.Clear();
        foreach (TimelineRowData item in _mergedRows)
        {
            _countByDate[item.Date] = _countByDate.GetValueOrDefault(item.Date) + 1;
        }

        Rows.Clear();
        _nextIndex = 0;
        _lastHeaderDate = null;
        IsEmpty = _mergedRows.Count == 0;
        HasMore = _mergedRows.Count > 0;
        LoadMore();
    }

    /// <summary>Appends the next page of note rows, inserting a date header on each date boundary.</summary>
    [RelayCommand]
    private void LoadMore()
    {
        if (_nextIndex >= _mergedRows.Count)
        {
            HasMore = false;
            return;
        }

        int end = Math.Min(_nextIndex + PageSize, _mergedRows.Count);
        for (; _nextIndex < end; _nextIndex++)
        {
            TimelineRowData item = _mergedRows[_nextIndex];
            if (_lastHeaderDate != item.Date)
            {
                Rows.Add(new TimelineDateHeaderRow(item.Date, _countByDate.GetValueOrDefault(item.Date)));
                _lastHeaderDate = item.Date;
            }

            if (item.Note is not null)
            {
                NoteSummary note = item.Note.Value;
                (string body, bool truncated) = NoteBodySummary.Summarize(note.Body);
                Rows.Add(new TimelineNoteRow(
                    note.Id, note.LocalDate, note.Title, note.IsFavorite, note.Body, body, truncated, _openNote));
            }
            else if (item.Todo is not null)
            {
                Rows.Add(new TimelineTodoRow(item.Todo));
            }
        }

        HasMore = _nextIndex < _mergedRows.Count;
    }

    private readonly record struct TimelineRowData(LocalDate Date, NoteSummary? Note = null, AgendaItem? Todo = null)
    {
        public TimelineRowData(LocalDate date, NoteSummary note) : this(date, note, null) { }
        public TimelineRowData(LocalDate date, AgendaItem todo) : this(date, null, todo) { }
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
