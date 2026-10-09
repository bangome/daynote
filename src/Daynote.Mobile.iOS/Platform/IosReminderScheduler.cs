using Daynote.App.Localization;
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
    internal const string ItemKey = "daynote.reminder.item";
    internal const string OccurrenceKey = "daynote.reminder.occurrence";

    /// <summary>A to-do's notification: 완료 and 30분 뒤 다시, which the watch shows too (Apple Watch design §05).</summary>
    internal const string TodoCategory = "daynote.todo";
    internal const string DoneAction = "daynote.done";
    internal const string SnoozeAction = "daynote.snooze";

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

        // Every pass, so the action titles follow a language switch. Neither action opens the app:
        // finishing a to-do or putting it off is the whole of what the user asked for.
        center.SetNotificationCategories(new NSSet<UNNotificationCategory>(UNNotificationCategory.FromIdentifier(
            TodoCategory,
            [
                UNNotificationAction.FromIdentifier(DoneAction, AppStrings.ReminderActionDone, UNNotificationActionOptions.None),
                UNNotificationAction.FromIdentifier(SnoozeAction, AppStrings.ReminderActionSnooze, UNNotificationActionOptions.None),
            ],
            [],
            UNNotificationCategoryOptions.None)));

        if (changes.Cancel.Count > 0)
        {
            center.RemovePendingNotificationRequests([.. changes.Cancel]);
        }

        foreach (Reminder reminder in changes.Schedule)
        {
            var info = new NSMutableDictionary
            {
                [DateKey] = new NSString(reminder.Date.ToString()),
                [NoteKey] = new NSString(reminder.NoteId.ToString("D")),
            };
            if (reminder.ItemId is { } item)
            {
                info[ItemKey] = new NSString(item.ToString("D"));
            }

            if (reminder.Occurrence is { } occurrence)
            {
                info[OccurrenceKey] = new NSString(occurrence);
            }

            var content = new UNMutableNotificationContent
            {
                Title = reminder.Title,
                Body = reminder.Body,
                Sound = UNNotificationSound.Default,
                UserInfo = info,
                // 완료 works through the App Group queue; without the group there is nowhere to
                // put it, so the buttons are not offered rather than offered and ignored.
                CategoryIdentifier = reminder.ItemId is not null && IosPlatformServices.Glance.Folder is not null
                    ? TodoCategory
                    : string.Empty,
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

    /// <summary>The 30분 뒤 다시 copies, whose to-do has since been ticked, deleted or switched off.</summary>
    public async Task SweepAsync(Func<Guid, string?, bool> stillOpen)
    {
        ArgumentNullException.ThrowIfNull(stillOpen);
        UNUserNotificationCenter center = UNUserNotificationCenter.Current;
        UNNotificationRequest[] pending = await center.GetPendingNotificationRequestsAsync().ConfigureAwait(true);
        string[] stale = [.. pending
            .Where(request => request.Identifier.EndsWith(SnoozeSuffix, StringComparison.Ordinal))
            .Where(request =>
            {
                NSDictionary info = request.Content.UserInfo;
                return !Guid.TryParse(info[ItemKey]?.ToString(), out Guid item)
                    || !stillOpen(item, info[OccurrenceKey]?.ToString());
            })
            .Select(static request => request.Identifier)];
        if (stale.Length > 0)
        {
            center.RemovePendingNotificationRequests(stale);
        }
    }

    /// <summary>The id a snoozed copy gets: the reminder's own, so the planner never mistakes it for one of its.</summary>
    internal const string SnoozeSuffix = ".snooze";

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

        if (response.ActionIdentifier == IosReminderScheduler.DoneAction)
        {
            // Into the queue widgets and the watch use, which the app drains through its store
            // (docs/APPLE_EXTENSIONS.md §4) — now if it is running, on its next start if not.
            // Written here and now, before the handler returns: iOS may suspend the app the moment
            // it does, and only the signal to drain needs the main thread.
            string? item = info[IosReminderScheduler.ItemKey]?.ToString();
            string? occurrence = info[IosReminderScheduler.OccurrenceKey]?.ToString();
            if (item is not null && date is not null)
            {
                IosPlatformServices.Glance.Enqueue(new Daynote.App.Glance.GlanceAction(
                    1,
                    Guid.NewGuid().ToString("D"),
                    Daynote.App.Glance.GlanceActionTypes.Complete,
                    DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                    ItemId: item,
                    SeriesId: occurrence is null ? null : item,
                    Occurrence: occurrence,
                    Date: date));
            }

            completionHandler();
            return;
        }

        if (response.ActionIdentifier == IosReminderScheduler.SnoozeAction)
        {
            // The same notification half an hour on. Its own id, so the next reminder pass, which
            // only knows the planned ones, neither replaces nor cancels it.
            UNNotificationContent original = response.Notification.Request.Content;
            UNNotificationRequest again = UNNotificationRequest.FromIdentifier(
                response.Notification.Request.Identifier + IosReminderScheduler.SnoozeSuffix,
                original,
                UNTimeIntervalNotificationTrigger.CreateTrigger(30 * 60, repeats: false));
            center.AddNotificationRequest(again, null);
            completionHandler();
            return;
        }

        // UIKit's main queue rather than Avalonia's dispatcher: a tap that launches the app arrives
        // before Avalonia has started, and touching Dispatcher.UIThread then crashes its start-up.
        NSRunLoop.Main.BeginInvokeOnMainThread(() => App.OpenReminder(date, note));
        completionHandler();
    }
}
