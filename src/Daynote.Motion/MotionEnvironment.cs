namespace Daynote.Motion;

/// <summary>The three touches the spec asks the phone to make.</summary>
public enum HapticKind
{
    /// <summary>M1, switching the @ reading: UISelectionFeedbackGenerator, SEGMENT_TICK.</summary>
    Selection,

    /// <summary>M2, a to-do made: <c>.success</c>; CONFIRM on Android.</summary>
    Success,

    /// <summary>M3, a tick: a light impact as the fill starts; CONFIRM on Android.</summary>
    Confirm,
}

/// <summary>
/// What motion needs from the OS: whether the user asked for less of it, and a way to make the
/// phone tap back. Supplied by each head; the shared code never asks the OS itself.
/// </summary>
/// <remarks>
/// Reduced motion is read where each platform keeps it (§04): UIAccessibility on iOS, the animator
/// duration scale on Android, NSWorkspace on the Mac. Haptics are independent of it - the spec keeps
/// them when motion is reduced and lets only the system's own haptic setting silence them.
/// </remarks>
public interface IMotionPlatform
{
    bool PrefersReducedMotion { get; }

    /// <summary>Raised when the user changes the setting while the app runs.</summary>
    event EventHandler? PreferenceChanged;

    void Play(HapticKind kind);
}

/// <summary>
/// The motion settings the whole app reads: which convention, whether motion is reduced, and the
/// haptics. One per process, because the setting is the user's, not a screen's.
/// </summary>
public static class MotionEnvironment
{
    private static IMotionPlatform? _platform;

    /// <summary>Touch on the phones, desktop on the Mac.</summary>
    public static MotionFlavor Flavor { get; set; } = MotionFlavor.Touch;

    /// <summary>
    /// The OS seam. Setting it reads the preference and follows its changes from then on.
    /// </summary>
    public static IMotionPlatform? Platform
    {
        get => _platform;
        set
        {
            if (_platform is not null)
            {
                _platform.PreferenceChanged -= OnPreferenceChanged;
            }

            _platform = value;
            if (_platform is not null)
            {
                _platform.PreferenceChanged += OnPreferenceChanged;
            }

            OnPreferenceChanged(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Forces reduced motion regardless of the platform; null follows the platform. For tests and
    /// for rendering a screen off a device.
    /// </summary>
    public static bool? ReduceMotionOverride
    {
        get => _override;
        set
        {
            _override = value;
            OnPreferenceChanged(null, EventArgs.Empty);
        }
    }

    private static bool? _override;

    /// <summary>True when every move and scale should be a 150 ms cross-fade (§04).</summary>
    public static bool ReduceMotion => _override ?? _platform?.PrefersReducedMotion ?? false;

    /// <summary>
    /// Jumps every animation straight to its end. For headless rendering, where no frames are
    /// drawn between two layout passes and a half-played sheet would be what gets captured.
    /// </summary>
    public static bool Instant { get; set; }

    /// <summary>Raised when <see cref="ReduceMotion"/> may have changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// A touch, on a phone. The desktops have none (M2: "햅틱 없음"), so the desktop flavour plays
    /// nothing even when a platform is set.
    /// </summary>
    public static void Haptic(HapticKind kind)
    {
        if (Flavor == MotionFlavor.Touch)
        {
            _platform?.Play(kind);
        }
    }

    /// <summary>A token under the current flavour.</summary>
    public static MotionSpec Token(MotionToken token) => MotionTokens.Get(token, Flavor);

    /// <summary>The same with another duration.</summary>
    public static MotionSpec Token(MotionToken token, double milliseconds) => MotionTokens.Get(token, Flavor, milliseconds);

    private static void OnPreferenceChanged(object? sender, EventArgs e) => Changed?.Invoke(null, EventArgs.Empty);
}
