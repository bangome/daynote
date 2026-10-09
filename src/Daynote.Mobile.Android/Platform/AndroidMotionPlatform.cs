using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Views;
using AndroidX.Core.Content;
using AndroidX.Window.Java.Layout;
using AndroidX.Window.Layout;
using Daynote.Mobile.Platform;
using Daynote.Motion;

namespace Daynote.Mobile.Android.Platform;

/// <summary>
/// Reduced motion and haptics on Android (motion spec §04, M1-M3).
/// </summary>
/// <remarks>
/// <para>
/// Android has no "reduce motion" switch of its own; the spec reads the developer-options and
/// accessibility "remove animations" setting, which sets the animator duration scale to 0. It is
/// watched, so turning it on while the app runs takes effect at once.
/// </para>
/// <para>
/// The haptics are <c>performHapticFeedback</c> on the window's view, which follows the system's
/// touch-feedback setting by itself. SEGMENT_TICK (M1) is Android 14; CONFIRM (M2, M3) is Android
/// 11. Older versions get the nearest constant they have.
/// </para>
/// </remarks>
internal sealed class AndroidMotionPlatform : IMotionPlatform
{
    private readonly Context _context;
    private readonly Func<global::Android.App.Activity?> _activity;

    public AndroidMotionPlatform(Context context, Func<global::Android.App.Activity?> activity)
    {
        _context = context;
        _activity = activity;
        if (Settings.Global.GetUriFor(Settings.Global.AnimatorDurationScale) is { } uri)
        {
            context.ContentResolver?.RegisterContentObserver(uri, false, new ScaleObserver(this));
        }
    }

    public bool PrefersReducedMotion =>
        Settings.Global.GetFloat(_context.ContentResolver, Settings.Global.AnimatorDurationScale, 1f) == 0f;

    public event EventHandler? PreferenceChanged;

    public void Play(HapticKind kind)
    {
        if (_activity()?.Window?.DecorView is not { } view)
        {
            return;
        }

        FeedbackConstants constant = kind switch
        {
            HapticKind.Selection => OperatingSystem.IsAndroidVersionAtLeast(34) ? FeedbackConstants.SegmentTick : FeedbackConstants.ClockTick,
            _ => OperatingSystem.IsAndroidVersionAtLeast(30) ? FeedbackConstants.Confirm : FeedbackConstants.VirtualKey,
        };
        view.PerformHapticFeedback(constant);
    }

    private sealed class ScaleObserver(AndroidMotionPlatform owner) : ContentObserver(new Handler(Looper.MainLooper!))
    {
        public override void OnChange(bool selfChange) => owner.PreferenceChanged?.Invoke(owner, EventArgs.Empty);
    }
}

/// <summary>
/// A foldable's hinge and an attached keyboard, on Android (Foldables §01, Tablet §02).
/// </summary>
/// <remarks>
/// <para>
/// The hinge comes from Jetpack WindowManager's <c>FoldingFeature</c>, through the Java callback
/// adapter rather than the Kotlin flow, which C# cannot collect without a coroutine of its own. It
/// is reported only while it separates the window - a Fold opened flat, a Flip in tabletop - and in
/// the window's own pixels, which are turned into points here.
/// </para>
/// <para>
/// The keyboard is the configuration's: a hardware keyboard that is attached and not hidden. The
/// activity handles keyboard configuration changes itself, so it calls <see cref="Refresh"/>.
/// </para>
/// </remarks>
internal sealed class AndroidDeviceShape : IDeviceShape
{
    private global::Android.App.Activity? _activity;
    private WindowInfoTrackerCallbackAdapter? _tracker;
    private LayoutListener? _listener;
    private Avalonia.Rect? _hinge;

    public Avalonia.Rect? Hinge => _hinge;

    public bool HasHardwareKeyboard =>
        _activity?.Resources?.Configuration is { } config &&
        config.Keyboard != global::Android.Content.Res.KeyboardType.Nokeys &&
        config.HardKeyboardHidden == global::Android.Content.Res.HardKeyboardHidden.No;

    public event EventHandler? Changed;

    /// <summary>Starts following <paramref name="activity"/>'s window; from its OnStart.</summary>
    public void Attach(global::Android.App.Activity activity)
    {
        Detach();
        _activity = activity;
        _tracker = new WindowInfoTrackerCallbackAdapter(WindowInfoTracker.Companion.GetOrCreate(activity));
        _listener = new LayoutListener(this);
        _tracker.AddWindowLayoutInfoListener(activity, ContextCompat.GetMainExecutor(activity)!, _listener);
        Refresh();
    }

    /// <summary>Stops; from OnStop, since the listener holds the activity.</summary>
    public void Detach()
    {
        if (_tracker is not null && _listener is not null)
        {
            _tracker.RemoveWindowLayoutInfoListener(_listener);
        }

        _tracker = null;
        _listener = null;
        _activity = null;
    }

    /// <summary>The configuration changed (a keyboard came or went).</summary>
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    private void OnLayout(WindowLayoutInfo info)
    {
        float density = _activity?.Resources?.DisplayMetrics?.Density ?? 1f;

        // The bounds are in the window's pixels; the app draws in its content view, which need not
        // start at the window's corner (a cutout, a system bar on the left in landscape).
        int[] origin = [0, 0];
        _activity?.FindViewById(global::Android.Resource.Id.Content)?.GetLocationInWindow(origin);

        _hinge = null;
        foreach (IDisplayFeature feature in info.DisplayFeatures)
        {
            if (feature is IFoldingFeature { IsSeparating: true } fold && fold.Bounds is { } bounds)
            {
                _hinge = new Avalonia.Rect(
                    (bounds.Left - origin[0]) / density,
                    (bounds.Top - origin[1]) / density,
                    bounds.Width() / density,
                    bounds.Height() / density);
            }
        }

        Refresh();
    }

    private sealed class LayoutListener(AndroidDeviceShape owner) : Java.Lang.Object, AndroidX.Core.Util.IConsumer
    {
        public void Accept(Java.Lang.Object? value)
        {
            if (value is WindowLayoutInfo info)
            {
                owner.OnLayout(info);
            }
        }
    }
}
