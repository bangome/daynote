using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>How the popover arrives (Motion M9).</summary>
public enum PopoverEntrance
{
    /// <summary>Opened from the shortcut: shown at once, no animation.</summary>
    None,

    /// <summary>The Mac: NSPopover's own, a short fade and a slight scale from the top, no spring.</summary>
    Popover,

    /// <summary>Windows: the Fluent flyout entrance, opacity and translateY −6→0 over 167 ms.</summary>
    Flyout,
}

/// <summary>
/// The menu bar popover and tray flyout window (menu bar design §01). Borderless, kept out of the
/// task switcher, and placed by <see cref="Lifecycle.MenuBarController"/> under the item that
/// opened it.
/// </summary>
/// <remarks>
/// The keys are taken before the box sees them, as the note editor does for its <c>@</c> popup:
/// Enter makes, Tab switches what Enter makes, Esc dismisses. Tab is always swallowed while the
/// popover has focus — moving focus off the box to a footer link is never what it means here.
/// </remarks>
public partial class MenuBarPopover : Window
{
    private static readonly TimeSpan PopoverDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan FlyoutDuration = TimeSpan.FromMilliseconds(167);
    private static readonly TimeSpan ReducedDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(100);

    public MenuBarPopover()
    {
        InitializeComponent();

        // Windows draws its flyouts with the system's 8 px corners (B6, B7); the Mac keeps 14.
        if (OperatingSystem.IsWindows())
        {
            Card.CornerRadius = new CornerRadius(8);
        }
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty && DataContext is MenuBarViewModel model)
            {
                model.UpdateCaret(Box.CaretIndex);
            }
        };
    }

    /// <summary>Where the card sits inside the window: the rest is room for its shadow.</summary>
    public Thickness CardMargin => Frame.Margin;

    /// <summary>The card's width in device-independent pixels.</summary>
    public double CardWidth => Card.Width;

    /// <summary>The card's height as last laid out, or as measured before the first layout.</summary>
    public double CardHeight => Card.Bounds.Height > 0 ? Card.Bounds.Height : Card.DesiredSize.Height;

    public void FocusBox()
    {
        Box.Focus();
        Box.CaretIndex = Box.Text?.Length ?? 0;
    }

    /// <summary>Plays the entrance, or with <see cref="PopoverEntrance.None"/> just shows the Card.</summary>
    /// <param name="reduceMotion">The system asks for less motion: a short fade and nothing else.</param>
    public void PlayEntrance(PopoverEntrance entrance, bool reduceMotion)
    {
        Card.Transitions = null;
        if (entrance == PopoverEntrance.None)
        {
            Card.Opacity = 1;
            Card.RenderTransform = TransformOperations.Identity;
            return;
        }

        TimeSpan duration = reduceMotion ? ReducedDuration
            : entrance == PopoverEntrance.Flyout ? FlyoutDuration
            : PopoverDuration;
        Easing easing = entrance == PopoverEntrance.Flyout
            ? new SplineEasing(0, 0, 0, 1)
            : new SplineEasing(0.2, 0.8, 0.2, 1);

        Card.Opacity = 0;
        Card.RenderTransform = reduceMotion ? TransformOperations.Identity
            : entrance == PopoverEntrance.Flyout ? TransformOperations.Parse("translateY(-6px)")
            : TransformOperations.Parse("scale(0.98)");

        Card.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = easing },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = duration, Easing = easing },
        ];
        Card.Opacity = 1;
        Card.RenderTransform = TransformOperations.Identity;
    }

    /// <summary>Focus went elsewhere: a 100 ms fade, then the window can be hidden.</summary>
    public async Task PlayExitAsync()
    {
        Card.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = ExitDuration, Easing = new LinearEasing() },
        ];
        Card.Opacity = 0;
        await Task.Delay(ExitDuration).ConfigureAwait(true);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MenuBarViewModel model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                bool newNote = e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

                // Posted, so a Korean syllable still being composed is committed into the box before
                // the text is read.
                Dispatcher.UIThread.Post(() => Lifecycle.MenuBarController.Forget(model.SubmitAsync(newNote)), DispatcherPriority.Input);
                break;
            case Key.Tab:
                e.Handled = true;
                model.ToggleKind();
                break;
            case Key.Escape:
                e.Handled = true;
                model.Cancel();
                break;
        }
    }
}
