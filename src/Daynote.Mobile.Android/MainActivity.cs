using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using Avalonia.Android;

namespace Daynote.Mobile.Android;

/// <summary>
/// The one activity. Avalonia draws the whole UI into it, so there is no fragment stack and no
/// per-screen activity: navigation is the shell view model's business.
/// </summary>
/// <remarks>
/// <para>
/// <c>ConfigurationChanges</c> lists everything the activity handles itself. Without it Android
/// destroys and recreates it on a rotation or a keyboard change, which would tear down the Avalonia
/// surface and lose the editor's caret and any unflushed draft.
/// </para>
/// <para>
/// <c>AdjustResize</c> is what makes the note editor usable: the window shrinks when the keyboard
/// opens, so the line being typed stays above it instead of behind it.
/// </para>
/// </remarks>
[Activity(
    Label = "Daynote",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density | ConfigChanges.ScreenLayout)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// The activity currently on screen, or null while the app is between activities.
    /// </summary>
    /// <remarks>
    /// The platform services are built by <see cref="MainApplication"/>, which has no activity, so
    /// the pieces that need one read it from here. A field rather than a captured reference because
    /// Android may destroy and recreate the activity; holding the old one would leak it and hand
    /// Custom Tabs a dead context.
    /// </remarks>
    internal static MainActivity? Current { get; private set; }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);

        // Launched by tapping a to-do reminder or a widget: the shell acts once the day has loaded.
        if (!OpenReminder(Intent))
        {
            OpenWidget(Intent);
        }

        // With three-button navigation Android lays a translucent grey scrim over the button bar for
        // contrast, which read as the bottom of the app being dimmed. The app paints that strip in its
        // own background colour (MainView), so the scrim has nothing to add.
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && Window is { } window)
        {
            window.NavigationBarContrastEnforced = false;
        }

        // The system back gesture closes the editor first; only then does it leave the app.
        BackRequested += (_, e) =>
        {
            if (Avalonia.Application.Current is App app)
            {
                e.Handled = app.TryGoBackAsync().GetAwaiter().GetResult();
            }
        };
    }

    /// <summary>
    /// Keeps the system bars clear of the platform's translucent scrim.
    /// </summary>
    /// <remarks>
    /// Avalonia goes edge to edge by setting the translucent status and navigation flags. Android 15
    /// ignores them, but 14 and earlier — most Galaxy phones still in use — draw a dark grey scrim over
    /// both bars for them, which made the top and bottom of the app look cut off. The window still
    /// draws behind the bars without them (Avalonia also takes over insets fitting); clearing them
    /// here, whenever they come back, leaves the bars the app's own colour.
    /// </remarks>
    public override void OnWindowAttributesChanged(WindowManagerLayoutParams? @params)
    {
        base.OnWindowAttributesChanged(@params);

        // Android 15 and later draw every app edge to edge and ignore all of this. The change is
        // posted rather than made here: changing the window from inside its own attributes callback
        // re-entered the window manager and left the app not responding on Android 14.
        if (OperatingSystem.IsAndroidVersionAtLeast(35) || @params is null || _barFixPending ||
            ((@params.Flags & TranslucentBars) == 0 && BarsAreTransparent()))
        {
            return;
        }

        _barFixPending = true;
        Window?.DecorView.Post(ClearBarScrim);
    }

    private const WindowManagerFlags TranslucentBars = WindowManagerFlags.TranslucentStatus | WindowManagerFlags.TranslucentNavigation;

    private bool _barFixPending;

    private bool BarsAreTransparent() =>
        OperatingSystem.IsAndroidVersionAtLeast(35) || Window is not { } window ||
        (window.StatusBarColor == global::Android.Graphics.Color.Transparent.ToArgb() &&
         window.NavigationBarColor == global::Android.Graphics.Color.Transparent.ToArgb());

    private void ClearBarScrim()
    {
        _barFixPending = false;
        if (OperatingSystem.IsAndroidVersionAtLeast(35) || Window is not { } window)
        {
            return;
        }

        if ((window.Attributes?.Flags & TranslucentBars) != 0)
        {
            window.ClearFlags(TranslucentBars);
            window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);
        }

        // Without the flags the bars show whatever colour was left on them (white); transparent lets
        // the app's own background, which already runs behind them, show through.
        if (!BarsAreTransparent())
        {
            window.SetStatusBarColor(global::Android.Graphics.Color.Transparent);
            window.SetNavigationBarColor(global::Android.Graphics.Color.Transparent);
        }
    }

    /// <summary>
    /// The sign-in redirect. The scheme is registered by <see cref="Platform.AuthCallbackActivity"/>,
    /// which forwards it here as a new intent because this activity is <c>SingleTask</c>.
    /// </summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (OpenReminder(intent) || OpenWidget(intent))
        {
            return;
        }

        if (intent?.Data is { } data)
        {
            Platform.AndroidAuthSession.Complete(new Uri(data.ToString()!));
        }
    }

    /// <summary>A tapped to-do reminder carries its note's date and id; hands them to the app.</summary>
    private static bool OpenReminder(Intent? intent)
    {
        if (intent?.GetStringExtra(Platform.AndroidReminderScheduler.ExtraNote) is not { } note)
        {
            return false;
        }

        string? date = intent.GetStringExtra(Platform.AndroidReminderScheduler.ExtraDate);
        intent.RemoveExtra(Platform.AndroidReminderScheduler.ExtraNote);
        intent.RemoveExtra(Platform.AndroidReminderScheduler.ExtraDate);
        App.OpenReminder(date, note);
        return true;
    }

    /// <summary>A tap on a home-screen widget: today, a new note, or a new note with the @ bar up.</summary>
    private static bool OpenWidget(Intent? intent)
    {
        if (intent?.GetStringExtra(Platform.Widgets.DaynoteWidgets.ExtraLaunch) is not { } name
            || !Enum.TryParse(name, out ViewModels.WidgetLaunch launch))
        {
            return false;
        }

        // Taken off the intent, so recreating the activity does not open a second note.
        intent.RemoveExtra(Platform.Widgets.DaynoteWidgets.ExtraLaunch);
        intent.SetData(null);
        App.OpenFromWidget(launch);
        return true;
    }

    private const int NotificationPermissionRequest = 7301;

    private TaskCompletionSource<bool>? _notificationPermission;

    /// <summary>
    /// Shows Android 13's notification prompt and answers whether it was granted. The reminder
    /// scheduler calls it the first time there is a to-do to remind about.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    internal Task<bool> RequestNotificationPermissionAsync()
    {
        _notificationPermission?.TrySetResult(false);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _notificationPermission = pending;
        RequestPermissions([global::Android.Manifest.Permission.PostNotifications], NotificationPermissionRequest);
        return pending.Task;
    }

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, [global::Android.Runtime.GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == NotificationPermissionRequest && _notificationPermission is { } pending)
        {
            _notificationPermission = null;
            pending.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }
    }

    /// <summary>
    /// Ends a sign-in the user walked away from.
    /// </summary>
    /// <remarks>
    /// Dismissing the Custom Tab - back gesture, swipe, the tab's own X - leaves no redirect
    /// behind, so nothing else would ever resolve the wait and the account card sat with its
    /// button greyed out until the app was killed. Coming back to this activity is the one signal
    /// Android gives that the tab is gone.
    ///
    /// Safe to call after a successful sign-in too: the redirect arrives through
    /// <c>OnNewIntent</c>, which runs before <c>OnResume</c> and has already taken the pending
    /// completion, so there is nothing left here to abandon.
    ///
    /// It is also the moment to pick up what other devices wrote while this one was away.
    /// </remarks>
    protected override void OnResume()
    {
        base.OnResume();
        Platform.AndroidAuthSession.Abandon();
        if (Avalonia.Application.Current is App app)
        {
            app.NotifyResumed();
        }
    }

    /// <summary>
    /// Backgrounding is the last moment an Android app is guaranteed to run, so the open note is
    /// flushed here rather than in <c>OnDestroy</c>, which may never be called.
    /// </summary>
    protected override void OnStop()
    {
        if (Avalonia.Application.Current is App app)
        {
            app.FlushAsync().GetAwaiter().GetResult();
        }

        base.OnStop();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        base.OnDestroy();
    }
}
