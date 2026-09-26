using Avalonia;
using Avalonia.Controls;
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

    /// <summary>
    /// Goes edge to edge and lets the bottom bar's fill run under the home indicator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Edge to edge is what puts the app's own colour behind the notch and the status bar instead
    /// of a band of the platform's. Avalonia then keeps the content itself inside the safe area on
    /// its own, so nothing here adds a top inset — an earlier version did and the month header came
    /// out twice as far down the screen as it should have been.
    /// </para>
    /// <para>
    /// The one thing Avalonia's own handling cannot give is a bar whose fill reaches the bottom
    /// edge: it stops the bar above the home indicator and the page colour shows through beneath.
    /// So the bar is pulled down into that strip with a negative margin and its content pushed back
    /// up by the same amount, which paints the strip and leaves the labels where they were.
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
            return;
        }

        insets.DisplayEdgeToEdgePreference = true;

        _safeArea = insets.SafeAreaPadding;
        insets.SafeAreaChanged += (_, args) =>
        {
            _safeArea = args.SafeAreaPadding;
            Apply(top);
        };
        top.ScalingChanged += (_, _) => Apply(top);
        Apply(top);
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => Apply(top), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Apply(TopLevel top)
    {
        if (this.FindControl<Border>("TabBar") is not { } bar)
        {
            return;
        }

        double scale = OperatingSystem.IsAndroid() && top.RenderScaling > 0 ? top.RenderScaling : 1;
        double bottom = _safeArea.Bottom / scale;

        bar.Margin = new Thickness(0, 0, 0, -bottom);
        bar.Padding = new Thickness(0, 0, 0, bottom);
    }
}
