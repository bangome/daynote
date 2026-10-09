using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Daynote.Motion;

namespace Daynote.Mobile.Views;

/// <summary>
/// A bottom sheet over its scrim (motion spec M6): it rises with a slight overshoot, closes
/// straight down without one, and can be dragged down to close.
/// </summary>
/// <remarks>
/// <para>
/// The first child is the scrim - a button whose command closes the sheet - and the second the
/// sheet. Opening and closing follow <see cref="IsOpen"/>; the layer stays visible until the
/// closing has played, which a plain <c>IsVisible</c> binding cannot do.
/// </para>
/// <para>
/// The drag is taken from the sheet's top strip (the grab handle and the title beside it), not the
/// whole sheet, so the hour grid and the month cells keep their taps and the scrolling sheets their
/// scroll. Let go faster than 800 pt/s, or more than 40% of the way down, and it closes, carrying
/// on from where the finger left it; otherwise it goes back up.
/// </para>
/// </remarks>
public sealed class SheetLayer : Panel
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<SheetLayer, bool>(nameof(IsOpen));

    /// <summary>How much of the sheet's top answers to a drag, in points.</summary>
    private const double GrabHeight = 56;

    private Point? _dragStart;
    private double _offset;
    private (double Y, ulong At) _last;
    private double _velocity;

    public SheetLayer()
    {
        IsVisible = false;
        AddHandler(PointerPressedEvent, OnPressed, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => _dragStart = null);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private Button? Scrim => Children.Count > 0 ? Children[0] as Button : null;

    private Control? Sheet => Children.Count > 1 ? Children[1] : null;

    /// <summary>How far the sheet travels to leave: its height and the strip it bleeds into.</summary>
    private double Travel => (Sheet?.Bounds.Height ?? 0) + Math.Max(0, -(Sheet?.Margin.Bottom ?? 0)) + 24;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsOpenProperty)
        {
            return;
        }

        if (IsOpen)
        {
            Open();
        }
        else
        {
            _ = CloseAsync(fromY: 0);
        }
    }

    private void Open()
    {
        if (Scrim is not { } scrim || Sheet is not { } sheet)
        {
            IsVisible = true;
            return;
        }

        MotionPlayer.Stop(this, "m6");
        IsVisible = true;
        if (MotionEnvironment.Instant)
        {
            _ = MotionPlayer.Play(this, "m6", Choreography.SheetOpen(scrim, sheet, Travel));
            return;
        }

        // Its height is known only once it has been laid out, which is the next pass; until then it
        // is kept out of sight so the first frame does not show it already up.
        scrim.Opacity = 0;
        sheet.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsOpen)
            {
                _ = MotionPlayer.Play(this, "m6", Choreography.SheetOpen(scrim, sheet, Travel));
            }
        }, DispatcherPriority.Loaded);
    }

    private async Task CloseAsync(double fromY)
    {
        if (!IsVisible)
        {
            return;
        }

        if (Scrim is { } scrim && Sheet is { } sheet &&
            !await MotionPlayer.Play(this, "m6", Choreography.SheetClose(scrim, sheet, Travel, fromY)).ConfigureAwait(true))
        {
            return;
        }

        // Opened again while it was on its way down: it stays.
        if (!IsOpen)
        {
            IsVisible = false;
        }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Sheet is not { } sheet || !IsOpen)
        {
            return;
        }

        Point at = e.GetPosition(sheet);
        if (at.Y < 0 || at.Y > GrabHeight || at.X < 0 || at.X > sheet.Bounds.Width)
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        _offset = 0;
        _velocity = 0;
        _last = (0, e.Timestamp);
        MotionPlayer.Stop(this, "m6");
        e.Pointer.Capture(this);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || Sheet is not { } sheet)
        {
            return;
        }

        // Down follows the finger; up resists, a third of the way, so the sheet cannot be pulled
        // off its edge.
        double dy = e.GetPosition(this).Y - start.Y;
        _offset = dy >= 0 ? dy : dy / 3;
        MotionTransform.For(sheet).Y = _offset;

        ulong elapsed = e.Timestamp - _last.At;
        if (elapsed > 0)
        {
            _velocity = (_offset - _last.Y) / elapsed * 1000;
            _last = (_offset, e.Timestamp);
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStart is null || Sheet is not { } sheet)
        {
            return;
        }

        _dragStart = null;
        e.Pointer.Capture(null);
        if (_offset <= 0.5)
        {
            return;
        }

        e.Handled = true;
        if (Choreography.ShouldDismiss(_offset, Travel, _velocity))
        {
            _ = DismissAsync(_offset);
        }
        else
        {
            _ = MotionPlayer.Play(this, "m6", Choreography.SheetSettle(sheet, _offset));
        }
    }

    /// <summary>A drag that closed it: the sheet carries on down from where it was let go, then the close command runs.</summary>
    private async Task DismissAsync(double fromY)
    {
        if (Scrim is { } scrim && Sheet is { } sheet)
        {
            await MotionPlayer.Play(this, "m6", Choreography.SheetClose(scrim, sheet, Travel, fromY)).ConfigureAwait(true);
        }

        IsVisible = false;
        Scrim?.Command?.Execute(Scrim.CommandParameter);
    }
}
