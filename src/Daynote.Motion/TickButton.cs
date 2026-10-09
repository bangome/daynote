using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Daynote.Motion;

/// <summary>
/// A to-do's checkbox that plays M3: the fill grows, the check draws itself, the row's
/// strikethrough passes left to right and, on a phone, a ring pulses out.
/// </summary>
/// <remarks>
/// <para>
/// The tick shows at once and the toggle runs <see cref="Choreography.CheckSettleMs"/> later. The
/// toggle is what moves the row down into 완료, and the spec holds that move back so a mistaken tap
/// can be taken back: a second tap inside the wait cancels the tick, and nothing is written.
/// Unticking is not held: it runs at once, with the fill shrinking away.
/// </para>
/// <para>
/// The strikethrough belongs to the row's text, not to the box, so it is found rather than owned:
/// the nearest ancestor classed <c>todorow</c>, and the <c>Border.strike</c> inside it. Each app
/// draws its own box; this is only the behaviour, so the phone and the Mac tick the same way.
/// </para>
/// </remarks>
public abstract class TickButton : Button
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<TickButton, bool>(nameof(IsChecked));

    /// <summary>What a tick runs: the row's toggle.</summary>
    public static readonly StyledProperty<ICommand?> ToggleProperty =
        AvaloniaProperty.Register<TickButton, ICommand?>(nameof(Toggle));

    private CancellationTokenSource? _pending;

    protected override Type StyleKeyOverride => typeof(Button);

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public ICommand? Toggle
    {
        get => GetValue(ToggleProperty);
        set => SetValue(ToggleProperty, value);
    }

    /// <summary>The fill that grows out of the box's middle.</summary>
    protected abstract Visual Fill { get; }

    /// <summary>The ring that pulses out behind the box, or null where the platform has none.</summary>
    protected abstract Visual? Pulse { get; }

    /// <summary>Draws the check stroke to a fraction, 0 to 1.</summary>
    protected abstract void DrawMark(double fraction);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty && _pending is null)
        {
            ShowResting(IsChecked);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowResting(IsChecked);
    }

    protected override void OnClick()
    {
        base.OnClick();

        if (_pending is { } waiting)
        {
            _pending = null;
            waiting.Cancel();
            PseudoClasses.Set(":checked", false);
            _ = MotionPlayer.Play(this, "check", Choreography.Uncheck(Parts()));
            return;
        }

        if (IsChecked)
        {
            PseudoClasses.Set(":checked", false);
            _ = MotionPlayer.Play(this, "check", Choreography.Uncheck(Parts()));
            Toggle?.Execute(null);
            return;
        }

        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        using var pending = new CancellationTokenSource();
        _pending = pending;
        PseudoClasses.Set(":checked", true);
        _ = MotionPlayer.Play(this, "check", Choreography.Check(Parts()));
        try
        {
            TimeSpan settle = MotionEnvironment.Instant ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Choreography.CheckSettleMs);
            await Task.Delay(settle, pending.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _pending = null;
        Toggle?.Execute(null);
    }

    /// <summary>The resting state, with nothing in motion: filled and drawn, or empty.</summary>
    private void ShowResting(bool on)
    {
        PseudoClasses.Set(":checked", on);
        MotionTransform.For(Fill).Reset();
        Fill.Opacity = on ? 1 : 0;
        DrawMark(on ? 1 : 0);
        if (Pulse is { } pulse)
        {
            pulse.Opacity = 0;
        }

        if (Strike() is { } strike)
        {
            strike.Opacity = 0;
        }
    }

    private CheckParts Parts() => new(Fill, DrawMark, Strike() ?? new Border(), Pulse);

    /// <summary>The row's strikethrough line, if the row has one.</summary>
    private Border? Strike() =>
        this.GetVisualAncestors().OfType<Control>().FirstOrDefault(static c => c.Classes.Contains("todorow")) is { } row
            ? row.GetVisualDescendants().OfType<Border>().FirstOrDefault(static b => b.Classes.Contains("strike"))
            : null;
}
