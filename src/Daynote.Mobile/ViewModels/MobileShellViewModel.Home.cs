using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Time;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The home screen's own state: the big date, the week strip, the day's note cards and to-dos, and
/// the Lists page's to-do bands and tag chips, all read from the same parse of the notes.
/// </summary>
public sealed partial class MobileShellViewModel
{
    /// <summary>The seven days of the selected date's week, Sunday first.</summary>
    public ObservableCollection<WeekDayViewModel> Week { get; } = [];

    /// <summary>The selected day's notes, as cards.</summary>
    public ObservableCollection<DayNoteCardViewModel> DayCards { get; } = [];

    /// <summary>The to-do lines written in the selected day's notes.</summary>
    public ObservableCollection<TodoRowViewModel> DayTodos { get; } = [];

    /// <summary>Every to-do, in the Lists page's five bands.</summary>
    public ObservableCollection<TodoGroupViewModel> TodoGroups { get; } = [];

    /// <summary>Every tag, for the Lists page and the search page's shortcuts.</summary>
    public ObservableCollection<TagChipViewModel> TagChips { get; } = [];

    /// <summary>The tags the search page offers before anything is typed: the six most used.</summary>
    public IEnumerable<TagChipViewModel> SearchTags => TagChips.Take(6);

    /// <summary>To-do totals per note, from the last parse, so a card can be rebuilt without one.</summary>
    private Dictionary<Guid, (int Done, int Total)> _todoCounts = [];

    private string? _selectedTagName;

    [ObservableProperty]
    private TagChipViewModel? _selectedTag;

    [ObservableProperty]
    private int _totalNoteCount;

    [ObservableProperty]
    private string _dayTodoCountText = string.Empty;

    public bool HasDayTodos => DayTodos.Count > 0;

    public bool IsTodoListEmpty => TodoGroups.Count == 0;

    public bool IsTagListEmpty => TagChips.Count == 0;

    public bool IsFavoritesListEmpty => FavoriteCards.Count == 0;

    /// <summary>The starred notes, newest day first, for the Lists page.</summary>
    public ObservableCollection<FavoriteCardViewModel> FavoriteCards { get; } = [];

    /// <summary>"30일" / "30": the big number at the top of the home screen.</summary>
    public string BigDayText => MobileStrings.Format("MobileDayNumberFormat", SelectedDate.Day.ToString(CultureInfo.CurrentCulture));

    /// <summary>"수요일" / "Wednesday", beside it.</summary>
    public string BigWeekdayText =>
        LocalizationService.Instance.Culture.DateTimeFormat.GetDayName(LocalDates.ToDateOnly(SelectedDate).DayOfWeek);

    public bool IsTodaySelected => SelectedDate == LocalDates.Today(_clock);

    /// <summary>"9월 30일 수요일" / "Wednesday, Sep 30": the editor's header.</summary>
    public string EditorDateText => Notes.SelectedTab is { } tab
        ? LocalDates.ToDateOnly(tab.LocalDate).ToString(MobileStrings.Get("MobileEditorDateFormat"), LocalizationService.Instance.Culture)
        : string.Empty;

    partial void OnSelectedDateChanged(LocalDate value)
    {
        OnPropertyChanged(nameof(BigDayText));
        OnPropertyChanged(nameof(BigWeekdayText));
        OnPropertyChanged(nameof(IsTodaySelected));
    }

    [RelayCommand]
    private Task PreviousWeek() => SelectDateAsync(LocalDates.AddDays(SelectedDate, -7));

    [RelayCommand]
    private Task NextWeek() => SelectDateAsync(LocalDates.AddDays(SelectedDate, 7));

    /// <summary>Rebuilds the week strip around the selected date, with a dot on each day that has notes.</summary>
    private async Task RefreshWeekAsync(CancellationToken cancellationToken = default)
    {
        DateOnly selected = LocalDates.ToDateOnly(SelectedDate);
        DateOnly start = selected.AddDays(-(int)selected.DayOfWeek);
        DateOnly end = start.AddDays(6);

        var withNotes = new HashSet<LocalDate>();
        foreach ((int year, int month) in new[] { (start.Year, start.Month), (end.Year, end.Month) }.Distinct())
        {
            foreach (DateContentSummary summary in await _repository
                .GetMonthContentSummaryAsync(year, month, cancellationToken).ConfigureAwait(true))
            {
                if (summary.NoteCount > 0)
                {
                    withNotes.Add(summary.Date);
                }
            }
        }

        LocalDate today = LocalDates.Today(_clock);
        string[] labels = WeekdayLabels();
        Week.Clear();
        for (int offset = 0; offset < 7; offset++)
        {
            LocalDate date = LocalDates.FromDateOnly(start.AddDays(offset));
            Week.Add(new WeekDayViewModel(
                date, labels[offset], date == SelectedDate, date == today, withNotes.Contains(date), SelectDateFromCalendarAsync));
        }
    }

    private static string[] WeekdayLabels() =>
    [
        AppStrings.WeekdaySun, AppStrings.WeekdayMon, AppStrings.WeekdayTue, AppStrings.WeekdayWed,
        AppStrings.WeekdayThu, AppStrings.WeekdayFri, AppStrings.WeekdaySat,
    ];

    /// <summary>
    /// Re-reads every note once and rebuilds what hangs off the to-do syntax: the shared to-do panel,
    /// the Lists page's bands, the day's to-dos and the progress on each card.
    /// </summary>
    private async Task RefreshTodosAsync(CancellationToken cancellationToken = default)
    {
        await Todo.RefreshAsync(cancellationToken).ConfigureAwait(true);

        ClockSnapshot snapshot = _clock.Read();
        DateTimeOffset now = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset);
        IReadOnlyList<NoteSummary> notes = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<TodoLine> lines = TodoParsing.Parse(notes, now);
        TotalNoteCount = notes.Count;

        TodoRowViewModel Row(TodoLine line) => new(new TodoItemViewModel(line, ToggleTodoAsync, JumpToTodoAsync), line);

        TodoGroups.Clear();
        foreach (TodoGroupViewModel group in TodoGroupViewModel.Build(lines, now, Row, GroupLabel))
        {
            TodoGroups.Add(group);
        }

        DayTodos.Clear();
        foreach (TodoLine line in lines.Where(line => line.Date == SelectedDate))
        {
            DayTodos.Add(Row(line));
        }

        DayTodoCountText = string.Create(
            CultureInfo.CurrentCulture, $"{DayTodos.Count(row => row.Item.Checked)}/{DayTodos.Count}");
        _todoCounts = lines
            .GroupBy(line => line.NoteId)
            .ToDictionary(group => group.Key, group => (group.Count(line => line.Checked), group.Count()));

        OnPropertyChanged(nameof(HasDayTodos));
        OnPropertyChanged(nameof(IsTodoListEmpty));
        RebuildCards();
    }

    private static string GroupLabel(TodoGroupKind kind) => kind switch
    {
        TodoGroupKind.Overdue => MobileStrings.Get("MobileTodoOverdue"),
        TodoGroupKind.Today => MobileStrings.Get("MobileTodoToday"),
        TodoGroupKind.Upcoming => MobileStrings.Get("MobileTodoUpcoming"),
        TodoGroupKind.NoDate => MobileStrings.Get("MobileTodoNoDate"),
        _ => MobileStrings.Get("MobileTodoDone"),
    };

    /// <summary>The day's cards, from the workspace's tabs and the last to-do parse.</summary>
    private void RebuildCards()
    {
        DayCards.Clear();
        if (Notes.ProjectionOnly)
        {
            return;
        }

        string empty = MobileStrings.Get("MobileNotePreviewEmpty");
        foreach (NoteTabViewModel tab in Notes.Tabs.Where(tab => !tab.IsProjection))
        {
            (int done, int total) = _todoCounts.TryGetValue(tab.Id.Value, out (int, int) counts) ? counts : (0, 0);
            DayCards.Add(new DayNoteCardViewModel(tab, done, total, empty));
        }
    }

    /// <summary>The shared favourites panel, and the phone's cards built from the same notes.</summary>
    private async Task RefreshFavoritesAsync(CancellationToken cancellationToken = default)
    {
        await Favorites.RefreshAsync(cancellationToken).ConfigureAwait(true);

        IReadOnlyList<NoteSummary> notes = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);
        FavoriteCards.Clear();
        foreach (NoteSummary note in notes
            .Where(note => note.IsFavorite)
            .OrderByDescending(note => (note.LocalDate.Year, note.LocalDate.Month, note.LocalDate.Day))
            .ThenBy(note => note.SortOrder))
        {
            FavoriteCards.Add(new FavoriteCardViewModel(note, OpenFavoriteAsync));
        }

        OnPropertyChanged(nameof(IsFavoritesListEmpty));
    }

    /// <summary>The shared tag panel, and the phone's chips built from the same notes.</summary>
    private async Task RefreshTagsAsync(CancellationToken cancellationToken = default)
    {
        await TagPanel.RefreshAsync(cancellationToken).ConfigureAwait(true);

        IReadOnlyList<NoteSummary> notes = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<NoteTagLink> links = await _repository.GetAllNoteTagsAsync(cancellationToken).ConfigureAwait(true);

        TagChips.Clear();
        foreach (TagSummary summary in NoteTagIndex.Build(notes, links))
        {
            TagChips.Add(new TagChipViewModel(summary, JumpToTagAsync, SelectTagChip));
        }

        // Keep the tag the user had chosen across a refresh; fall back to the most used one.
        SelectTagChip(TagChips.FirstOrDefault(chip => chip.Name == _selectedTagName) ?? TagChips.FirstOrDefault());
        OnPropertyChanged(nameof(SearchTags));
        OnPropertyChanged(nameof(IsTagListEmpty));
    }

    private void SelectTagChip(TagChipViewModel? chip)
    {
        foreach (TagChipViewModel each in TagChips)
        {
            each.IsSelected = ReferenceEquals(each, chip);
        }

        _selectedTagName = chip?.Name ?? _selectedTagName;
        SelectedTag = chip;
    }
}
