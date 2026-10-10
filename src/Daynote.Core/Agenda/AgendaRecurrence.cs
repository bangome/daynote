using System.Globalization;

namespace Daynote.Core.Agenda;

/// <summary>One dated appearance of an item (docs/TODOS.md §5).</summary>
/// <param name="Item">
/// The row to show and edit: the override where there is one, otherwise the series itself.
/// </param>
/// <param name="RecurrenceId">
/// The occurrence's original start, which is what identifies it for the rest of its life. An
/// override that moved the occurrence to another hour still answers to this one — that is what
/// <c>RECURRENCE-ID</c> is for.
/// </param>
/// <param name="Start">Where it actually falls, after any override moved it.</param>
public readonly record struct AgendaOccurrence(
    AgendaItem Item,
    WallClock RecurrenceId,
    WallClock Start,
    WallClock? End)
{
    /// <summary>True when a row of its own carries this one, rather than the rule.</summary>
    public bool IsOverride => Item.IsOverride;
}

/// <summary>How often a rule repeats, as far as naming it is concerned.</summary>
public enum RecurrenceFrequency
{
    /// <summary>Not an RRULE this build recognises at all, so there is nothing to call it.</summary>
    Unknown,
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// A rule reduced to what it takes to say its name out loud.
/// </summary>
/// <param name="Days">The weekdays it names, if any.</param>
/// <param name="MonthDay">The day of the month, for a monthly rule that names one.</param>
/// <param name="Ordinal">Which occurrence of the weekday — 2 for "the second Monday", -1 for the last.</param>
/// <param name="CanExpand">
/// Whether <see cref="AgendaRecurrence.Expand"/> can turn this one into dates. False is the case
/// the phone's alert notice exists for.
/// </param>
public readonly record struct RecurrenceSummary(
    RecurrenceFrequency Frequency,
    int Interval,
    IReadOnlyList<DayOfWeek> Days,
    int? MonthDay,
    int? Ordinal,
    bool CanExpand);

/// <summary>
/// Turns a rule into dates (docs/TODOS.md §5).
/// </summary>
/// <remarks>
/// Pure, and the only place that reads an <c>RRULE</c>. Everything that shows a repeating item —
/// the day panel, the Timeline, reminders — asks this rather than each inventing its own idea of
/// what "every Monday" means.
/// <para>
/// <b>Supported:</b> <c>FREQ=DAILY</c> and <c>FREQ=WEEKLY</c>, with <c>INTERVAL</c>, <c>BYDAY</c>,
/// <c>COUNT</c> and <c>UNTIL</c>, and a plain <c>FREQ=MONTHLY</c> or <c>FREQ=YEARLY</c> that repeats
/// on its anchor's own day — what the phone's to-do sheet offers as 매월 and 매년. That is everything
/// the <c>@</c> command and the sheet can produce and the common shape of what a calendar sends.
/// Anything else — <c>BYMONTHDAY</c>, "the second Monday", <c>BYSETPOS</c> — is
/// <b>reported as unreadable and expands to nothing</b>, because a rule half-understood puts
/// occurrences on the wrong days, and a to-do that silently appears on the wrong day is worse than
/// one that visibly does not appear at all.
/// </para>
/// </remarks>
public static class AgendaRecurrence
{
    /// <summary>A ceiling on one expansion, so a daily rule cannot be asked for a century of days.</summary>
    public const int MaxOccurrences = 1000;

    /// <summary>True when this build can turn the rule into dates.</summary>
    public static bool CanExpand(string? rrule) => Rule.TryParse(rrule, out _);

    /// <summary>
    /// Enough of a rule to name it, whether or not this build can expand it.
    /// </summary>
    /// <remarks>
    /// Naming and scheduling are deliberately different jobs with different standards. Putting an
    /// occurrence on the wrong day is a broken to-do, so <see cref="Expand"/> refuses anything it
    /// is not sure of; calling a rule by the wrong name is a cosmetic error, and saying nothing at
    /// all is worse than saying "매월 25일" about a rule that turns out to be subtler. The phone's
    /// unsupported-alert notice puts the name in the sentence precisely so the user can see what
    /// it is talking about.
    /// </remarks>
    public static RecurrenceSummary? Summarize(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
        {
            return null;
        }

        var frequency = RecurrenceFrequency.Unknown;
        int interval = 1;
        List<DayOfWeek> days = [];
        int? monthDay = null;
        int? ordinal = null;

        foreach (string part in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            string value = pair[1].Trim();
            switch (pair[0].Trim().ToUpperInvariant())
            {
                case "FREQ":
                    frequency = value.ToUpperInvariant() switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        "YEARLY" => RecurrenceFrequency.Yearly,
                        _ => RecurrenceFrequency.Unknown,
                    };
                    break;

                case "INTERVAL":
                    if (int.TryParse(value, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    {
                        interval = parsed;
                    }

                    break;

                case "BYMONTHDAY":
                    if (int.TryParse(value.Split(',')[0], CultureInfo.InvariantCulture, out int day))
                    {
                        monthDay = day;
                    }

                    break;

                case "BYDAY":
                    foreach (string entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string token = entry.Trim();
                        // "2MO" — the second Monday — is a position this cannot schedule but can
                        // perfectly well name.
                        int cut = 0;
                        while (cut < token.Length && (char.IsAsciiDigit(token[cut]) || token[cut] is '-' or '+'))
                        {
                            cut += 1;
                        }

                        if (cut > 0 && int.TryParse(token[..cut], CultureInfo.InvariantCulture, out int position))
                        {
                            ordinal = position;
                        }

                        if (Rule.ParseDay(token[cut..]) is { } weekday)
                        {
                            days.Add(weekday);
                        }
                    }

                    break;

                default:
                    break;
            }
        }

        return new RecurrenceSummary(frequency, interval, days, monthDay, ordinal, CanExpand(rrule));
    }

    /// <summary>
    /// Every occurrence of <paramref name="series"/> that falls between <paramref name="from"/> and
    /// <paramref name="to"/>, inclusive, with <paramref name="overrides"/> applied.
    /// </summary>
    /// <param name="overrides">
    /// Rows whose <see cref="AgendaItem.SeriesId"/> is this series. Any that are not are ignored
    /// rather than rejected — the caller usually hands over everything it loaded for the day.
    /// </param>
    public static IReadOnlyList<AgendaOccurrence> Expand(
        AgendaItem series,
        IReadOnlyList<AgendaItem> overrides,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(overrides);

        if (series.Anchor is not { } anchor
            || !Rule.TryParse(series.Rrule, out Rule rule)
            || to < from)
        {
            return [];
        }

        Dictionary<WallClock, AgendaItem> moved = overrides
            .Where(item => item.SeriesId == series.Id && item.RecurrenceId is not null)
            .GroupBy(static item => item.RecurrenceId!.Value)
            // Two rows for one occurrence should not happen; if it does, the later edit wins, the
            // same way last-write-wins settles it between devices.
            .ToDictionary(static group => group.Key, static group => group.MaxBy(static i => i.UpdatedUtc)!);

        var skipped = new HashSet<WallClock>(series.ExceptionDates);
        TimeSpan length = series.StartsAt is { } start && series.EndsAt is { } end
            ? end.Value - start.Value
            : TimeSpan.Zero;

        var found = new List<AgendaOccurrence>();
        foreach (WallClock occurrence in rule.Walk(anchor))
        {
            if (found.Count >= MaxOccurrences)
            {
                break;
            }

            DateOnly day = DateOnly.FromDateTime(occurrence.Value);
            if (day > to && !moved.ContainsKey(occurrence))
            {
                // Past the window, and nothing was moved out of it into view. Walk() is ordered,
                // so there is nothing further to find.
                break;
            }

            if (skipped.Contains(occurrence))
            {
                // EXDATE: the occurrence is gone outright, override or not.
                continue;
            }

            if (moved.TryGetValue(occurrence, out AgendaItem? replacement))
            {
                if (replacement.Anchor is not { } at)
                {
                    continue;
                }

                DateOnly when = DateOnly.FromDateTime(at.Value);
                if (when >= from && when <= to)
                {
                    found.Add(new AgendaOccurrence(replacement, occurrence, at, replacement.EndsAt));
                }

                continue;
            }

            if (day >= from && day <= to)
            {
                found.Add(new AgendaOccurrence(
                    series,
                    occurrence,
                    occurrence,
                    length == TimeSpan.Zero ? null : new WallClock(occurrence.Value + length)));
            }
        }

        // An override can be dragged earlier than the occurrence it replaces, so the walk's order
        // is not the order they land in.
        return [.. found.OrderBy(static o => o.Start.Value)];
    }

    /// <summary>
    /// The next occurrence at or after <paramref name="after"/>, which is what the <c>@</c> popup
    /// reads back and what §5 says an export emits.
    /// </summary>
    public static AgendaOccurrence? Next(
        AgendaItem series,
        IReadOnlyList<AgendaItem> overrides,
        DateOnly after,
        int searchDays = 400)
    {
        IReadOnlyList<AgendaOccurrence> found =
            Expand(series, overrides, after, after.AddDays(searchDays));
        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>The part of an RRULE this build understands.</summary>
    private readonly record struct Rule(
        RecurrenceFrequency Frequency,
        int Interval,
        IReadOnlyList<DayOfWeek> Days,
        int? Count,
        DateOnly? Until)
    {
        internal static bool TryParse(string? rrule, out Rule rule)
        {
            rule = default;
            if (string.IsNullOrWhiteSpace(rrule))
            {
                return false;
            }

            var frequency = RecurrenceFrequency.Unknown;
            int interval = 1;
            List<DayOfWeek> days = [];
            int? count = null;
            DateOnly? until = null;

            foreach (string part in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);
                if (pair.Length != 2)
                {
                    return false;
                }

                string name = pair[0].Trim().ToUpperInvariant();
                string value = pair[1].Trim();

                switch (name)
                {
                    case "FREQ":
                        frequency = value.ToUpperInvariant() switch
                        {
                            "DAILY" => RecurrenceFrequency.Daily,
                            "WEEKLY" => RecurrenceFrequency.Weekly,
                            "MONTHLY" => RecurrenceFrequency.Monthly,
                            "YEARLY" => RecurrenceFrequency.Yearly,
                            _ => RecurrenceFrequency.Unknown,
                        };
                        if (frequency == RecurrenceFrequency.Unknown)
                        {
                            return false;
                        }

                        break;

                    case "INTERVAL":
                        if (!int.TryParse(value, CultureInfo.InvariantCulture, out interval) || interval < 1)
                        {
                            return false;
                        }

                        break;

                    case "BYDAY":
                        foreach (string day in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (ParseDay(day.Trim()) is not { } parsed)
                            {
                                // "2MO" — the second Monday — is a monthly shape this does not do.
                                return false;
                            }

                            days.Add(parsed);
                        }

                        break;

                    case "COUNT":
                        if (!int.TryParse(value, CultureInfo.InvariantCulture, out int parsedCount) || parsedCount < 1)
                        {
                            return false;
                        }

                        count = parsedCount;
                        break;

                    case "UNTIL":
                        if (!TryParseUntil(value, out DateOnly parsedUntil))
                        {
                            return false;
                        }

                        until = parsedUntil;
                        break;

                    case "WKST":
                        // Only changes which day a week starts on, which matters for shapes this
                        // does not support. Accepted and ignored.
                        break;

                    default:
                        return false;
                }
            }

            // A monthly or yearly rule that names days is a shape ("the second Monday", "every
            // Monday in March") this does not do; only the plain one, on the anchor's own day.
            if (frequency == RecurrenceFrequency.Unknown
                || (frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly && days.Count > 0))
            {
                return false;
            }

            rule = new Rule(frequency, interval, days, count, until);
            return true;
        }

        /// <summary>Every occurrence from <paramref name="anchor"/> onwards, in order and lazily.</summary>
        internal IEnumerable<WallClock> Walk(WallClock anchor)
        {
            TimeOnly time = TimeOnly.FromDateTime(anchor.Value);
            DateOnly start = DateOnly.FromDateTime(anchor.Value);
            int emitted = 0;

            if (Frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly)
            {
                // The anchor's day of the month, every Interval months (or years). A month without
                // that day — the 31st in April, the 29th of February outside a leap year — is
                // skipped, as iCalendar says, rather than moved to its last day.
                int step = Frequency == RecurrenceFrequency.Yearly ? 12 * Interval : Interval;
                for (int months = 0; ; months += step)
                {
                    DateOnly month = new DateOnly(start.Year, start.Month, 1).AddMonths(months);
                    if (start.Day > DateTime.DaysInMonth(month.Year, month.Month))
                    {
                        if (Until is { } last && month > last)
                        {
                            yield break;
                        }

                        continue;
                    }

                    DateOnly date = new(month.Year, month.Month, start.Day);
                    if (!Allowed(date, ref emitted))
                    {
                        yield break;
                    }

                    yield return new WallClock(date.ToDateTime(time));
                }
            }

            if (Frequency == RecurrenceFrequency.Daily)
            {
                for (DateOnly day = start; ; day = day.AddDays(Interval))
                {
                    if (!Allowed(day, ref emitted))
                    {
                        yield break;
                    }

                    yield return new WallClock(day.ToDateTime(time));
                }
            }

            // Weekly: every named day inside each repeating week. With no BYDAY the rule repeats on
            // the day it started, which is what both iCalendar and the @ command mean by it.
            IReadOnlyList<DayOfWeek> days = Days.Count > 0 ? [.. Days.Order()] : [start.DayOfWeek];
            DateOnly weekStart = start.AddDays(-DayOffset(start.DayOfWeek));

            for (; ; weekStart = weekStart.AddDays(7 * Interval))
            {
                foreach (DayOfWeek day in days)
                {
                    DateOnly date = weekStart.AddDays(DayOffset(day));
                    if (date < start)
                    {
                        continue;
                    }

                    if (!Allowed(date, ref emitted))
                    {
                        yield break;
                    }

                    yield return new WallClock(date.ToDateTime(time));
                }
            }
        }

        /// <summary>COUNT and UNTIL, the two ways a rule stops.</summary>
        private bool Allowed(DateOnly day, ref int emitted)
        {
            if (Until is { } last && day > last)
            {
                return false;
            }

            if (Count is { } limit && emitted >= limit)
            {
                return false;
            }

            emitted += 1;
            return true;
        }

        /// <summary>Days from Monday, because ISO weeks start there and so does iCalendar's WKST default.</summary>
        private static int DayOffset(DayOfWeek day) => ((int)day + 6) % 7;

        internal static DayOfWeek? ParseDay(string value) => value.ToUpperInvariant() switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => null,
        };

        /// <summary>
        /// <c>UNTIL</c> arrives as a date or a UTC instant. Only the date is kept: everything in
        /// this model is a wall clock against the item's own zone (§6), and comparing it to an
        /// instant is the mistake that whole section exists to prevent.
        /// </summary>
        private static bool TryParseUntil(string value, out DateOnly until)
        {
            string date = value.Length >= 8 ? value[..8] : value;
            return DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out until);
        }
    }
}
