using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Daynote.Mobile.Reminders;

/// <summary>
/// Which to-dos get a reminder and when: the pure half of to-do reminders, with no clock, store or
/// platform of its own.
/// </summary>
/// <remarks>
/// Driven by to-do entities only (ReminderPlanner.Agenda.cs). Note bodies are plain text and never
/// remind: a <c>-[ ]</c> line in one is just a line.
/// </remarks>
public static partial class ReminderPlanner
{
    /// <summary>When a to-do with a date and no time reminds unless the user picked another time: 09:00.</summary>
    public static readonly TimeSpan DefaultDateOnlyTime = new(9, 0, 0);

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
