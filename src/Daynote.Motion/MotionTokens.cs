using Avalonia.Animation.Easings;

namespace Daynote.Motion;

/// <summary>The four springs every value in the spec is chosen from (§00).</summary>
public enum MotionToken
{
    /// <summary>Short transitions, about 320 ms.</summary>
    Snappy,

    /// <summary>Arrival and confirmation, about 480 ms with a 6% overshoot.</summary>
    Bouncy,

    /// <summary>Large surfaces, and leaving, about 450 ms.</summary>
    Gentle,

    /// <summary>Closing, 220-280 ms, easing in.</summary>
    Exit,
}

/// <summary>
/// Which platform convention a token is read under: the phones' system springs, or the desktops',
/// which damp the bounce (§00, and the Mac/Win line under every interaction).
/// </summary>
public enum MotionFlavor
{
    Touch,
    Desktop,
}

/// <summary>One curve and how long it runs.</summary>
public sealed record MotionSpec(TimeSpan Duration, Easing Easing)
{
    public static MotionSpec Ms(double milliseconds, Easing easing) => new(TimeSpan.FromMilliseconds(milliseconds), easing);
}

/// <summary>
/// The spec's tokens as Avalonia easings, and the handful of fixed desktop values it names beside
/// them. Every animation in the apps takes its curve from here; no screen defines its own.
/// </summary>
public static class MotionTokens
{
    /// <summary>snappy — <c>linear(0, 0.35 8%, 0.75 18%, 0.96 30%, 1.01 40%, 1)</c>.</summary>
    public static SampledSpringEasing SnappyCurve { get; } = new(
        (0, 0), (0.08, 0.35), (0.18, 0.75), (0.30, 0.96), (0.40, 1.01), (1, 1));

    /// <summary>
    /// bouncy — <c>linear(0, 0.22 6%, 0.58 14%, 0.9 24%, 1.05 33%, 1.06 38%, 1.02 48%, 0.995 60%, 1)</c>.
    /// </summary>
    public static SampledSpringEasing BouncyCurve { get; } = new(
        (0, 0), (0.06, 0.22), (0.14, 0.58), (0.24, 0.9), (0.33, 1.05), (0.38, 1.06), (0.48, 1.02), (0.60, 0.995), (1, 1));

    /// <summary>gentle — <c>cubic-bezier(0.2, 0.8, 0.2, 1)</c>, iOS <c>.smooth</c>.</summary>
    public static Easing GentleCurve { get; } = new SplineEasing(0.2, 0.8, 0.2, 1);

    /// <summary>exit — <c>cubic-bezier(0.4, 0, 1, 1)</c>, Android FastOutLinearIn.</summary>
    public static Easing ExitCurve { get; } = new SplineEasing(0.4, 0, 1, 1);

    /// <summary>CSS <c>ease-out</c>, which M3 names for the desktop's check fill instead of the spring.</summary>
    public static Easing EaseOutCurve { get; } = new SplineEasing(0, 0, 0.58, 1);

    /// <summary>The overshoot the desktops keep (M2: "overshoot 절반").</summary>
    public const double DesktopBounceFactor = 0.5;

    private static readonly Easing DesktopSnappy = new ReducedBounceEasing(SnappyCurve, DesktopBounceFactor);
    private static readonly Easing DesktopBouncy = new ReducedBounceEasing(BouncyCurve, DesktopBounceFactor);

    /// <summary>A token as <paramref name="flavor"/> reads it.</summary>
    public static MotionSpec Get(MotionToken token, MotionFlavor flavor) => (token, flavor) switch
    {
        (MotionToken.Snappy, MotionFlavor.Touch) => MotionSpec.Ms(320, SnappyCurve),
        (MotionToken.Snappy, _) => MotionSpec.Ms(320, DesktopSnappy),
        (MotionToken.Bouncy, MotionFlavor.Touch) => MotionSpec.Ms(480, BouncyCurve),
        (MotionToken.Bouncy, _) => MotionSpec.Ms(480, DesktopBouncy),
        (MotionToken.Gentle, _) => MotionSpec.Ms(450, GentleCurve),
        (MotionToken.Exit, _) => MotionSpec.Ms(240, ExitCurve),
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, null),
    };

    /// <summary>
    /// The one curve reduced motion leaves (§04): every move and scale becomes a 150 ms opacity
    /// cross-fade, with no spring.
    /// </summary>
    public static MotionSpec ReducedFade { get; } = MotionSpec.Ms(150, new LinearEasing());

    /// <summary>The same token with another duration, for the spec's "bouncy 500ms" and "ease-in 220ms".</summary>
    public static MotionSpec Get(MotionToken token, MotionFlavor flavor, double milliseconds) =>
        Get(token, flavor) with { Duration = TimeSpan.FromMilliseconds(milliseconds) };
}
