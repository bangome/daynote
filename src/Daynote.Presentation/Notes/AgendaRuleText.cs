using System.Globalization;
using Daynote.App.Localization;
using Daynote.Core.Agenda;

namespace Daynote.App.Notes;

/// <summary>
/// What a recurrence rule is called, in the language the UI is in
/// (docs/design-renewal/Daynote Mobile B Tasks - Events 06 Alerts §6i, §6j).
/// </summary>
/// <remarks>
/// The short form a row and a card show — "매일", "매월 25일", "Monthly on the 25th". The @
/// popup's readback is longer and lives in <see cref="AgendaReadback"/>, because it is a sentence
/// about one reading rather than a label on a rule.
/// <para>
/// It will name a rule this build cannot schedule, and that is the point: the unsupported-alert
/// notice puts the name in the sentence so the user can see which repeat it is talking about.
/// Naming one wrongly is cosmetic; scheduling one wrongly is a broken to-do, which is why
/// <see cref="AgendaRecurrence.Expand"/> is the strict half and this is the forgiving one.
/// </para>
/// </remarks>
public static class AgendaRuleText
{
    /// <summary>The rule's short name, or empty when there is no rule.</summary>
    public static string Describe(string? rrule) =>
        AgendaRecurrence.Summarize(rrule) is { } summary ? Describe(summary) : string.Empty;

    public static string Describe(RecurrenceSummary summary)
    {
        string weekdays = Weekdays(summary.Days);

        return summary.Frequency switch
        {
            RecurrenceFrequency.Daily => summary.Interval > 1
                ? Format(AppStrings.AgendaRuleEveryNDays, summary.Interval)
                : AppStrings.AgendaRuleDaily,

            RecurrenceFrequency.Weekly when weekdays.Length > 0 => summary.Interval > 1
                ? Format(AppStrings.AgendaRuleEveryNWeeksOn, summary.Interval, weekdays)
                : Format(AppStrings.AgendaRuleWeeklyOn, weekdays),

            RecurrenceFrequency.Weekly => summary.Interval > 1
                ? Format(AppStrings.AgendaRuleEveryNWeeks, summary.Interval)
                : AppStrings.AgendaRuleWeekly,

            // "둘째 주 월요일" / "the second Monday". Named, never scheduled.
            RecurrenceFrequency.Monthly when summary.Ordinal is { } ordinal && weekdays.Length > 0 =>
                Format(AppStrings.AgendaRuleMonthlyOnNthWeekday, Ordinal(ordinal), weekdays),

            RecurrenceFrequency.Monthly when summary.MonthDay is { } day =>
                Format(AppStrings.AgendaRuleMonthlyOnDay, day),

            RecurrenceFrequency.Monthly => AppStrings.AgendaRuleMonthly,
            RecurrenceFrequency.Yearly => AppStrings.AgendaRuleYearly,

            // An RRULE with no FREQ this build knows. Better a vague true word than a precise
            // false one.
            _ => AppStrings.AgendaRuleRepeats,
        };
    }

    /// <summary>
    /// The notice that goes where the alert list would be, or empty when the rule schedules
    /// normally.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose. Daily and weekly rules — intervals, named days, a count, an end date —
    /// take alerts like any other to-do, and saying otherwise would be a lie the user could
    /// disprove in a minute. Only a rule the expander refuses gets the notice, and it names that
    /// rule so the sentence is about something the user can see.
    /// </remarks>
    public static string AlertsUnsupportedNotice(string? rrule)
    {
        if (AgendaRecurrence.Summarize(rrule) is not { CanExpand: false } summary)
        {
            return string.Empty;
        }

        return Format(AppStrings.AgendaRuleNoAlerts, Describe(summary));
    }

    private static string Weekdays(IReadOnlyList<DayOfWeek> days)
    {
        if (days.Count == 0)
        {
            return string.Empty;
        }

        DateTimeFormatInfo format = CultureInfo.CurrentCulture.DateTimeFormat;
        return string.Join(
            AppStrings.AgendaRuleDayJoin,
            days.Order().Select(day => day.ToString() is var _
                ? format.GetAbbreviatedDayName(day)
                : string.Empty));
    }

    /// <summary>"둘째" / "second", and "마지막" / "last" for -1.</summary>
    private static string Ordinal(int position) => position switch
    {
        -1 => AppStrings.AgendaRuleOrdinalLast,
        1 => AppStrings.AgendaRuleOrdinal1,
        2 => AppStrings.AgendaRuleOrdinal2,
        3 => AppStrings.AgendaRuleOrdinal3,
        4 => AppStrings.AgendaRuleOrdinal4,
        _ => Format(AppStrings.AgendaRuleOrdinalN, position),
    };

    private static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
