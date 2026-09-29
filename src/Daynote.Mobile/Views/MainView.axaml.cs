using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input.TextInput;
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

    /// <summary>The last safe area the platform reported, in points.</summary>
    private Thickness _safeArea;

    /// <summary>Where the on-screen keyboard's top edge is, in points from the top, or null when it is down.</summary>
    private double? _keyboardTop;
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
            Padding = value ?? default;
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
    /// Both platforms report the inset in points. Android used to report it in physical pixels, and
    /// this divided it by <see cref="TopLevel.RenderScaling"/>; Avalonia 12.1 converts it itself (a
    /// Pixel 9 Pro at 3x reports 0,52,0,24, the status bar and the gesture bar in points), and the
    /// division then pulled the bars down by a third of the strip. The inset is not settled at
    /// attach, which is why this re-applies on every change and once more after the first layout.
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

        // Edge to edge, the window is not resized for the keyboard, so the editor's helper bar would
        // sit under it: it follows the keyboard's top edge instead.
        EventHandler<InputPaneStateEventArgs>? onKeyboard = null;
        if (top.InputPane is { } pane)
        {
            onKeyboard = (_, args) =>
            {
                _keyboardTop = args.NewState == InputPaneState.Open && args.EndRect.Height > 0 ? args.EndRect.Top : null;
                Apply(top);
            };
            pane.StateChanged += onKeyboard;
        }

        // A profile switch replaces this view while the top level lives on, so the handlers go with it.
        _detach = () =>
        {
            insets.SafeAreaChanged -= onSafeArea;
            top.ScalingChanged -= onScaling;
            if (onKeyboard is not null && top.InputPane is { } pane)
            {
                pane.StateChanged -= onKeyboard;
            }
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
        double bottom = _previewSafeArea is { } preview ? preview.Bottom : _safeArea.Bottom;

        // The keyboard's rectangle is in the window's coordinates and the content stops above the
        // home indicator, so the part of the keyboard over the content runs from its top edge to
        // there. (On a Pixel 9 Pro: a window 952 tall, the content ending at 928, the keyboard's top
        // at 616, so 312 points of it cover the content.)
        double keyboard = _keyboardTop is { } keyboardTop && top is not null
            ? Math.Max(0, top.ClientSize.Height - bottom - keyboardTop)
            : 0;

        // The bar sits 30 points off the bottom edge of a 34-point home-indicator strip, so 4 into
        // it. Anything taller is a bar of buttons (Android's three-button navigation reports 48),
        // which the bar keeps clear of instead of sitting on; with no strip at all it keeps clear of
        // the edge.
        if (this.FindControl<Grid>("Dock") is { } dock)
        {
            dock.Margin = new Thickness(16, 0, 16, DockBottomMargin(bottom));
        }

        // The sheet's content ends 40 points above the screen edge in the design, 6 above the strip.
        Bleed(this.FindControl<Border>("Sheet"), bottom, extra: 6, fallback: 24);
        if (this.FindControl<Border>("Sheet") is { } sheet)
        {
            // When the sheet is taller than the screen it scrolls; this keeps a sliver of scrim above
            // it, so it still reads as a sheet and the tap-to-close area never vanishes.
            sheet.Margin = new Thickness(0, 24, 0, sheet.Margin.Bottom);
        }

        this.FindControl<EditorPage>("Editor")?.SetBottomInset(bottom, keyboard);
    }

    /// <summary>The floating tab bar's bottom margin over a bottom inset of <paramref name="bottom"/> points.</summary>
    public static double DockBottomMargin(double bottom) => bottom switch
    {
        <= 0 => 16,
        <= HomeIndicatorStrip => -Math.Min(4, bottom),
        _ => 8,
    };

    /// <summary>The tallest bottom inset that is a home-indicator strip rather than a bar of buttons.</summary>
    public const double HomeIndicatorStrip = 34;

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
