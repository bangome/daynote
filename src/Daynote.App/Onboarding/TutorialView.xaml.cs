using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Daynote.App.Onboarding;

/// <summary>
/// A coaching overlay: dims the whole window with a scrim, cuts a hole around the current step's target
/// element (so the real UI shows through), rings it, and floats a callout beside it. Target elements are
/// resolved by x:Name from the hosting window's namescope; steps without a target (or whose target is
/// hidden) fall back to a centered callout with no cut-out.
/// </summary>
public partial class TutorialView : System.Windows.Controls.UserControl
{
    private const double Pad = 6;      // spotlight inflation around the target
    private const double Gap = 12;     // callout gap from the target / window edges
    private const double Edge = 8;     // min margin from window edges

    private TutorialViewModel? _vm;

    public TutorialView()
    {
        InitializeComponent();
        Loaded += (_, _) => ScheduleReposition();
        SizeChanged += (_, _) => ScheduleReposition();
        IsVisibleChanged += (_, _) => ScheduleReposition();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as TutorialViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        ScheduleReposition();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TutorialViewModel.CurrentStep)
            or nameof(TutorialViewModel.Index)
            or nameof(TutorialViewModel.IsOpen))
        {
            ScheduleReposition();
        }
    }

    /// <summary>Escape skips the tutorial (same as the Skip button).</summary>
    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == WpfKey.Escape && _vm is not null && _vm.SkipCommand.CanExecute(null))
        {
            _vm.SkipCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Layout of the target may not be settled at the moment the step changes / overlay shows, so defer.
    private void ScheduleReposition() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Reposition));

    private void Reposition()
    {
        if (_vm is null || !_vm.IsOpen || !IsVisible)
        {
            return;
        }

        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var full = new RectangleGeometry(new Rect(0, 0, w, h));
        Rect? target = TryGetTargetRect(w, h);

        if (target is { } rect)
        {
            double radius = 8;
            Scrim.Data = new GeometryGroup
            {
                FillRule = FillRule.EvenOdd,
                Children = { full, new RectangleGeometry(rect, radius, radius) },
            };
            Canvas.SetLeft(HighlightRing, rect.X);
            Canvas.SetTop(HighlightRing, rect.Y);
            HighlightRing.Width = rect.Width;
            HighlightRing.Height = rect.Height;
            HighlightRing.Visibility = Visibility.Visible;
            PlaceCallout(rect, w, h);
        }
        else
        {
            Scrim.Data = full;
            HighlightRing.Visibility = Visibility.Collapsed;
            PlaceCalloutCentered(w, h);
        }
    }

    private Rect? TryGetTargetRect(double w, double h)
    {
        string? name = _vm?.CurrentStep.TargetName;
        if (string.IsNullOrEmpty(name) || Window.GetWindow(this) is not { } window)
        {
            return null;
        }

        if (window.FindName(name) is not FrameworkElement { IsVisible: true } element
            || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }

        Rect bounds = element.TransformToVisual(OverlayCanvas)
            .TransformBounds(new Rect(new System.Windows.Size(element.ActualWidth, element.ActualHeight)));
        bounds.Inflate(Pad, Pad);
        bounds.Intersect(new Rect(0, 0, w, h));
        return bounds.IsEmpty ? null : bounds;
    }

    private (double Width, double Height) MeasureCallout()
    {
        Callout.Measure(new System.Windows.Size(Callout.Width, double.PositiveInfinity));
        double height = Callout.DesiredSize.Height > 0 ? Callout.DesiredSize.Height : Callout.ActualHeight;
        return (Callout.Width, height);
    }

    private void PlaceCallout(Rect target, double w, double h)
    {
        (double cw, double ch) = MeasureCallout();

        double top;
        if (target.Bottom + Gap + ch <= h)
        {
            top = target.Bottom + Gap;                 // below the target (preferred)
        }
        else if (target.Top - Gap - ch >= 0)
        {
            top = target.Top - Gap - ch;               // above
        }
        else
        {
            top = Clamp(target.Top, Edge, h - ch - Edge); // beside / clamped
        }

        double left = Clamp(target.Left, Edge, w - cw - Edge);
        // If placing beside (no vertical room), prefer the target's right/left side.
        if (top != target.Bottom + Gap && top != target.Top - Gap - ch)
        {
            left = target.Right + Gap + cw <= w ? target.Right + Gap : Clamp(target.Left - Gap - cw, Edge, w - cw - Edge);
        }

        Canvas.SetLeft(Callout, Clamp(left, Edge, w - cw - Edge));
        Canvas.SetTop(Callout, Clamp(top, Edge, h - ch - Edge));
    }

    private void PlaceCalloutCentered(double w, double h)
    {
        (double cw, double ch) = MeasureCallout();
        Canvas.SetLeft(Callout, Clamp((w - cw) / 2, Edge, w - cw - Edge));
        Canvas.SetTop(Callout, Clamp((h - ch) / 2, Edge, h - ch - Edge));
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Clamp(value, min, max);
}
