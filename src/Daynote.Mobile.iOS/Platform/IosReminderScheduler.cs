using Daynote.Mobile.Reminders;
using Foundation;
using UIKit;
using UserNotifications;

namespace Daynote.Mobile.iOS.Platform;

/// <summary>
/// To-do reminders on iOS: one pending <see cref="UNNotificationRequest"/> per reminder, keyed by the
/// reminder's id, on a calendar trigger at the exact local time.
/// </summary>
/// <remarks>
/// iOS keeps at most 64 pending local notifications per app and silently drops the rest, so the
/// capacity is 64: the shared planner hands over the nearest 64 and tops the set up on every pass
/// (each start, resume and edit). Adding a request with an id that is already pending replaces it,
/// which is what lets a pass reschedule a changed reminder in place. Local notifications need no
/// Info.plist key and no entitlement; only the user's permission.
/// </remarks>
internal sealed class IosReminderScheduler : IReminderScheduler
{
    internal const string DateKey = "daynote.reminder.date";
    internal const string NoteKey = "daynote.reminder.note";

    public int Capacity => 64;

    public async Task<ReminderPermission> GetPermissionAsync()
    {
        UNNotificationSettings settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync().ConfigureAwait(true);
        return settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.NotDetermined => ReminderPermission.NotDetermined,
            UNAuthorizationStatus.Denied => ReminderPermission.Denied,
            _ => ReminderPermission.Granted,
        };
    }

    public async Task<ReminderPermission> RequestPermissionAsync()
    {
        try
        {
            await UNUserNotificationCenter.Current
                .RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound)
                .ConfigureAwait(true);
        }
        catch (NSErrorException)
        {
            return ReminderPermission.NotDetermined;
        }

        return await GetPermissionAsync().ConfigureAwait(true);
    }

    public async Task ApplyAsync(ReminderChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        UNUserNotificationCenter center = UNUserNotificationCenter.Current;
        if (changes.Cancel.Count > 0)
        {
            center.RemovePendingNotificationRequests([.. changes.Cancel]);
        }

        foreach (Reminder reminder in changes.Schedule)
        {
            var content = new UNMutableNotificationContent
            {
                Title = reminder.Title,
                Body = reminder.Body,
                Sound = UNNotificationSound.Default,
                UserInfo = NSDictionary.FromObjectsAndKeys(
                    [new NSString(reminder.Date.ToString()), new NSString(reminder.NoteId.ToString("D"))],
                    [new NSString(DateKey), new NSString(NoteKey)]),
            };

            // Date components rather than an interval: the reminder stays at 14:00 local even if
            // the phone changes zone or the clock is set in between.
            DateTime at = reminder.At;
            var when = new NSDateComponents
            {
                Year = at.Year,
                Month = at.Month,
                Day = at.Day,
                Hour = at.Hour,
                Minute = at.Minute,
            };
            UNCalendarNotificationTrigger trigger = UNCalendarNotificationTrigger.CreateTrigger(when, repeats: false);
            UNNotificationRequest request = UNNotificationRequest.FromIdentifier(reminder.Id, content, trigger);
            try
            {
                await center.AddNotificationRequestAsync(request).ConfigureAwait(true);
            }
            catch (NSErrorException exception)
            {
                System.Diagnostics.Trace.TraceWarning($"Scheduling reminder {reminder.Id} failed: {exception.Message}");
            }
        }
    }

    /// <summary>Calendar triggers fire on the minute; there is nothing to ask for.</summary>
    public ExactAlarmState ExactAlarms => ExactAlarmState.NotApplicable;

    public void OpenExactAlarmSettings()
    {
    }

    public void OpenSystemSettings()
    {
        // iOS 16 opens the app's notification page directly; before that, the app's settings page.
        string target = OperatingSystem.IsIOSVersionAtLeast(16)
            ? UIApplication.OpenNotificationSettingsUrl.ToString()
            : UIApplication.OpenSettingsUrlString;
        if (NSUrl.FromString(target) is { } url)
        {
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
        }
    }
}

/// <summary>
/// Shows a reminder that fires while Daynote is open (iOS otherwise drops it silently), and turns
/// a tap on one into "open this note".
/// </summary>
/// <remarks>
/// Set as the center's delegate during launch, before it finishes, which is what iOS requires for a
/// tap that launched the app to be delivered here at all.
/// </remarks>
internal sealed class IosReminderDelegate : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(
        UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler) =>
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);

    public override void DidReceiveNotificationResponse(
        UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
    {
        NSDictionary info = response.Notification.Request.Content.UserInfo;
        string? date = info[IosReminderScheduler.DateKey]?.ToString();
        string? note = info[IosReminderScheduler.NoteKey]?.ToString();
        // UIKit's main queue rather than Avalonia's dispatcher: a tap that launches the app arrives
        // before Avalonia has started, and touching Dispatcher.UIThread then crashes its start-up.
        NSRunLoop.Main.BeginInvokeOnMainThread(() => App.OpenReminder(date, note));
        completionHandler();
    }
}
