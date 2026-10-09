using Avalonia.Animation.Easings;

namespace Daynote.Motion;

/// <summary>
/// A spring, sampled: the CSS <c>linear()</c> the spec gives for each spring token, as an Avalonia
/// easing.
/// </summary>
/// <remarks>
/// The spec's iOS and Android values are system springs (<c>.spring(response:dampingFraction:)</c>,
/// <c>spring(dampingRatio, stiffness)</c>), which Avalonia has no equivalent of. The web column
/// gives the same springs as piecewise-linear curves over a fixed duration, and that is what is
/// reproduced here, point for point, so a curve on a phone is the curve in the spec's own demos.
/// </remarks>
public sealed class SampledSpringEasing : Easing
{
    private readonly (double At, double Value)[] _points;

    /// <param name="points">
    /// The stops, in order, as (progress 0..1, value). The first must be at 0 and the last at 1.
    /// </param>
    public SampledSpringEasing(params (double At, double Value)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2 || points[0].At != 0 || points[^1].At != 1)
        {
            throw new ArgumentException("A sampled curve runs from progress 0 to progress 1.", nameof(points));
        }

        _points = points;
    }

    /// <summary>The highest value the curve reaches, less one: 0.06 for a 6% overshoot.</summary>
    public double Overshoot => _points.Max(static p => p.Value) - 1;

    public override double Ease(double progress)
    {
        if (progress <= 0)
        {
            return _points[0].Value;
        }

        if (progress >= 1)
        {
            return _points[^1].Value;
        }

        for (int i = 1; i < _points.Length; i++)
        {
            (double at, double value) = _points[i];
            if (progress <= at)
            {
                (double fromAt, double fromValue) = _points[i - 1];
                return fromValue + ((value - fromValue) * (progress - fromAt) / (at - fromAt));
            }
        }

        return _points[^1].Value;
    }
}

/// <summary>
/// The same spring with its bounce scaled down: the Mac and Windows reading of a touch spring
/// ("Mac·Windows는 각 OS 관례대로 탄성을 줄입니다").
/// </summary>
/// <remarks>
/// Only what happens after the curve first reaches its end value is scaled - the overshoot and
/// the settle back - so the rise keeps its speed and the item still lands when the touch one does.
/// M2 asks for exactly this on the desktop: the same row arrival, the overshoot halved.
/// </remarks>
public sealed class ReducedBounceEasing : Easing
{
    private readonly Easing _inner;
    private readonly double _factor;
    private readonly double _firstArrival;

    /// <param name="inner">The touch curve.</param>
    /// <param name="factor">How much of the bounce to keep: 0.5 halves it, 0 removes it.</param>
    public ReducedBounceEasing(Easing inner, double factor)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _factor = Math.Clamp(factor, 0, 1);

        // Where the curve first reaches 1, found by walking it: these curves are monotonic up to
        // that point, and a thousand steps place it well inside a frame.
        _firstArrival = 1;
        for (int step = 0; step <= 1000; step++)
        {
            double at = step / 1000.0;
            if (_inner.Ease(at) >= 1)
            {
                _firstArrival = at;
                break;
            }
        }
    }

    public override double Ease(double progress)
    {
        double value = _inner.Ease(progress);
        return progress < _firstArrival ? value : 1 + ((value - 1) * _factor);
    }
}
