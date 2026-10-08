using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;

namespace Daynote.Mobile.Reminders;

/// <summary>
/// The same planning, driven by to-do entities instead of <c>-[ ]</c> lines (docs/TODOS.md §12
/// step 4).
/// </summary>
/// <remarks>
/// <b>Nothing calls this yet.</b> <see cref="ReminderCoordinator"/> still reads note bodies,
/// because §12 requires desktop and phone to switch their readers in the same release and the
/// desktop half is not built. Step 3 swaps the call; the shape below is already what it needs.
/// <para>
/// <see cref="FireTime(TodoLine, TimeSpan)"/> keeps its shape and its two rules — a to-do that
/// carries a clock reminds at it, one that carries only a day reminds at the settings page's
/// default reminder time. What the entity adds is the lead the old comment promised:
/// <c>VALARM</c> triggers arrive as <see cref="AgendaItem.AlarmLeadMinutes"/> and are subtracted
/// here, so one item can hold several reminders.
/// </para>
/// <para>
/// <b>Reminder ids change at the cutover</b>, from a hash of the note and the line's text to a
/// hash of the item's own id. That is the point of the entity: a line of prose has no identity, so
/// the old id had to be reconstructed from its text and had to move whenever the text did.
/// <see cref="Diff"/> handles the changeover without special-casing — every old id is absent from
/// the new set and is cancelled, every new one is scheduled — so the first run after the upgrade
/// re-registers the lot and nothing fires twice.
/// </para>
/// </remarks>
public static partial class ReminderPlanner
{
    /// <summary>
    /// How far ahead a repeating item is expanded.
    /// </summary>
    /// <remarks>
    /// Two months, not a year. iOS holds 64 pending notifications and the set is topped up every
    /// time the app runs, so computing further buys nothing — and without a bound a daily rule
    /// would generate thousands of occurrences to sort and throw away.
    /// </remarks>
    public const int HorizonDays = 60;

    /// <summary>
    /// When one alarm on <paramref name="item"/> fires, in local wall-clock time; null when the
    /// item has no time to hang an alarm off.
    /// </summary>
    /// <param name="leadMinutes">
    /// How long before the item, from <see cref="AgendaItem.AlarmLeadMinutes"/>. Zero is "at the
    /// time itself", which is what <c>TRIGGER:PT0S</c> means.
    /// </param>
    public static DateTime? FireTime(AgendaItem item, TimeSpan dateOnlyTime, int leadMinutes = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Anchor is { } anchor ? FireTime(item, anchor, dateOnlyTime, leadMinutes) : null;
    }

    /// <summary>
    /// The same, for one occurrence of a repeating item: <paramref name="anchor"/> is where that
    /// occurrence falls rather than where the rule started.
    /// </summary>
    private static DateTime FireTime(
        AgendaItem item,
        WallClock anchor,
        TimeSpan dateOnlyTime,
        int leadMinutes)
    {
        // An event is a block of time; its start is a real clock reading and the default reminder
        // time has nothing to say about it.
        //
        // For a to-do, `has_due_time` is the one field that says whether a clock was given, and it
        // answers for whichever field is carrying the wall clock. A one-off to-do keeps its time
        // in DUE; a repeating one keeps it in DTSTART, because that is what an RRULE anchors on.
        // Reading the flag rather than the field is what keeps "@매주 월 7시" ringing at seven —
        // the obvious version of this looked at DUE, found none, and quietly moved every repeating
        // to-do to the default hour.
        bool hasClock = item.Kind == AgendaKind.Event || item.HasDueTime;
        DateTime wall = hasClock ? anchor.Value : anchor.Value.Date + dateOnlyTime;
        return DateTime.SpecifyKind(wall, DateTimeKind.Unspecified).AddMinutes(-leadMinutes);
    }

    /// <summary>
    /// The reminders <paramref name="items"/> call for at <paramref name="now"/>, nearest first, at
    /// most <paramref name="capacity"/> of them.
    /// </summary>
    /// <param name="items">
    /// Everything loaded: one-off items, series, and the overrides belonging to them. An override
    /// is planned as part of its series rather than on its own, unless its series is not here — an
    /// orphan still reminds, because the alternative is a to-do that silently stops.
    /// </param>
    /// <param name="listNames">
    /// List id to display name, for the notification's second line. A list that is not here — the
    /// built-in one, whose stored name is empty — contributes nothing and the body is the time
    /// alone.
    /// </param>
    public static IReadOnlyList<Reminder> Plan(
        IEnumerable<AgendaItem> items,
        DateTimeOffset now,
        int capacity,
        TimeSpan dateOnlyTime,
        IReadOnlyDictionary<Guid, string>? listNames = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (capacity <= 0)
        {
            return [];
        }

        AgendaItem[] all = [.. items.OrderBy(static i => i.Id)];
        var known = new HashSet<Guid>(all.Where(static i => i.IsSeries).Select(static i => i.Id));

        DateTime localNow = DateTime.SpecifyKind(now.DateTime, DateTimeKind.Unspecified);
        DateOnly today = DateOnly.FromDateTime(localNow);
        var planned = new List<Reminder>();

        foreach (AgendaItem item in all)
        {
            if (item.IsSeries)
            {
                AgendaItem[] overrides = [.. all.Where(other => other.SeriesId == item.Id)];
                foreach (AgendaOccurrence occurrence in
                    AgendaRecurrence.Expand(item, overrides, today, today.AddDays(HorizonDays)))
                {
                    Add(planned, occurrence.Item, item.Id, occurrence.RecurrenceId, occurrence.Start,
                        localNow, dateOnlyTime, listNames);
                }

                continue;
            }

            if (item.IsOverride && known.Contains(item.SeriesId!.Value))
            {
                // Already emitted by its series, with the occurrence in its id.
                continue;
            }

            if (item.Anchor is { } anchor)
            {
                Add(planned, item, item.Id, occurrence: null, anchor,
                    localNow, dateOnlyTime, listNames);
            }
        }

        return [.. planned
            .OrderBy(static r => r.At)
            .ThenBy(static r => r.Id, StringComparer.Ordinal)
            .Take(capacity)];
    }

    private static void Add(
        List<Reminder> planned,
        AgendaItem item,
        Guid identity,
        WallClock? occurrence,
        WallClock anchor,
        DateTime localNow,
        TimeSpan dateOnlyTime,
        IReadOnlyDictionary<Guid, string>? listNames)
    {
        if (item.Status != AgendaStatus.NeedsAction)
        {
            return;
        }

        foreach (int lead in item.AlarmLeadMinutes)
        {
            DateTime at = FireTime(item, anchor, dateOnlyTime, lead);
            if (at <= localNow)
            {
                continue;
            }

            planned.Add(new Reminder(
                IdFor(identity, occurrence, lead),
                at,
                item.Title,
                BodyFor(item, anchor, dateOnlyTime, listNames),
                DayOf(item, anchor, dateOnlyTime),
                // What a tap opens. Empty when the to-do was never captured from a note, which is
                // ordinary: the shell then selects the day and opens no editor.
                item.SourceNoteId ?? Guid.Empty));
        }
    }

    /// <summary>"todo-" and 16 hex digits over what the reminder is for and when it fires.</summary>
    /// <remarks>
    /// Keeps the shape the heads already parse — <see cref="RequestCodeFor"/> reads the last eight
    /// digits — while the input becomes the one thing that is genuinely stable about a to-do.
    /// <para>
    /// For an occurrence the identity is the series plus the original start, not the row that
    /// happens to carry it. Moving one occurrence to another hour then replaces its notification
    /// rather than adding a second, which is the same thing <c>RECURRENCE-ID</c> does everywhere
    /// else. The lead is in it because an item with two alarms is two notifications.
    /// </para>
    /// </remarks>
    public static string IdFor(Guid identity, WallClock? occurrence, int leadMinutes)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(
            identity.ToString("N"),
            "\n",
            occurrence?.ToString() ?? string.Empty,
            "\n",
            leadMinutes.ToString(CultureInfo.InvariantCulture))));
        return "todo-" + Convert.ToHexStringLower(hash, 0, 8);
    }

    private static string BodyFor(
        AgendaItem item,
        WallClock anchor,
        TimeSpan dateOnlyTime,
        IReadOnlyDictionary<Guid, string>? listNames)
    {
        // The occurrence's own time, not the moment this alarm fires: a reminder 30 minutes early
        // still has to say when the thing actually is.
        DateTime at = FireTime(item, anchor, dateOnlyTime, 0);
        string when = item.Kind == AgendaKind.Event || item.HasDueTime
            ? string.Create(CultureInfo.InvariantCulture, $"{at.Month}/{at.Day} {at:HH\\:mm}")
            : string.Create(CultureInfo.InvariantCulture, $"{at.Month}/{at.Day}");

        if (listNames is not null
            && listNames.TryGetValue(item.ListId, out string? list)
            && list.Length > 0)
        {
            return string.Format(CultureInfo.CurrentCulture, AppStrings.ReminderBodyFormat, list, when);
        }

        return when;
    }

    private static LocalDate DayOf(AgendaItem item, WallClock anchor, TimeSpan dateOnlyTime)
    {
        DateTime at = FireTime(item, anchor, dateOnlyTime, 0);
        return LocalDate.Parse(at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Value;
    }
}
