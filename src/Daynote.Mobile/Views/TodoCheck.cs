using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Daynote.Motion;

namespace Daynote.Mobile.Views;

/// <summary>
/// A to-do's checkbox, with the tick the motion spec draws (M3): the fill grows from the middle,
/// the check draws itself, the row's strikethrough passes left to right, and a ring pulses out.
/// </summary>
/// <remarks>
/// <para>
/// The tick is shown at once and kept for 600 ms before the command runs (<see
/// cref="Choreography.CheckSettleMs"/>). Running it is what moves the row down into 완료, and the
/// spec holds that move back so a mistaken tap can be taken back: a second tap inside the wait
/// cancels the tick and nothing is written.
/// </para>
/// <para>
/// The strikethrough belongs to the row's text, not to the box, so it is found rather than owned:
/// the nearest ancestor classed <c>todorow</c>, and the <c>Border.strike</c> inside it.
/// </para>
/// </remarks>
public sealed class TodoCheck : Button
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<TodoCheck, bool>(nameof(IsChecked));

    /// <summary>What a tick runs: the row's toggle.</summary>
    public static readonly StyledProperty<ICommand?> ToggleProperty =
        AvaloniaProperty.Register<TodoCheck, ICommand?>(nameof(Toggle));

    private readonly Border _pulse;
    private readonly Border _fill;
    private readonly Glyph _mark;
    private CancellationTokenSource? _pending;

    public TodoCheck()
    {
        Classes.Add("check");
        _pulse = new Border { Classes = { "checkpulse" }, Opacity = 0, IsHitTestVisible = false };
        _fill = new Border { Classes = { "checkfill" } };
        _mark = new Glyph
        {
            Kind = GlyphKind.Check,
            Width = 11,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _mark.Bind(Glyph.ForegroundProperty, this.GetResourceObservable("Mobile.OnPri"));
        _pulse.RenderTransformOrigin = RelativePoint.Center;
        _fill.RenderTransformOrigin = RelativePoint.Center;

        Content = new Panel
        {
            Width = 22,
            Height = 22,
            Children = { _pulse, new Border { Classes = { "checkbox" } }, _fill, _mark },
        };
        Show(IsChecked);
    }

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty && _pending is null)
        {
            Show(IsChecked);
        }
    }

    protected override void OnClick()
    {
        base.OnClick();

        // A second tap while the tick waits takes it back: nothing has been written yet.
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
            await Task.Delay(MotionEnvironment.Instant ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Choreography.CheckSettleMs), pending.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _pending = null;
        Toggle?.Execute(null);
    }

    /// <summary>The resting state, with nothing in motion: filled and drawn, or empty.</summary>
    private void Show(bool on)
    {
        PseudoClasses.Set(":checked", on);
        MotionTransform.For(_fill).Reset();
        _fill.Opacity = on ? 1 : 0;
        _mark.Draw = on ? 1 : 0;
        _pulse.Opacity = 0;
        if (Strike() is { } strike)
        {
            strike.Opacity = 0;
        }
    }

    private CheckParts Parts() =>
        new(_fill, v => _mark.Draw = v, Strike() ?? new Border(), _pulse);

    /// <summary>The row's strikethrough line, if the row has one.</summary>
    private Border? Strike() =>
        this.GetVisualAncestors().OfType<Control>().FirstOrDefault(static c => c.Classes.Contains("todorow")) is { } row
            ? row.GetVisualDescendants().OfType<Border>().FirstOrDefault(static b => b.Classes.Contains("strike"))
            : null;
}
