using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Time;

namespace Daynote.App.Sidebar;

/// <summary>
/// Bottom-docked mini month calendar. Composes a fixed six-week grid of <see cref="CalendarDayViewModel"/>
/// cells with month paging; selection routes through the shared <see cref="IDateNavigator"/>.
/// </summary>
public sealed partial class MiniCalendarViewModel : ObservableObject, ILanguageAware
{
    private const int WeeksShown = 6;
    private const int DaysPerWeek = 7;

    private readonly IDateNavigator _navigator;
    private readonly IClock _clock;
    private LocalDate _visibleMonth;

    public MiniCalendarViewModel(IDateNavigator navigator, IClock clock)
    {
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        RefreshWeekdayHeadings();
        _visibleMonth = FirstOfMonth(LocalDates.Today(clock));
        LocalizationService.Instance.Observe(this);
    }

    public ObservableCollection<CalendarDayViewModel> Days { get; } = [];

    /// <summary>
    /// The Sunday-first weekday header row, re-derived when the language changes.
    /// </summary>
    /// <remarks>
    /// Replaced wholesale rather than mutated in place: an <see cref="ObservableCollection{T}"/>
    /// raises CollectionChanged, which a bound WPF <c>CollectionView</c> refuses to accept from any
    /// thread but the dispatcher's. A plain list swap travels through the ordinary property-changed
    /// path, which WPF does marshal for us.
    /// </remarks>
    public IReadOnlyList<string> Weekdays
    {
        get => _weekdays;
        private set => SetProperty(ref _weekdays, value);
    }

    private IReadOnlyList<string> _weekdays = [];

    [ObservableProperty]
    private string _monthDisplay = string.Empty;

    /// <summary>Rebuilds the grid around the selected date, showing that date's month.</summary>
    public void ShowSelected(LocalDate selectedDate)
    {
        _visibleMonth = FirstOfMonth(selectedDate);
        Rebuild(selectedDate);
    }

    /// <summary>Updates selection cues without changing the visible month.</summary>
    public void SyncSelection(LocalDate selectedDate)
    {
        foreach (CalendarDayViewModel day in Days)
        {
            day.IsSelected = day.Date == selectedDate;
        }
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        _visibleMonth = LocalDates.AddMonths(_visibleMonth, -1);
        Rebuild(_navigator.SelectedDate);
    }

    [RelayCommand]
    private void NextMonth()
    {
        _visibleMonth = LocalDates.AddMonths(_visibleMonth, 1);
        Rebuild(_navigator.SelectedDate);
    }

    [RelayCommand]
    private Task SelectDayAsync(CalendarDayViewModel? day) =>
        day is null ? Task.CompletedTask : _navigator.SelectDateAsync(day.Date);

    private void Rebuild(LocalDate selectedDate)
    {
        MonthDisplay = LocalDates.DisplayMonth(_visibleMonth);
        LocalDate today = LocalDates.Today(_clock);
        LocalDate cursor = StartOfGrid(_visibleMonth);
        Days.Clear();
        for (int cell = 0; cell < WeeksShown * DaysPerWeek; cell++)
        {
            var day = new CalendarDayViewModel(
                cursor,
                isToday: cursor == today,
                isOutsideMonth: !LocalDates.IsSameMonth(cursor, _visibleMonth))
            {
                IsSelected = cursor == selectedDate,
            };
            Days.Add(day);
            cursor = LocalDates.AddDays(cursor, 1);
        }
    }

    private static LocalDate FirstOfMonth(LocalDate date) =>
        LocalDates.FromDateOnly(new DateOnly(date.Year, date.Month, 1));

    private static LocalDate StartOfGrid(LocalDate firstOfMonth)
    {
        int offset = (int)LocalDates.ToDateOnly(firstOfMonth).DayOfWeek;
        return LocalDates.AddDays(firstOfMonth, -offset);
    }

    private void RefreshWeekdayHeadings()
    {
        string[] names = LocalizationService.Instance.Culture.DateTimeFormat.ShortestDayNames;
        Weekdays = names.Length == DaysPerWeek
            ? names
            : ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    }

    /// <summary>
    /// Re-derives everything that reads from the catalog or the culture: the weekday header row,
    /// the month label, and each day cell's accessible name.
    /// </summary>
    void ILanguageAware.OnLanguageChanged()
    {
        RefreshWeekdayHeadings();
        MonthDisplay = LocalDates.DisplayMonth(_visibleMonth);
        foreach (CalendarDayViewModel day in Days)
        {
            day.RefreshAutomationName();
        }
    }
}
