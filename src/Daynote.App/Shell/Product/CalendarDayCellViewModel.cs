using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.Core.Domain;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One day cell in the month grid (calendar-notes.dc.html calendar). Out-of-month cells render blank;
/// in-month cells show the day number, an accent dot when the date has notes, an orange dot when it has
/// clipboard items or files, and a small note-count badge. Selection and today are distinct treatments.
/// </summary>
public sealed partial class CalendarDayCellViewModel : ObservableObject
{
    private readonly Func<LocalDate, Task> _onSelect;

    public CalendarDayCellViewModel(
        LocalDate date,
        bool inMonth,
        bool isToday,
        bool isSunday,
        bool isSaturday,
        int noteCount,
        bool hasExtras,
        Func<LocalDate, Task> onSelect)
    {
        Date = date;
        IsInMonth = inMonth;
        IsToday = isToday;
        IsSunday = isSunday;
        IsSaturday = isSaturday;
        HasNotes = inMonth && noteCount > 0;
        HasExtras = inMonth && hasExtras;
        HasCount = inMonth && noteCount > 0;
        CountText = noteCount > 0 ? noteCount.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
        DayText = inMonth ? date.Day.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
        _onSelect = onSelect;
    }

    public LocalDate Date { get; }

    public string DayText { get; }

    public bool IsInMonth { get; }

    public bool IsToday { get; }

    public bool IsSunday { get; }

    public bool IsSaturday { get; }

    public bool HasNotes { get; }

    public bool HasExtras { get; }

    public bool HasCount { get; }

    public string CountText { get; }

    /// <summary>True when this cell is the selected date; drives the accent-soft fill and accent day number.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private Task Select() => IsInMonth ? _onSelect(Date) : Task.CompletedTask;
}
