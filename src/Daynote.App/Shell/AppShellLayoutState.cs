using System.Windows;

namespace Daynote.App.Shell;

public enum AppLayoutState
{
    Compact,
    Regular,
    Wide,
}

/// <summary>
/// Layout thresholds sourced from the <c>Daynote.Layout.*</c> resource dictionary so no
/// view-model duplicates the Section 4 constants.
/// </summary>
public readonly record struct LayoutThresholds(
    double CompactMax,
    double RegularMin,
    double RegularMax,
    double WideMin,
    double Hysteresis)
{
    public static LayoutThresholds FromLookup(Func<string, double> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return new LayoutThresholds(
            resolve("Daynote.Layout.CompactMax"),
            resolve("Daynote.Layout.RegularMin"),
            resolve("Daynote.Layout.RegularMax"),
            resolve("Daynote.Layout.WideMin"),
            resolve("Daynote.Layout.Hysteresis"));
    }

    public static LayoutThresholds FromApplicationResources() =>
        FromLookup(static key =>
            System.Windows.Application.Current?.TryFindResource(key) is double value
                ? value
                : throw new InvalidOperationException($"Required layout resource '{key}' is not loaded."));
}

/// <summary>
/// Single owner of the Compact/Regular/Wide decision. The band partition is exact at the
/// registered thresholds; leaving an established state requires crossing the next threshold
/// by <see cref="LayoutThresholds.Hysteresis"/> device-independent pixels (Section 4).
/// </summary>
public sealed class AppShellLayoutState
{
    private readonly LayoutThresholds _thresholds;
    private bool _initialized;

    public AppShellLayoutState(LayoutThresholds thresholds)
    {
        _thresholds = thresholds;
    }

    public AppLayoutState Current { get; private set; } = AppLayoutState.Regular;

    /// <summary>Applies an effective content width and returns the resulting state.</summary>
    public AppLayoutState Update(double effectiveWidth)
    {
        AppLayoutState classified = Classify(effectiveWidth);
        if (!_initialized)
        {
            _initialized = true;
            Current = classified;
            return Current;
        }

        if (classified == Current)
        {
            return Current;
        }

        if (classified > Current && CrossedUpward(effectiveWidth, classified))
        {
            Current = classified;
        }
        else if (classified < Current && CrossedDownward(effectiveWidth))
        {
            Current = classified;
        }

        return Current;
    }

    private AppLayoutState Classify(double width)
    {
        if (width < _thresholds.RegularMin)
        {
            return AppLayoutState.Compact;
        }

        return width >= _thresholds.WideMin ? AppLayoutState.Wide : AppLayoutState.Regular;
    }

    private bool CrossedUpward(double width, AppLayoutState target)
    {
        double boundary = target == AppLayoutState.Wide ? _thresholds.WideMin : _thresholds.RegularMin;
        return width >= boundary + _thresholds.Hysteresis;
    }

    private bool CrossedDownward(double width)
    {
        double boundary = Current == AppLayoutState.Wide ? _thresholds.WideMin : _thresholds.RegularMin;
        return width <= boundary - 1 - _thresholds.Hysteresis;
    }
}
