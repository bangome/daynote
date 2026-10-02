using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Daynote.Mobile.Reminders;

namespace Daynote.Mobile.Android.Platform;

/// <summary>
/// To-do reminders on Android: one <see cref="AlarmManager"/> alarm per reminder, firing
/// <see cref="ReminderAlarmReceiver"/>, which posts the notification.
/// </summary>
/// <remarks>
/// <para>
/// Exact when the app may (<c>SCHEDULE_EXACT_ALARM</c>, which Android 14 no longer grants on install
/// and the user can switch on under "Alarms &amp; reminders"), otherwise
/// <c>setAndAllowWhileIdle</c>: inexact, so in Doze a reminder can arrive some minutes late. Not
/// <c>USE_EXACT_ALARM</c>, which Play reserves for alarm-clock and calendar apps.
/// </para>
/// <para>
/// Each alarm's intent carries the reminder's id as its data URI, so two reminders never share a
/// PendingIntent, and cancelling one rebuilds exactly its intent without needing its extras.
/// </para>
/// </remarks>
internal sealed class AndroidReminderScheduler(Context context, Func<Activity?> currentActivity) : IReminderScheduler
{
    internal const string ChannelId = "todo-reminders";

    internal const string ExtraTitle = "daynote.reminder.title";
    internal const string ExtraBody = "daynote.reminder.body";
    internal const string ExtraDate = "daynote.reminder.date";
    internal const string ExtraNote = "daynote.reminder.note";
    internal const string ExtraChannel = "daynote.reminder.channel";

    /// <summary>
    /// Far under the 500 alarms Android allows an app, and far more than anyone has due at once;
    /// the rest are armed as the nearest ones fire and the app runs again.
    /// </summary>
    public int Capacity => 128;

    public Task<ReminderPermission> GetPermissionAsync() => Task.FromResult(PermissionOf(context));

    public async Task<ReminderPermission> RequestPermissionAsync()
    {
        // Before Android 13 notifications need no runtime permission: on unless the user turned them off.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return PermissionOf(context);
        }

        if (currentActivity() is not MainActivity activity)
        {
            return ReminderPermission.NotDetermined;
        }

        bool granted = await activity.RequestNotificationPermissionAsync().ConfigureAwait(true);
        return granted ? PermissionOf(context) : ReminderPermission.Denied;
    }

    public Task ApplyAsync(ReminderChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        EnsureChannel(context, changes.ChannelName, changes.ChannelDescription);
        foreach (string id in changes.Cancel)
        {
            Cancel(context, id);
        }

        foreach (Reminder reminder in changes.Schedule)
        {
            Arm(context, reminder, changes.ChannelName);
        }

        return Task.CompletedTask;
    }

    public void OpenSystemSettings()
    {
        try
        {
            using var intent = new Intent(global::Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
        }
    }

    public ExactAlarmState ExactAlarms =>
        !OperatingSystem.IsAndroidVersionAtLeast(31) ? ExactAlarmState.NotApplicable
        : (context.GetSystemService(Context.AlarmService) as AlarmManager)?.CanScheduleExactAlarms() == true
            ? ExactAlarmState.Allowed
            : ExactAlarmState.NotAllowed;

    /// <summary>
    /// The "Alarms &amp; reminders" page for this app. Granting it sends
    /// SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED, which <see cref="ReminderRescheduleReceiver"/>
    /// answers by re-arming every alarm exact.
    /// </summary>
    public void OpenExactAlarmSettings()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return;
        }

        try
        {
            using var intent = new Intent(
                global::Android.Provider.Settings.ActionRequestScheduleExactAlarm,
                global::Android.Net.Uri.Parse("package:" + context.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
        }
    }

    /// <summary>
    /// Granted when notifications are on; on Android 13+ "not determined" while the runtime
    /// permission has not been granted, since the platform cannot say whether it was ever asked.
    /// </summary>
    private static ReminderPermission PermissionOf(Context context)
    {
        if (context.GetSystemService(Context.NotificationService) is NotificationManager manager && manager.AreNotificationsEnabled())
        {
            return ReminderPermission.Granted;
        }

        return OperatingSystem.IsAndroidVersionAtLeast(33)
            && context.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted
                ? ReminderPermission.NotDetermined
                : ReminderPermission.Denied;
    }

    /// <summary>Creates the channel, or renames it after a language switch (the call updates an existing one).</summary>
    internal static void EnsureChannel(Context context, string name, string description)
    {
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        using var channel = new NotificationChannel(ChannelId, name.Length > 0 ? name : "Daynote", NotificationImportance.High)
        {
            Description = description,
        };
        manager.CreateNotificationChannel(channel);
    }

    /// <summary>Sets (or replaces) the alarm for one reminder. Also used by the reschedule receiver.</summary>
    internal static void Arm(Context context, Reminder reminder, string channelName)
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms)
        {
            return;
        }

        using Intent intent = AlarmIntent(context, reminder.Id);
        intent.PutExtra(ExtraTitle, reminder.Title);
        intent.PutExtra(ExtraBody, reminder.Body);
        intent.PutExtra(ExtraDate, reminder.Date.ToString());
        intent.PutExtra(ExtraNote, reminder.NoteId.ToString("D"));
        intent.PutExtra(ExtraChannel, channelName);
        PendingIntent pending = PendingIntent.GetBroadcast(
            context, reminder.RequestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

        // The wall-clock time in the zone the phone is in now; a zone change re-arms everything.
        DateTime at = reminder.At;
        long trigger = new DateTimeOffset(at, TimeZoneInfo.Local.GetUtcOffset(at)).ToUnixTimeMilliseconds();
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms())
            {
                alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, pending);
                return;
            }
        }
        catch (Java.Lang.SecurityException)
        {
            // Exact alarms were revoked between the check and the call; fall through to inexact.
        }

        alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, trigger, pending);
    }

    internal static void Cancel(Context context, string id)
    {
        using Intent intent = AlarmIntent(context, id);
        PendingIntent? pending = PendingIntent.GetBroadcast(
            context, ReminderPlanner.RequestCodeFor(id), intent, PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pending is null)
        {
            return;
        }

        (context.GetSystemService(Context.AlarmService) as AlarmManager)?.Cancel(pending);
        pending.Cancel();
    }

    private static Intent AlarmIntent(Context context, string id)
    {
        var intent = new Intent(context, typeof(ReminderAlarmReceiver));
        intent.SetData(global::Android.Net.Uri.Parse("daynote-reminder:" + id));
        return intent;
    }
}
