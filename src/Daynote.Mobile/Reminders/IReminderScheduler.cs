using System.Globalization;
using Daynote.Core.Domain;

namespace Daynote.Mobile.Reminders;

/// <summary>
/// The device's local notification service, as each head provides it: <c>UNUserNotificationCenter</c>
/// on iOS, <c>AlarmManager</c> and a broadcast receiver on Android.
/// </summary>
/// <remarks>
/// Deliberately thin. Which to-dos get a reminder, at what time, and what changed since the last run
/// are decided in shared code (<see cref="ReminderPlanner"/>, <see cref="ReminderCoordinator"/>) so
/// they are unit tested once; a head only schedules, cancels and asks for permission.
/// </remarks>
public interface IReminderScheduler
{
    /// <summary>
    /// How many reminders the platform holds at once. iOS keeps at most 64 pending local
    /// notifications per app and silently drops the rest, so the nearest ones are scheduled and the
    /// set is topped up whenever the app runs.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// What the OS says about notifications for this app. Android 13+ cannot tell "never asked" from
    /// "denied" and answers <see cref="ReminderPermission.NotDetermined"/> for both; the coordinator
    /// remembers whether it already asked.
    /// </summary>
    Task<ReminderPermission> GetPermissionAsync();

    /// <summary>Shows the system prompt. Answers <see cref="ReminderPermission.NotDetermined"/> when it could not be shown.</summary>
    Task<ReminderPermission> RequestPermissionAsync();

    /// <summary>
    /// Cancels <see cref="ReminderChanges.Cancel"/> and schedules (or replaces, by id)
    /// <see cref="ReminderChanges.Schedule"/>. Already delivered notifications are left alone.
    /// </summary>
    Task ApplyAsync(ReminderChanges changes);

    /// <summary>Opens this app's page in the system notification settings.</summary>
    void OpenSystemSettings();

    /// <summary>
    /// Whether reminders fire on the minute. Android 12+ needs the user's "Alarms &amp; reminders"
    /// grant for that and is otherwise up to an hour late; iOS and older Android always are exact.
    /// Read fresh each time, since the user changes it in the system settings.
    /// </summary>
    ExactAlarmState ExactAlarms { get; }

    /// <summary>Opens the system page where the user allows exact alarms (Android only).</summary>
    void OpenExactAlarmSettings();
}

/// <summary>Whether the platform fires reminders exactly.</summary>
public enum ExactAlarmState
{
    /// <summary>Always exact (iOS, Android before 12): there is nothing to ask for.</summary>
    NotApplicable,
    Allowed,
    NotAllowed,
}

/// <summary>What the OS allows.</summary>
public enum ReminderPermission
{
    NotDetermined,
    Granted,
    Denied,
}

/// <summary>
/// One local notification for one to-do.
/// </summary>
/// <param name="Id">
/// Deterministic per (note, to-do): see <see cref="ReminderPlanner.IdFor"/>. Scheduling the same id
/// again replaces the earlier one on both platforms, which is what makes a reschedule idempotent.
/// </param>
/// <param name="At">Local wall-clock time, <see cref="DateTimeKind.Unspecified"/>; the head converts it in the zone the device is in when it schedules.</param>
/// <param name="Title">The to-do's text.</param>
/// <param name="Body">"note title · due label".</param>
/// <param name="Date">The note's day, which a tap opens.</param>
/// <param name="NoteId">The note a tap opens in the editor.</param>
/// <param name="ItemId">
/// The to-do it is for — the series, for an occurrence of a rule — so the notification's 완료 can
/// complete it without the app on screen (Apple Watch design §05). Null on a reminder that cannot
/// be completed from the notification.
/// </param>
/// <param name="Occurrence">Which occurrence of <paramref name="ItemId"/>, as its <c>RECURRENCE-ID</c>, or null.</param>
public sealed record Reminder(
    string Id, DateTime At, string Title, string Body, LocalDate Date, Guid NoteId,
    Guid? ItemId = null, string? Occurrence = null)
{
    /// <summary>Everything a platform shows or fires on; a change in any of it means "schedule again".</summary>
    public string Fingerprint => string.Join(
        '\u001f',
        At.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
        Title,
        Body,
        Date.ToString(),
        NoteId.ToString("N"),
        ItemId?.ToString("N") ?? string.Empty,
        Occurrence ?? string.Empty);

    /// <summary>
    /// A stable 32-bit number for the id, for the APIs that want an int (Android's PendingIntent
    /// request code and notification id). Taken from the id's hash digits, so it never changes.
    /// </summary>
    public int RequestCode => ReminderPlanner.RequestCodeFor(Id);
}

/// <summary>One reconciliation for a head to carry out.</summary>
/// <param name="Schedule">Reminders to add, or to replace because something about them changed.</param>
/// <param name="Cancel">Ids that are no longer wanted: checked, edited away, moved past, deleted, or switched off.</param>
/// <param name="ChannelName">The Android channel's name in the current language (iOS has no channels).</param>
/// <param name="ChannelDescription">The channel's description, likewise.</param>
public sealed record ReminderChanges(
    IReadOnlyList<Reminder> Schedule,
    IReadOnlyList<string> Cancel,
    string ChannelName,
    string ChannelDescription)
{
    public bool IsEmpty => Schedule.Count == 0 && Cancel.Count == 0;
}
