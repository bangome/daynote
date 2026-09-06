using System.Globalization;
using Daynote.Core.Domain;
using Daynote.Core.Time;

namespace Daynote.App.Composition;

/// <summary>
/// App-layer helpers for <see cref="LocalDate"/> arithmetic and locale-aware display. Core keeps
/// <c>LocalDate</c> construction internal, so the shell round-trips through the canonical ISO string.
/// </summary>
public static class LocalDates
{
    public static LocalDate FromDateOnly(DateOnly date)
    {
        DomainResult<LocalDate> parsed = LocalDate.Parse(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return parsed.IsSuccess
            ? parsed.Value
            : throw new ArgumentOutOfRangeException(nameof(date), "The date is not a canonical local date.");
    }

    public static DateOnly ToDateOnly(LocalDate date) => new(date.Year, date.Month, date.Day);

    public static LocalDate Today(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ClockSnapshot snapshot = clock.Read();
        DateTimeOffset local = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset);
        return FromDateOnly(DateOnly.FromDateTime(local.DateTime));
    }

    public static LocalDate AddDays(LocalDate date, int days) => FromDateOnly(ToDateOnly(date).AddDays(days));

    public static LocalDate AddMonths(LocalDate date, int months) => FromDateOnly(ToDateOnly(date).AddMonths(months));

    public static bool IsSameMonth(LocalDate first, LocalDate second) =>
        first.Year == second.Year && first.Month == second.Month;

    /// <summary>
    /// A full date with weekday. Both the pattern and the culture come from the active UI
    /// language, so switching to English yields "Monday, July 27, 2026" rather than an
    /// English weekday wedged into the Korean 년/월/일 pattern.
    /// </summary>
    public static string DisplayLong(LocalDate date) =>
        ToDateOnly(date).ToString(Localization.AppStrings.DateFormatLong, Localization.LocalizationService.Instance.Culture);

    public static string DisplayMonth(LocalDate date) =>
        ToDateOnly(date).ToString(Localization.AppStrings.DateFormatMonth, Localization.LocalizationService.Instance.Culture);

    /// <summary>The compact month/day heading shown above the note list ("7월 27일 (일)" / "Sun, Jul 27").</summary>
    public static string DisplayDayHeading(LocalDate date) =>
        ToDateOnly(date).ToString(Localization.AppStrings.DateFormatDayHeading, Localization.LocalizationService.Instance.Culture);

    public static string DisplayShort(LocalDate date) =>
        ToDateOnly(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
