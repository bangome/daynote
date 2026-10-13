using Daynote.App.Localization;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Which day a week starts on in the month grids and the week strip (desktop settings, 한 주의 시작).
/// </summary>
/// <remarks>
/// One value for the process rather than a parameter threaded through every calendar, because it is
/// a reading preference of the person at the screen and every grid has to agree with it. Sunday
/// until a shell says otherwise; the phone never does.
/// </remarks>
public static class CalendarWeekStart
{
    public static DayOfWeek First { get; private set; } = DayOfWeek.Sunday;

    /// <summary>Raised after <see cref="First"/> changes, so the grids on screen rebuild.</summary>
    public static event EventHandler? Changed;

    public static void Set(DayOfWeek first)
    {
        if (first is not (DayOfWeek.Sunday or DayOfWeek.Monday) || first == First)
        {
            return;
        }

        First = first;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>How many days <paramref name="day"/> sits after the week's first.</summary>
    public static int Offset(DayOfWeek day) => ((int)day - (int)First + 7) % 7;

    /// <summary>The seven weekday headings, in the order the grids draw them.</summary>
    public static IReadOnlyList<WeekdayHeading> Headings() =>
        [.. Enumerable.Range(0, 7).Select(static i => new WeekdayHeading((DayOfWeek)(((int)First + i) % 7)))];
}

/// <summary>One weekday heading over a month grid: "일" in red, "토" in the accent.</summary>
public sealed class WeekdayHeading(DayOfWeek day)
{
    public DayOfWeek Day { get; } = day;

    public string Label => Day switch
    {
        DayOfWeek.Sunday => AppStrings.WeekdaySun,
        DayOfWeek.Monday => AppStrings.WeekdayMon,
        DayOfWeek.Tuesday => AppStrings.WeekdayTue,
        DayOfWeek.Wednesday => AppStrings.WeekdayWed,
        DayOfWeek.Thursday => AppStrings.WeekdayThu,
        DayOfWeek.Friday => AppStrings.WeekdayFri,
        _ => AppStrings.WeekdaySat,
    };

    public bool IsSunday => Day == DayOfWeek.Sunday;

    public bool IsSaturday => Day == DayOfWeek.Saturday;
}
