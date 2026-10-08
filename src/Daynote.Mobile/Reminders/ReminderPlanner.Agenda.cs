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
/// <see cref="FireTime(TodoLine, TimeSpan)"/> keeps its shape and its two rules — a date and a time
/// remind at that time, a date alone at the settings page's default reminder time. What the entity
/// adds is the lead the old comment promised: <c>VALARM</c> triggers arrive as
/// <see cref="AgendaItem.AlarmLeadMinutes"/> and are subtracted here, so one item can hold several
/// reminders.
/// </para>
/// <para>
/// <b>Reminder ids change at the cutover</b>, from a hash of the note and the line's text to a hash
/// of the item's own id. That is the point of the entity: a line of prose has no identity, so the
/// old id had to be reconstructed from its text and had to move whenever the text did.
/// <see cref="Diff"/> handles the changeover without special-casing — every old id is absent from
/// the new set and is cancelled, every new one is scheduled — so the first run after the upgrade
/// re-registers the lot and nothing fires twice.
/// </para>
/// </remarks>
public static partial class ReminderPlanner
{
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

        DateTime wall;
        if (item.Kind == AgendaKind.Event)
        {
            // An event is a block of time; its start is a real clock reading and the default
            // reminder time has nothing to say about it.
            if (item.StartsAt is not { } start)
            {
                return null;
            }

            wall = start.Value;
        }
        else if (item.DueAt is { } due)
        {
            wall = item.HasDueTime ? due.Value : due.Value.Date + dateOnlyTime;
        }
        else if (item.StartsAt is { } start)
        {
            // A task's DTSTART is a day placement, not a clock reading — the migration of §8 sets
            // it to midnight of the note's date — so it reminds at the default time like any other
            // dated-but-untimed to-do.
            wall = start.Value.Date + dateOnlyTime;
        }
        else
        {
            return null;
        }

        return DateTime.SpecifyKind(wall, DateTimeKind.Unspecified).AddMinutes(-leadMinutes);
    }

    /// <summary>
    /// The reminders <paramref name="items"/> call for at <paramref name="now"/>, nearest first, at
    /// most <paramref name="capacity"/> of them.
    /// </summary>
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

        DateTime localNow = DateTime.SpecifyKind(now.DateTime, DateTimeKind.Unspecified);
        var planned = new List<Reminder>();

        foreach (AgendaItem item in items.OrderBy(static i => i.Id))
        {
            if (item.Status != AgendaStatus.NeedsAction || item.IsSeries)
            {
                // A series carries a rule, not a time. Expanding RRULE into occurrences is its own
                // piece of work (§5) and is not built, so a repeating to-do does not remind yet —
                // its overrides, which are ordinary rows with concrete times, do.
                continue;
            }

            foreach (int lead in LeadsFor(item))
            {
                if (FireTime(item, dateOnlyTime, lead) is not { } at || at <= localNow)
                {
                    continue;
                }

                planned.Add(new Reminder(
                    IdFor(item.Id, lead),
                    at,
                    item.Title,
                    BodyFor(item, dateOnlyTime, listNames),
                    DayOf(item, dateOnlyTime),
                    // What a tap opens. Empty when the to-do was never captured from a note, which
                    // is ordinary: the shell then selects the day and opens no editor.
                    item.SourceNoteId ?? Guid.Empty));
            }
        }

        return [.. planned
            .OrderBy(static r => r.At)
            .ThenBy(static r => r.Id, StringComparer.Ordinal)
            .Take(capacity)];
    }

    /// <summary>"todo-" and 16 hex digits over the item's id and this alarm's lead.</summary>
    /// <remarks>
    /// Keeps the shape the heads already parse — <see cref="RequestCodeFor"/> reads the last eight
    /// digits — while the input becomes the one thing that is genuinely stable about a to-do. The
    /// lead is in it because an item with two alarms is two notifications, and they must not
    /// replace each other.
    /// </remarks>
    public static string IdFor(Guid itemId, int leadMinutes)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(
            itemId.ToString("N"),
            "\n",
            leadMinutes.ToString(CultureInfo.InvariantCulture))));
        return "todo-" + Convert.ToHexStringLower(hash, 0, 8);
    }

    /// <summary>
    /// The alarms to honour, and nothing else.
    /// </summary>
    /// <remarks>
    /// An empty list means no alert. It used to mean "the usual single one", because there was
    /// nowhere in the product to say otherwise and every dated to-do had reminded since reminders
    /// shipped. The alerts design (Mobile §06) replaced that guess with something honest: a dated
    /// item is *created* carrying one alert and the user can remove it. The default survives where
    /// it is decided — AgendaCapture for a new item, the §8 migration for an old one — and the
    /// planner is left reading what the item actually says.
    /// </remarks>
    private static IReadOnlyList<int> LeadsFor(AgendaItem item) => item.AlarmLeadMinutes;

    private static string BodyFor(
        AgendaItem item,
        TimeSpan dateOnlyTime,
        IReadOnlyDictionary<Guid, string>? listNames)
    {
        // The item's own time, not the moment this alarm fires: a reminder 30 minutes early still
        // has to say when the thing actually is.
        string when = string.Empty;
        if (FireTime(item, dateOnlyTime) is { } at)
        {
            when = HasClockTime(item)
                ? string.Create(CultureInfo.InvariantCulture, $"{at.Month}/{at.Day} {at:HH\\:mm}")
                : string.Create(CultureInfo.InvariantCulture, $"{at.Month}/{at.Day}");
        }

        if (listNames is not null
            && listNames.TryGetValue(item.ListId, out string? list)
            && list.Length > 0)
        {
            return string.Format(CultureInfo.CurrentCulture, AppStrings.ReminderBodyFormat, list, when);
        }

        return when;
    }

    private static bool HasClockTime(AgendaItem item) =>
        item.Kind == AgendaKind.Event || (item.DueAt is not null && item.HasDueTime);

    private static LocalDate DayOf(AgendaItem item, TimeSpan dateOnlyTime)
    {
        DateTime at = FireTime(item, dateOnlyTime) ?? DateTime.Today;
        return LocalDate.Parse(at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Value;
    }
}
