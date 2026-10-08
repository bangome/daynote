using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Notes;

namespace Daynote.Mobile.Reminders;

/// <summary>
/// Which to-dos get a reminder and when: the pure half of to-do reminders, with no clock, store or
/// platform of its own.
/// </summary>
/// <remarks>
/// The to-do grammar is <see cref="TodoParsing"/>'s, so a line the Lists tab shows with a due date is
/// exactly a line that can remind. Only unchecked lines whose fire time is still ahead are planned.
/// </remarks>
public static partial class ReminderPlanner
{
    /// <summary>When a to-do with a date and no time reminds unless the user picked another time: 09:00.</summary>
    public static readonly TimeSpan DefaultDateOnlyTime = new(9, 0, 0);

    /// <summary>
    /// The moment a to-do reminds, in local wall-clock time; null when it has no due date.
    /// </summary>
    /// <remarks>
    /// The two rules live here and nowhere else: a date and a time remind at that time, a date alone
    /// at <paramref name="dateOnlyTime"/> on that day (the settings page's "default reminder time").
    /// A "remind me N minutes before" option would subtract its lead here and touch nothing else.
    /// </remarks>
    public static DateTime? FireTime(TodoLine line, TimeSpan dateOnlyTime)
    {
        if (line.Due is not { } due)
        {
            return null;
        }

        DateTime wall = DateTime.SpecifyKind(due.DateTime, DateTimeKind.Unspecified);
        return line.HasDueTime ? wall : wall.Date + dateOnlyTime;
    }

    /// <summary>
    /// The reminders the notes call for at <paramref name="now"/>, nearest first, at most
    /// <paramref name="capacity"/> of them, with date-only to-dos at <paramref name="dateOnlyTime"/>.
    /// </summary>
    public static IReadOnlyList<Reminder> Plan(IEnumerable<NoteSummary> notes, DateTimeOffset now, int capacity, TimeSpan dateOnlyTime)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (capacity <= 0)
        {
            return [];
        }

        DateTime localNow = DateTime.SpecifyKind(now.DateTime, DateTimeKind.Unspecified);
        var seen = new Dictionary<(Guid, string), int>();
        var planned = new List<Reminder>();

        // In note order, so the n-th "- [] call mom" of a note keeps its number when other lines
        // move. Checked lines count too: ticking the first of two duplicates must not hand the
        // second one the first one's id.
        foreach (TodoLine line in TodoParsing.Parse(notes, now).OrderBy(l => l.NoteId).ThenBy(l => l.LineIndex))
        {
            (Guid, string) key = (line.NoteId, line.Text);
            int occurrence = seen.TryGetValue(key, out int count) ? count : 0;
            seen[key] = occurrence + 1;

            if (line.Checked || FireTime(line, dateOnlyTime) is not { } at || at <= localNow)
            {
                continue;
            }

            planned.Add(new Reminder(
                IdFor(line.NoteId, line.Text, occurrence),
                at,
                line.Text,
                string.Format(CultureInfo.CurrentCulture, AppStrings.ReminderBodyFormat, line.NoteTitle, line.DueLabel),
                line.Date,
                line.NoteId));
        }

        return [.. planned
            .OrderBy(r => r.At)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Take(capacity)];
    }

    /// <summary>
    /// What to tell the platform to get from <paramref name="previous"/> (what was scheduled last
    /// time) to <paramref name="desired"/>: cancel what is gone, schedule what is new or changed,
    /// leave the rest alone.
    /// </summary>
    public static ReminderChanges Diff(
        IEnumerable<Reminder> previous, IReadOnlyList<Reminder> desired, string channelName, string channelDescription)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(desired);

        var before = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Reminder reminder in previous)
        {
            before[reminder.Id] = reminder.Fingerprint;
        }

        var wanted = new HashSet<string>(desired.Select(r => r.Id), StringComparer.Ordinal);
        return new ReminderChanges(
            [.. desired.Where(r => !before.TryGetValue(r.Id, out string? fingerprint) || fingerprint != r.Fingerprint)],
            [.. before.Keys.Where(id => !wanted.Contains(id)).Order(StringComparer.Ordinal)],
            channelName,
            channelDescription);
    }

    /// <summary>
    /// "todo-" and 16 hex digits of a hash over the note, the to-do's text (without its due stamp)
    /// and which occurrence of that text in the note it is.
    /// </summary>
    /// <remarks>
    /// Not the line number: typing a line above a to-do would then cancel and reschedule it, and
    /// two to-dos swapping places would swap reminders. Not including the due stamp either, so
    /// moving a to-do to another time replaces its reminder rather than adding a second.
    /// </remarks>
    public static string IdFor(Guid noteId, string text, int occurrence)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(noteId.ToString("N"), "\n", text, "\n", occurrence.ToString(CultureInfo.InvariantCulture))));
        return "todo-" + Convert.ToHexStringLower(hash, 0, 8);
    }

    /// <summary>The last eight hex digits of an id as an int; any other string hashes the same way every time.</summary>
    public static int RequestCodeFor(string id)
    {
        if (id.Length >= 8 && int.TryParse(id.AsSpan(id.Length - 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return BitConverter.ToInt32(hash, 0);
    }
}
