namespace Daynote.Core.Agenda;

/// <summary>
/// The alerts an item can carry, and what the "알림 추가" sheet offers
/// (docs/design-renewal/Daynote Mobile B Tasks - Events 06 Alerts.dc.html).
/// </summary>
/// <remarks>
/// A lead in minutes before the item, which is what iCalendar's <c>VALARM TRIGGER</c> is: a
/// duration relative to the start. <see cref="AgendaItem.AlarmLeadMinutes"/> holds them, several
/// at a time, and an empty list means no alert at all.
/// <para>
/// <b>Empty means none, and that is a change.</b> Before this design there was nowhere to say
/// "do not remind me", so the planner read an empty list as "the usual single alert" to keep the
/// behaviour every dated to-do has had since reminders shipped. §06 replaces that with something
/// honest: a new dated item is *created* carrying one alert, which the user can then remove. The
/// default survives and "none" becomes expressible, and the planner stops guessing.
/// </para>
/// </remarks>
public static class AgendaAlert
{
    /// <summary>Rings at the item's own time. <c>TRIGGER:PT0S</c>.</summary>
    public const int AtTime = 0;

    /// <summary>
    /// What a newly created dated item starts with: one alert at its own time. For a to-do with a
    /// day but no clock, that resolves to the settings page's default reminder time, which is what
    /// the sheet shows as "당일 오전 9:00".
    /// </summary>
    public static IReadOnlyList<int> Default { get; } = [AtTime];

    /// <summary>Nothing. The user removed the last row, and the sheet reads "알림 없음".</summary>
    public static IReadOnlyList<int> None { get; } = [];

    /// <summary>
    /// The fixed leads the sheet offers, in the order it lists them. "하루 전 오전 9:00" and
    /// "직접 설정…" are not here: the first depends on the item's own time
    /// (<see cref="DayBefore"/>) and the second is whatever the user picks.
    /// </summary>
    public static IReadOnlyList<int> Offered { get; } = [AtTime, 5, 10, 30, 60];

    /// <summary>
    /// The lead that lands at <paramref name="hour"/> on the previous day — the sheet's
    /// "하루 전 오전 9:00".
    /// </summary>
    /// <remarks>
    /// A lead and not a second kind of alarm, because iCalendar has only the one: a trigger is a
    /// duration before the item, so "the day before at nine" is just a long one. It therefore
    /// depends on the item's own time, which is why the sheet computes it rather than listing a
    /// number.
    /// </remarks>
    public static int DayBefore(TimeOnly itemTime, TimeOnly hour) =>
        (int)(itemTime.ToTimeSpan() - hour.ToTimeSpan() + TimeSpan.FromDays(1)).TotalMinutes;

    /// <summary>
    /// Adds <paramref name="lead"/> if it is not already there, keeping the list ordered from the
    /// earliest warning to the item itself.
    /// </summary>
    /// <remarks>
    /// Ordered, because the sheet lists them and two orders of the same set would look like two
    /// different states; distinct, because the sheet marks an already-chosen lead "추가됨" rather
    /// than letting it be added twice, and two identical alerts would be two identical
    /// notifications.
    /// </remarks>
    public static IReadOnlyList<int> With(IReadOnlyList<int> leads, int lead)
    {
        ArgumentNullException.ThrowIfNull(leads);
        ArgumentOutOfRangeException.ThrowIfNegative(lead);
        return leads.Contains(lead) ? leads : [.. leads.Append(lead).OrderDescending()];
    }

    public static IReadOnlyList<int> Without(IReadOnlyList<int> leads, int lead)
    {
        ArgumentNullException.ThrowIfNull(leads);
        return [.. leads.Where(existing => existing != lead)];
    }
}
