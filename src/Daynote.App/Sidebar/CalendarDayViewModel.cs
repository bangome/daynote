using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.Core.Domain;

namespace Daynote.App.Sidebar;

/// <summary>One cell in the bottom-docked mini month calendar (DESIGN Section 5, CalendarDay).</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private bool _hasClipboard;

    public CalendarDayViewModel(LocalDate date, bool isToday, bool isOutsideMonth)
    {
        Date = date;
        IsToday = isToday;
        IsOutsideMonth = isOutsideMonth;
    }

    public LocalDate Date { get; }

    public int DayNumber => Date.Day;

    public bool IsToday { get; }

    public bool IsOutsideMonth { get; }

    public string AutomationName => Composition.LocalDates.DisplayLong(Date) + (IsToday ? Localization.AppStrings.TodaySuffix : string.Empty);

    /// <summary>
    /// Re-raises <see cref="AutomationName"/> after a language switch. Cells are owned by
    /// <see cref="MiniCalendarViewModel"/>, which drives this rather than each cell subscribing —
    /// a six-week grid is rebuilt often enough that 42 individual registrations would be churn.
    /// </summary>
    internal void RefreshAutomationName() => OnPropertyChanged(nameof(AutomationName));
}
