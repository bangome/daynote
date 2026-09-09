using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Onboarding;

namespace Daynote.Desktop.Views;

/// <summary>
/// The coaching overlay: dims the window, cuts a hole around the current step's target so the real
/// UI shows through, rings it, and floats a callout beside it.
/// </summary>
/// <remarks>
/// A port of the WPF <c>TutorialView</c>, which the Avalonia shell had no equivalent of — it showed
/// one flat scrim and a centred card, so every step described a part of the UI without pointing at
/// it. Targets are resolved by name from the hosting window; a step with no target, or whose target
/// is not laid out, falls back to a centred callout and no hole.
/// </remarks>
public partial class TutorialOverlay : UserControl
{
    private const double Pad = 6;   // how far the hole is inflated past the target
    private const double Gap = 12;  // callout gap from the target
    private const double Edge = 8;  // minimum margin from the overlay's edges

    private TutorialViewModel? _model;
    private Control? _target;

    public TutorialOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => ScheduleReposition();
        SizeChanged += (_, _) => ScheduleReposition();
        DataContextChanged += (_, _) => Observe(DataContext as TutorialViewModel);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                ScheduleReposition();
            }
        };
    }

    private void Observe(TutorialViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        _model = model;
        if (_model is not null)
        {
            _model.PropertyChanged += OnModelPropertyChanged;
        }

        ScheduleReposition();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TutorialViewModel.CurrentStep)
            or nameof(TutorialViewModel.Index)
            or nameof(TutorialViewModel.IsOpen))
        {
            ScheduleReposition();
        }
    }

    /// <summary>Escape skips the tutorial, as the Skip button does.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _model is not null && _model.SkipCommand.CanExecute(null))
        {
            _model.SkipCommand.Execute(null);
            e.Handled = true;
        }
    }

    // The target's layout is not settled at the instant a step changes or the overlay is shown, so
    // the measurement is deferred a pass.
    private void ScheduleReposition() =>
        Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);

    private void Reposition()
    {
        if (_model is not { IsOpen: true } || !IsVisible)
        {
            return;
        }

        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var full = new RectangleGeometry(new Rect(0, 0, w, h));

        if (TryGetTargetRect(w, h) is { } rect && _target is not null)
        {
            // Concentric with the target: the hole is the target inflated by Pad, so its radius is
            // the target's own radius plus Pad — the way a focus ring sits outside a rounded control.
            double radius = SpotlightRadius(_target) + Pad;
            Scrim.Data = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                full,
                new RectangleGeometry(rect, radius, radius));

            Canvas.SetLeft(HighlightRing, rect.X);
            Canvas.SetTop(HighlightRing, rect.Y);
            HighlightRing.Width = rect.Width;
            HighlightRing.Height = rect.Height;
            HighlightRing.CornerRadius = new CornerRadius(radius);
            HighlightRing.IsVisible = true;
            PlaceCallout(rect, w, h);
        }
        else
        {
            Scrim.Data = full;
            HighlightRing.IsVisible = false;
            PlaceCalloutCentered(w, h);
        }
    }

    private Rect? TryGetTargetRect(double w, double h)
    {
        _target = null;
        if (_model?.CurrentStep.TargetName is not { Length: > 0 } name
            || this.FindAncestorOfType<Window>() is not { } window)
        {
            return null;
        }

        if (window.FindControl<Control>(name) is not { IsVisible: true } element
            || element.Bounds.Width <= 0 || element.Bounds.Height <= 0
            || element.TranslatePoint(default, OverlayCanvas) is not { } origin)
        {
            return null;
        }

        _target = element;
        var bounds = new Rect(origin, element.Bounds.Size).Inflate(Pad);
        Rect clipped = bounds.Intersect(new Rect(0, 0, w, h));
        return clipped.Width <= 0 || clipped.Height <= 0 ? null : clipped;
    }

    private Size MeasureCallout()
    {
        Callout.Measure(new Size(Callout.Width, double.PositiveInfinity));
        double height = Callout.DesiredSize.Height > 0 ? Callout.DesiredSize.Height : Callout.Bounds.Height;
        return new Size(Callout.Width, height);
    }

    private void PlaceCallout(Rect target, double w, double h)
    {
        Size size = MeasureCallout();

        double top;
        bool stacked = true;
        if (target.Bottom + Gap + size.Height <= h)
        {
            top = target.Bottom + Gap;                          // below the target, preferred
        }
        else if (target.Top - Gap - size.Height >= 0)
        {
            top = target.Top - Gap - size.Height;               // above it
        }
        else
        {
            top = Clamp(target.Y, Edge, h - size.Height - Edge); // beside it
            stacked = false;
        }

        double left = stacked
            ? Clamp(target.X, Edge, w - size.Width - Edge)
            : target.Right + Gap + size.Width <= w
                ? target.Right + Gap
                : Clamp(target.X - Gap - size.Width, Edge, w - size.Width - Edge);

        Canvas.SetLeft(Callout, Clamp(left, Edge, w - size.Width - Edge));
        Canvas.SetTop(Callout, Clamp(top, Edge, h - size.Height - Edge));
    }

    private void PlaceCalloutCentered(double w, double h)
    {
        Size size = MeasureCallout();
        Canvas.SetLeft(Callout, Clamp((w - size.Width) / 2, Edge, w - size.Width - Edge));
        Canvas.SetTop(Callout, Clamp((h - size.Height) / 2, Edge, h - size.Height - Edge));
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);

    /// <summary>
    /// The corner radius the target actually draws with. A Border says so itself; a templated control
    /// says so through the first rounded Border inside it; a bare control in a pill says so through
    /// the pill around it. Anything else takes the control radius. Same rule as the WPF shell.
    /// </summary>
    public static double SpotlightRadius(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is Border own && own.CornerRadius.TopLeft > 0)
        {
            return own.CornerRadius.TopLeft;
        }

        if (target.GetVisualDescendants().OfType<Border>().FirstOrDefault(static b => b.CornerRadius.TopLeft > 0)
            is { } inner)
        {
            return inner.CornerRadius.TopLeft;
        }

        if (target.GetVisualAncestors().OfType<Border>().FirstOrDefault(static b => b.CornerRadius.TopLeft > 0)
            is { } outer)
        {
            return outer.CornerRadius.TopLeft;
        }

        return target.TryFindResource("Daynote.Product.Radius.Control", out object? value)
            && value is CornerRadius control
            ? control.TopLeft
            : 8;
    }
}
