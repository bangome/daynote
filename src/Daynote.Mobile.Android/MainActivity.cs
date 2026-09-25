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
    /// The sign-in redirect. The scheme is registered by <see cref="Platform.AuthCallbackActivity"/>,
    /// which forwards it here as a new intent because this activity is <c>SingleTask</c>.
    /// </summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent?.Data is { } data)
        {
            Platform.AndroidAuthSession.Complete(new Uri(data.ToString()!));
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
