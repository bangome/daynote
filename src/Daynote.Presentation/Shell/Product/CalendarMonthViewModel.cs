using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Time;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The month calendar card. Builds a Sunday-start grid for the cursor month and paints each in-month day
/// with its content cues from <see cref="INoteRepository.GetMonthContentSummaryAsync"/> (one aggregate
/// query — dates absent from the result are treated as empty, mirroring the design's <c>notesBy</c> map).
/// Clicking a day asks the shell to select that date; month navigation reloads the summary.
/// </summary>
public sealed partial class CalendarMonthViewModel : ObservableObject, ILanguageAware
{
    private readonly IClock _clock;
    private readonly INoteRepository _repository;
    private readonly Func<LocalDate, Task> _onSelectDate;

    public CalendarMonthViewModel(IClock clock, INoteRepository repository, Func<LocalDate, Task> onSelectDate)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onSelectDate = onSelectDate ?? throw new ArgumentNullException(nameof(onSelectDate));
        LocalDate today = LocalDates.Today(clock);
        _cursorYear = today.Year;
        _cursorMonth = today.Month;
        _selectedDate = today;
        LocalizationService.Instance.Observe(this);
    }

    public ObservableCollection<CalendarDayCellViewModel> Cells { get; } = [];

    [ObservableProperty]
    private int _cursorYear;

    [ObservableProperty]
    private int _cursorMonth;

    [ObservableProperty]
    private LocalDate _selectedDate;

    [ObservableProperty]
    private string _monthLabel = string.Empty;

    /// <summary>The month label is stored rather than computed, so re-derive it on a language switch.</summary>
    void ILanguageAware.OnLanguageChanged() =>
        MonthLabel = LocalDates.DisplayMonth(LocalDates.FromDateOnly(new DateOnly(CursorYear, CursorMonth, 1)));

    /// <summary>Rebuilds the grid for the cursor month, querying the aggregate content summary.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        MonthLabel = LocalDates.DisplayMonth(LocalDates.FromDateOnly(new DateOnly(CursorYear, CursorMonth, 1)));
        IReadOnlyList<DateContentSummary> summaries = await _repository
            .GetMonthContentSummaryAsync(CursorYear, CursorMonth, cancellationToken).ConfigureAwait(true);
        var byDate = summaries.ToDictionary(s => s.Date);

        LocalDate today = LocalDates.Today(_clock);
        var first = new DateOnly(CursorYear, CursorMonth, 1);
        int startOffset = (int)first.DayOfWeek; // Sunday = 0
        int daysIn = DateTime.DaysInMonth(CursorYear, CursorMonth);
        int total = (int)Math.Ceiling((startOffset + daysIn) / 7.0) * 7;

        Cells.Clear();
        for (int i = 0; i < total; i++)
        {
            int dayNum = i - startOffset + 1;
            bool inMonth = dayNum >= 1 && dayNum <= daysIn;
            DateOnly dayDate = inMonth ? new DateOnly(CursorYear, CursorMonth, dayNum) : first.AddDays(i - startOffset);
            LocalDate date = LocalDates.FromDateOnly(dayDate);
            int dow = (int)dayDate.DayOfWeek;
            byDate.TryGetValue(date, out DateContentSummary summary);

            var cell = new CalendarDayCellViewModel(
                date,
                inMonth,
                inMonth && date == today,
                dow == 0,
                dow == 6,
                inMonth ? summary.NoteCount : 0,
                inMonth && (summary.HasClipboard || summary.HasFiles),
                _onSelectDate)
            {
                IsSelected = inMonth && date == SelectedDate,
            };
            Cells.Add(cell);
        }
    }

    /// <summary>Moves the cursor to the month containing <paramref name="date"/> and marks it selected.</summary>
    public async Task ShowSelectedAsync(LocalDate date, CancellationToken cancellationToken = default)
    {
        SelectedDate = date;
        CursorYear = date.Year;
        CursorMonth = date.Month;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Updates the selection highlight in place when the selected date is already in the cursor month.</summary>
    public void SyncSelection(LocalDate date)
    {
        SelectedDate = date;
        foreach (CalendarDayCellViewModel cell in Cells)
        {
            cell.IsSelected = cell.IsInMonth && cell.Date == date;
        }
    }

    [RelayCommand]
    private Task PreviousMonth()
    {
        if (CursorMonth == 1)
        {
            CursorMonth = 12;
            CursorYear--;
        }
        else
        {
            CursorMonth--;
        }

        return LoadAsync();
    }

    [RelayCommand]
    private Task NextMonth()
    {
        if (CursorMonth == 12)
        {
            CursorMonth = 1;
            CursorYear++;
        }
        else
        {
            CursorMonth++;
        }

        return LoadAsync();
    }
}
