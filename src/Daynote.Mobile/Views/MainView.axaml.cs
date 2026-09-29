using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Markup.Xaml;

namespace Daynote.Mobile.Views;

/// <summary>
/// The phone's single view: it paints the whole screen, notch and home indicator included, and
/// keeps its content clear of them.
/// </summary>
public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>The last safe area the platform reported, in that platform's units.</summary>
    private Thickness _safeArea;
    private Action? _detach;

    /// <summary>
    /// A safe area to lay out against when there is no platform to report one, in points.
    /// </summary>
    /// <remarks>
    /// For rendering the screens off a device, at the size of a phone with a notch: the content is
    /// inset by it the way the platform insets it, and the surfaces that run under the home
    /// indicator do so here too. Left null on a device, where the platform's own value is used.
    /// </remarks>
    public Thickness? PreviewSafeArea
    {
        get => _previewSafeArea;
        set
        {
            _previewSafeArea = value;
            Padding = value is { } inset ? new Thickness(0, inset.Top, 0, inset.Bottom) : default;
            Apply(TopLevel.GetTopLevel(this));
        }
    }

    private Thickness? _previewSafeArea;

    /// <summary>
    /// Goes edge to edge and lets the surfaces at the bottom run under the home indicator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Edge to edge is what puts the app's own colour behind the notch and the status bar instead
    /// of a band of the platform's. Avalonia then keeps the content itself inside the safe area on
    /// its own, so nothing here adds a top inset — an earlier version did and the month header came
    /// out twice as far down the screen as it should have been.
    /// </para>
    /// <para>
    /// What Avalonia's own handling cannot give is a surface whose fill reaches the bottom edge: it
    /// stops above the home indicator and the page colour shows through beneath. The editor's
    /// toolbar and the month sheet are pulled down into that strip with a negative margin and their
    /// content pushed back up by the same amount. The floating tab bar dips into it by the few
    /// points the design has it do.
    /// </para>
    /// <para>
    /// The inset arrives in different units on the two platforms. Android reports window insets in
    /// physical pixels, so on a 3x screen its 72-pixel gesture bar has to be divided by
    /// <see cref="TopLevel.RenderScaling"/> to become the 24 points a <see cref="Thickness"/> means;
    /// iOS passes UIKit's safe-area insets through in points already. Neither the inset nor the
    /// scaling is settled at attach — RenderScaling reads 1 and ClientSize 1x1 until the surface
    /// exists — which is why this re-applies on both changes and once more after the first layout.
    /// </para>
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is not { InsetsManager: { } insets } top)
        {
            Apply(TopLevel.GetTopLevel(this));
            return;
        }

        insets.DisplayEdgeToEdgePreference = true;

        _safeArea = insets.SafeAreaPadding;
        EventHandler<SafeAreaChangedArgs> onSafeArea = (_, args) =>
        {
            _safeArea = args.SafeAreaPadding;
            Apply(top);
        };
        EventHandler onScaling = (_, _) => Apply(top);
        insets.SafeAreaChanged += onSafeArea;
        top.ScalingChanged += onScaling;

        // A profile switch replaces this view while the top level lives on, so the handlers go with it.
        _detach = () =>
        {
            insets.SafeAreaChanged -= onSafeArea;
            top.ScalingChanged -= onScaling;
        };

        Apply(top);
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => Apply(top), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _detach?.Invoke();
        _detach = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Apply(TopLevel? top)
    {
        double bottom;
        if (_previewSafeArea is { } preview)
        {
            bottom = preview.Bottom;
        }
        else
        {
            double scale = OperatingSystem.IsAndroid() && top is { RenderScaling: > 0 } ? top.RenderScaling : 1;
            bottom = _safeArea.Bottom / scale;
        }

        // The bar sits 30 points off the bottom edge of a 34-point home-indicator strip, so 4 into
        // it; with no strip it keeps clear of the edge instead of touching it.
        if (this.FindControl<Grid>("Dock") is { } dock)
        {
            dock.Margin = new Thickness(16, 0, 16, bottom > 0 ? -Math.Min(4, bottom) : 16);
        }

        // The sheet's content ends 40 points above the screen edge in the design, 6 above the strip.
        Bleed(this.FindControl<Border>("Sheet"), bottom, extra: 6, fallback: 24);
        this.FindControl<EditorPage>("Editor")?.SetBottomInset(bottom);
    }

    /// <summary>
    /// Runs a bottom surface's fill under the home indicator and keeps its content above it, by
    /// <paramref name="extra"/> more; on a screen with no indicator, <paramref name="fallback"/> from the edge.
    /// </summary>
    internal static void Bleed(Border? surface, double bottom, double extra, double fallback)
    {
        if (surface is null)
        {
            return;
        }

        surface.Margin = new Thickness(0, 0, 0, -bottom);
        surface.Padding = new Thickness(0, 0, 0, bottom > 0 ? bottom + extra : fallback);
    }
}
