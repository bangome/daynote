using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Time;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The desktop header's week strip (Daynote Desktop B): the seven days of the week that holds the
/// selected date, Sunday first like the month calendar, each with a dot when it has notes. Picking a day
/// or stepping a week asks the shell to select a date, so every navigation still goes through its
/// autosave-safe flush; the strip only redraws once the shell calls <see cref="ShowAsync"/>.
/// </summary>
/// <remarks>
/// A week can straddle two months, so the dots come from up to two month summaries — the same
/// aggregate query the calendar makes, rather than a per-day read.
/// </remarks>
public sealed partial class WeekStripViewModel : ObservableObject, ILanguageAware
{
    private readonly IClock _clock;
    private readonly INoteRepository _repository;
    private readonly Func<LocalDate, Task> _onSelectDate;
    private int _sequence;

    public WeekStripViewModel(IClock clock, INoteRepository repository, Func<LocalDate, Task> onSelectDate)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onSelectDate = onSelectDate ?? throw new ArgumentNullException(nameof(onSelectDate));
        _selectedDate = LocalDates.Today(clock);
        LocalizationService.Instance.Observe(this);
    }

    public ObservableCollection<WeekStripDayViewModel> Days { get; } = [];

    [ObservableProperty]
    private LocalDate _selectedDate;

    /// <summary>The Sunday that starts the week holding <paramref name="date"/>.</summary>
    public static LocalDate WeekStart(LocalDate date)
    {
        DateOnly day = LocalDates.ToDateOnly(date);
        return LocalDates.FromDateOnly(day.AddDays(-(int)day.DayOfWeek));
    }

    /// <summary>Rebuilds the strip around <paramref name="selected"/>, reading which days have notes.</summary>
    /// <remarks>
    /// Stale-guarded like the search: two quick steps can finish out of order, and the one that
    /// started last is the one that draws. Otherwise the strip could end up on the older week while
    /// the header and the editor show the newer day.
    /// </remarks>
    public async Task ShowAsync(LocalDate selected, CancellationToken cancellationToken = default)
    {
        int sequence = ++_sequence;
        SelectedDate = selected;
        LocalDate start = WeekStart(selected);
        LocalDate end = LocalDates.AddDays(start, 6);

        var withNotes = new HashSet<LocalDate>();
        foreach ((int year, int month) in new[] { (start.Year, start.Month), (end.Year, end.Month) }.Distinct())
        {
            IReadOnlyList<DateContentSummary> summaries = await _repository
                .GetMonthContentSummaryAsync(year, month, cancellationToken).ConfigureAwait(true);
            foreach (DateContentSummary summary in summaries)
            {
                if (summary.NoteCount > 0)
                {
                    withNotes.Add(summary.Date);
                }
            }
        }

        if (sequence != _sequence)
        {
            return; // A newer call superseded this one.
        }

        LocalDate today = LocalDates.Today(_clock);
        Days.Clear();
        for (int i = 0; i < 7; i++)
        {
            LocalDate date = LocalDates.AddDays(start, i);
            Days.Add(new WeekStripDayViewModel(
                date,
                WeekdayLabel(i),
                isSelected: date == selected,
                isToday: date == today,
                hasNotes: withNotes.Contains(date),
                _onSelectDate));
        }
    }

    /// <summary>Re-reads the dots for the week on screen, after a note was added or removed.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => ShowAsync(SelectedDate, cancellationToken);

    [RelayCommand]
    private Task PreviousWeek() => _onSelectDate(LocalDates.AddDays(SelectedDate, -7));

    [RelayCommand]
    private Task NextWeek() => _onSelectDate(LocalDates.AddDays(SelectedDate, 7));

    void ILanguageAware.OnLanguageChanged()
    {
        for (int i = 0; i < Days.Count; i++)
        {
            Days[i].WeekdayLabel = WeekdayLabel(i);
        }
    }

    private static string WeekdayLabel(int index) => index switch
    {
        0 => AppStrings.WeekdaySun,
        1 => AppStrings.WeekdayMon,
        2 => AppStrings.WeekdayTue,
        3 => AppStrings.WeekdayWed,
        4 => AppStrings.WeekdayThu,
        5 => AppStrings.WeekdayFri,
        _ => AppStrings.WeekdaySat,
    };
}

/// <summary>
/// One day pill in the week strip: weekday over the day number over a note dot. Selected days are
/// filled with the primary colour, today (when not selected) is ringed, Sunday's weekday is red.
/// </summary>
public sealed partial class WeekStripDayViewModel : ObservableObject
{
    private readonly Func<LocalDate, Task> _onSelect;

    public WeekStripDayViewModel(
        LocalDate date,
        string weekdayLabel,
        bool isSelected,
        bool isToday,
        bool hasNotes,
        Func<LocalDate, Task> onSelect)
    {
        Date = date;
        _weekdayLabel = weekdayLabel;
        IsSelected = isSelected;
        IsToday = isToday;
        HasNotes = hasNotes;
        _onSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
    }

    public LocalDate Date { get; }

    public string DayText => Date.Day.ToString(CultureInfo.CurrentCulture);

    [ObservableProperty]
    private string _weekdayLabel;

    public bool IsSelected { get; }

    public bool IsToday { get; }

    /// <summary>The orange ring: today, unless today is also the selected (filled) day.</summary>
    public bool IsTodayRing => IsToday && !IsSelected;

    public bool IsSunday => LocalDates.ToDateOnly(Date).DayOfWeek == DayOfWeek.Sunday;

    public bool HasNotes { get; }

    [RelayCommand]
    private Task Select() => _onSelect(Date);
}
