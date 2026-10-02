using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Daynote.Mobile.Reminders;

namespace Daynote.Mobile.Android.Platform;

/// <summary>
/// A reminder's alarm went off: post its notification. A tap brings <see cref="MainActivity"/>
/// forward with the note's date and id, and the shell opens that note.
/// </summary>
/// <remarks>
/// Not exported: only the app's own alarms reach it. Everything the notification shows travels in
/// the alarm's extras, so posting needs neither the database nor the UI.
/// </remarks>
[BroadcastReceiver(Exported = false)]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null
            || context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && context.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            return;
        }

        string id = intent.Data?.SchemeSpecificPart ?? string.Empty;
        string title = intent.GetStringExtra(AndroidReminderScheduler.ExtraTitle) ?? string.Empty;
        string body = intent.GetStringExtra(AndroidReminderScheduler.ExtraBody) ?? string.Empty;
        int code = ReminderPlanner.RequestCodeFor(id);

        // The channel normally exists already, with its description; only a missing one (the app's
        // data cleared since the alarm was armed) is created here, so nothing is overwritten.
        if (manager.GetNotificationChannel(AndroidReminderScheduler.ChannelId) is null)
        {
            AndroidReminderScheduler.EnsureChannel(
                context, intent.GetStringExtra(AndroidReminderScheduler.ExtraChannel) ?? string.Empty, string.Empty);
        }

        var open = new Intent(context, typeof(MainActivity));
        open.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        open.PutExtra(AndroidReminderScheduler.ExtraDate, intent.GetStringExtra(AndroidReminderScheduler.ExtraDate));
        open.PutExtra(AndroidReminderScheduler.ExtraNote, intent.GetStringExtra(AndroidReminderScheduler.ExtraNote));
        PendingIntent tap = PendingIntent.GetActivity(
            context, code, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

        using var builder = new Notification.Builder(context, AndroidReminderScheduler.ChannelId);
        builder
            .SetSmallIcon(Resource.Drawable.ic_stat_reminder)!
            .SetContentTitle(title)!
            .SetContentText(body)!
            .SetStyle(new Notification.BigTextStyle().BigText(body))!
            .SetCategory(Notification.CategoryReminder)!
            .SetAutoCancel(true)!
            .SetContentIntent(tap);
        manager.Notify(id, 0, builder.Build());
    }
}

/// <summary>
/// Re-arms the reminders after the alarms were lost or went stale: a reboot clears every alarm, an
/// app update clears this app's, and a clock or time-zone change moves the instant a local
/// "14:00" stands for. Also when the user allows exact alarms, so the next ones are exact.
/// </summary>
/// <remarks>
/// Kept light, as a receiver must be: it reads the device's <c>reminders.json</c> (what the last
/// pass scheduled) and sets alarms for the ones still ahead. No database, no network, no UI; the
/// next time the app runs, a full pass reconciles against the notes again. Not exported: these
/// broadcasts come from the system, which reaches a non-exported receiver.
/// </remarks>
[BroadcastReceiver(Exported = false)]
[IntentFilter([
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced,
    Intent.ActionTimeChanged,
    Intent.ActionTimezoneChanged,
    AlarmManager.ActionScheduleExactAlarmPermissionStateChanged,
])]
public sealed class ReminderRescheduleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        try
        {
            // .NET caches the zone; after a zone change the cached one is the old one.
            TimeZoneInfo.ClearCachedData();
            DateTime now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
            ReminderState state = ReminderStateStore.InFolder(AndroidPlatformServices.ResolveDataRoot(context)).Load();
            if (state.Scheduled.Count > 0)
            {
                AndroidReminderScheduler.EnsureChannel(context, state.ChannelName, state.ChannelDescription);
            }

            foreach (Reminder reminder in state.Scheduled.Where(r => r.At > now))
            {
                AndroidReminderScheduler.Arm(context, reminder, state.ChannelName);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            global::Android.Util.Log.Warn("Daynote", $"Re-arming reminders failed: {exception}");
        }
    }
}
