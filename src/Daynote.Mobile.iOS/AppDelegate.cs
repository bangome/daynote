using Avalonia;
using Avalonia.iOS;
using Foundation;
using Daynote.Mobile.iOS.Platform;

namespace Daynote.Mobile.iOS;

/// <summary>
/// The iOS application delegate. Avalonia owns the single window; this supplies the platform
/// services and forwards the lifecycle moments the app actually cares about.
/// </summary>
[Register(nameof(AppDelegate))]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    /// <summary>Held here: the notification center keeps only a weak reference to its delegate.</summary>
    private static readonly IosReminderDelegate ReminderDelegate = new();

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.Platform = IosPlatformServices.Create();

        // Before launch finishes, or a tap on a reminder that launched the app never arrives.
        UserNotifications.UNUserNotificationCenter.Current.Delegate = ReminderDelegate;

        // Deactivation is the last moment iOS guarantees the app runs, so the open note is flushed
        // there. WillTerminate is not a substitute: iOS kills a suspended app without calling it.
        // Avalonia surfaces it as an event rather than a method to override, because the modern
        // binding makes the UIApplicationDelegate members interface exports, not virtuals.
        ((IAvaloniaAppDelegate)this).Deactivated += (_, _) =>
        {
            if (Avalonia.Application.Current is App app)
            {
                app.FlushAsync().GetAwaiter().GetResult();
            }
        };

        // Coming back to the foreground is when other devices' notes are most likely waiting.
        ((IAvaloniaAppDelegate)this).Activated += (_, _) =>
        {
            if (Avalonia.Application.Current is App app)
            {
                app.NotifyResumed();
            }
        };

        return base.CustomizeAppBuilder(builder).WithInterFont();
    }
}
