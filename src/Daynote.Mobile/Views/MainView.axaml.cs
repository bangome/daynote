using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Input.TextInput;
using Avalonia.Markup.Xaml;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>
/// The phone's single view: it paints the whole screen, notch and home indicator included, and
/// keeps its content clear of them.
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>How the to-do lists know a row across rebuilds, for the arrival in M2.</summary>
    static MainView()
    {
        Daynote.Motion.RowArrivals.RegisterKey<ViewModels.TodoRowViewModel>(static row => row.Item.Key);
        Daynote.Motion.RowArrivals.RegisterKey<Daynote.App.Shell.Product.TodoItemViewModel>(static todo => todo.Key);
    }

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
    /// How much of the content an on-screen keyboard covers, in points, when there is no platform
    /// to report one. Off a device only, beside <see cref="PreviewSafeArea"/>.
    /// </summary>
    public double? PreviewKeyboard
    {
        get => _previewKeyboard;
        set
        {
            _previewKeyboard = value;
            Apply(TopLevel.GetTopLevel(this));
        }
    }

    private double? _previewKeyboard;

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
    /// content pushed back up by the same amount. The floating tab bar does not: on a device the
    /// content is clipped where the strip begins, so the bar sits above it (see DockBottomMargin).
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
        FollowDevice(true);

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
        FollowDevice(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void Apply(TopLevel? top)
    {
        double bottom = _previewSafeArea is { } preview ? preview.Bottom : _safeArea.Bottom;

        // The keyboard's rectangle is in the window's coordinates and the content stops above the
        // home indicator, so the part of the keyboard over the content runs from its top edge to
        // there. (On a Pixel 9 Pro: a window 952 tall, the content ending at 928, the keyboard's top
        // at 616, so 312 points of it cover the content.)
        double keyboard = _previewKeyboard ?? (_keyboardTop is { } keyboardTop && top is not null
            ? Math.Max(0, top.ClientSize.Height - bottom - keyboardTop)
            : 0);

        // The bar keeps clear of the home-indicator strip or the navigation buttons, and of the
        // edge when there is neither. The design dips it 4 points into the strip, but on a device
        // the content stops where the strip begins and anything drawn past it is clipped: on an
        // iPhone that cut the bar's round bottom flat, and the shadow under it everywhere.
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

        // The attachment sheets sit where the month sheet does.
        Bleed(this.FindControl<Border>("AttachSheet"), bottom, extra: 6, fallback: 24);
        Bleed(this.FindControl<Border>("FileSheet"), bottom, extra: 6, fallback: 24);
        Bleed(this.FindControl<Border>("ListSheet"), bottom, extra: 6, fallback: 24);
        Bleed(this.FindControl<Border>("ReminderTimeSheet"), bottom, extra: 6, fallback: 24);
        if (this.FindControl<Border>("ReminderTimeSheet") is { } timeSheet)
        {
            // Taller than a phone on its side: it scrolls, under a sliver of scrim that still closes it.
            timeSheet.Margin = new Thickness(0, 24, 0, timeSheet.Margin.Bottom);
        }

        // The to-do sheet's title field brings the keyboard up with it, so the sheet stands on the
        // keyboard's top edge while it is up, and sits where the others do once it goes down.
        if (this.FindControl<Border>("TodoSheet") is { } todoSheet)
        {
            if (keyboard > 0)
            {
                todoSheet.Margin = new Thickness(0, 24, 0, keyboard);
                todoSheet.Padding = new Thickness(0, 0, 0, 10);
            }
            else
            {
                Bleed(todoSheet, bottom, extra: 6, fallback: 24);
                todoSheet.Margin = new Thickness(0, 24, 0, todoSheet.Margin.Bottom);
            }
        }

        // The image viewer's dark ground covers the whole screen, notch and strip included, with its
        // bar and buttons kept inside the safe area.
        if (this.FindControl<Border>("Viewer") is { } viewer)
        {
            Thickness safe = _previewSafeArea ?? _safeArea;
            viewer.Margin = new Thickness(-safe.Left, -safe.Top, -safe.Right, -safe.Bottom);
            viewer.Padding = new Thickness(safe.Left, safe.Top, safe.Right, safe.Bottom);
        }
    }

    /// <summary>
    /// Puts the caret in the to-do sheet's title, which is what brings the keyboard up: the title is
    /// the one field the sheet cannot do without. Posted, because the sheet is laid out on the next pass
    /// and a control that is not yet visible takes no focus.
    /// </summary>
    private void FocusTodoText() => Avalonia.Threading.Dispatcher.UIThread.Post(
        () => this.FindControl<TextBox>("TodoTextBox")?.Focus(),
        Avalonia.Threading.DispatcherPriority.Loaded);

    // ── The image viewer's zoom ──────────────────────────────────────────────────────────────────

    private const double MaxZoom = 4;
    private double _zoom = 1;
    private double _pinchStartZoom = 1;

    /// <summary>A new picture starts unzoomed.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        PlaceFrame(Bounds.Size);
        if (DataContext is ViewModels.MobileShellViewModel shell)
        {
            shell.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ViewModels.MobileShellViewModel.ViewerImage))
                {
                    SetZoom(1);
                }
                else if (args.PropertyName == nameof(ViewModels.MobileShellViewModel.IsTodoSheetOpen) && shell.IsTodoSheetOpen)
                {
                    FocusTodoText();
                }
            };
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (this.FindControl<Border>("ViewerStage") is { } stage)
        {
            stage.AddHandler(InputElement.PinchEvent, OnViewerPinch);
            stage.AddHandler(InputElement.PinchEndedEvent, (_, _) => _pinchStartZoom = _zoom);
        }
    }

    /// <summary>Double tap: in to twice the size, and back out.</summary>
    private void OnViewerDoubleTapped(object? sender, TappedEventArgs e) => SetZoom(_zoom > 1 ? 1 : 2);

    private void OnViewerPinch(object? sender, PinchEventArgs e) => SetZoom(_pinchStartZoom * e.Scale);

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 1, MaxZoom);
        if (_zoom == 1)
        {
            _pinchStartZoom = 1;
        }

        if (this.FindControl<Image>("ViewerImage") is { } image)
        {
            image.RenderTransform = new ScaleTransform(_zoom, _zoom);
        }
    }

    // ── The layout by width (Daynote Tablet §00, Foldables §00) ─────────────────────────────────

    private Platform.IDeviceShape? _device;
    private MobileLayout? _shownLayout;
    private Avalonia.Threading.DispatcherTimer? _settle;
    private IReadOnlyList<Control> _waiting = [];

    /// <summary>The rail's width in the two-pane layout.</summary>
    private const double RailWidth = 72;

    /// <summary>The tablet's sidebar and day panel.</summary>
    private const double SidebarWidth = 260;

    private const double DayPanelWidth = 300;

    /// <summary>
    /// The hinge and the keyboard, from the head. Null off a device, where there is neither.
    /// </summary>
    public Platform.IDeviceShape? Device
    {
        get => _device;
        set
        {
            // The source is the head's, one per process, and outlives this view: a profile switch
            // builds a new view, and a subscription held past detach would keep the old view and its
            // shell alive. So it is held only while attached.
            if (_device is not null && _deviceFollowed)
            {
                _device.Changed -= OnDeviceChanged;
                _deviceFollowed = false;
            }

            _device = value;
            FollowDevice(TopLevel.GetTopLevel(this) is not null);
            OnDeviceChanged(null, EventArgs.Empty);
        }
    }

    private bool _deviceFollowed;

    private void FollowDevice(bool follow)
    {
        if (_device is null || follow == _deviceFollowed)
        {
            return;
        }

        if (follow)
        {
            _device.Changed += OnDeviceChanged;
        }
        else
        {
            _device.Changed -= OnDeviceChanged;
        }

        _deviceFollowed = follow;
    }

    private void OnDeviceChanged(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PlaceFrame(Bounds.Size));

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PlaceFrame(e.NewSize);
    }

    /// <summary>
    /// Chooses the layout for <paramref name="size"/> and puts the parts in their columns.
    /// </summary>
    /// <remarks>
    /// Runs on every size change: a Split View or Stage Manager resize goes through every width in
    /// between, and the layout follows it live. The panel that a new layout brings in (M7) waits
    /// until the width has stopped changing, so the app's one movement does not race the system's.
    /// </remarks>
    private void PlaceFrame(Size size)
    {
        double width = size.Width;
        if (width <= 0 || DataContext is not ViewModels.MobileShellViewModel shell ||
            this.FindControl<Grid>("Frame") is not { } frame ||
            this.FindControl<Grid>("PageColumn") is not { } pages)
        {
            return;
        }

        MobileLayout layout = MobileLayouts.For(width, size.Height);
        shell.Layout = layout;

        double rail = layout == MobileLayout.TwoPane ? RailWidth : 0;
        double panel = layout == MobileLayout.TwoPane ? PanelWidth(width) : 0;
        double sidebar = layout == MobileLayout.Tablet ? SidebarWidth : 0;
        double day = layout is MobileLayout.Tablet or MobileLayout.TabletCompact ? DayPanelWidth : 0;
        SetColumns(frame, Math.Max(rail, sidebar), panel, day);
        Grid.SetColumn(pages, layout == MobileLayout.TwoPane ? 1 : 2);

        if (_shownLayout is { } previous && previous != layout)
        {
            AwaitSettledWidth(Appearing(previous, layout));
        }

        _shownLayout = layout;
    }

    /// <summary>
    /// The panel's width beside the note. On a foldable whose hinge runs down the window, the
    /// panel ends where the hinge begins, so nothing straddles the fold (Foldables §01); otherwise
    /// half the window, within what a list reads well at.
    /// </summary>
    internal double PanelWidth(double width)
    {
        // The frame starts after the left safe-area inset (a cutout in landscape), the hinge at the
        // view's own corner.
        double left = (_previewSafeArea ?? _safeArea).Left;
        if (_device?.Hinge is { } hinge && hinge.Height >= hinge.Width &&
            hinge.X - left > RailWidth + 200 && hinge.X - left < width - 200)
        {
            return hinge.X - left - RailWidth;
        }

        return Math.Clamp((width / 2) - RailWidth, 280, 360);
    }

    private static void SetColumns(Grid frame, double side, double panel, double day)
    {
        frame.ColumnDefinitions[0].Width = new GridLength(side);
        frame.ColumnDefinitions[1].Width = new GridLength(panel);
        frame.ColumnDefinitions[3].Width = new GridLength(day);
    }

    /// <summary>What a change of layout brings in from the side: the panels, never the note (M7).</summary>
    private IReadOnlyList<Control> Appearing(MobileLayout from, MobileLayout to)
    {
        var appearing = new List<Control>();
        void Add(string name)
        {
            if (this.FindControl<Control>(name) is { } control)
            {
                appearing.Add(control);
            }
        }

        if (to == MobileLayout.TwoPane && from != MobileLayout.TwoPane)
        {
            Add("PageColumn");
        }

        if (to is MobileLayout.Tablet or MobileLayout.TabletCompact && from is MobileLayout.Phone or MobileLayout.TwoPane)
        {
            Add("DayPanel");
        }

        if (to == MobileLayout.Tablet && from != MobileLayout.Tablet)
        {
            Add("Sidebar");
        }

        return appearing;
    }

    /// <summary>
    /// Holds the appearing panels out of sight while the window is still being resized, then plays
    /// M7 on them once it has been still for a moment.
    /// </summary>
    private void AwaitSettledWidth(IReadOnlyList<Control> appearing)
    {
        // A resize that crossed another boundary before the last one settled: what that one held
        // back is shown again, and only what this one brings in waits.
        foreach (Control waiting in _waiting.Except(appearing))
        {
            waiting.Opacity = 1;
        }

        _waiting = appearing;
        _settle?.Stop();
        if (appearing.Count == 0)
        {
            return;
        }

        foreach (Control panel in appearing)
        {
            panel.Opacity = 0;
        }

        _settle = new Avalonia.Threading.DispatcherTimer { Interval = SettleDelay };
        _settle.Tick += (_, _) =>
        {
            _settle?.Stop();
            _waiting = [];
            foreach (Control panel in appearing)
            {
                _ = Daynote.Motion.MotionPlayer.Play(panel, "m7", Daynote.Motion.Choreography.PanelAppear(panel));
            }
        };

        if (Daynote.Motion.MotionEnvironment.Instant)
        {
            _waiting = [];
            foreach (Control panel in appearing)
            {
                _ = Daynote.Motion.MotionPlayer.Play(panel, "m7", Daynote.Motion.Choreography.PanelAppear(panel));
            }

            return;
        }

        _settle.Start();
    }

    /// <summary>How long the width has to stay put before the new panel comes in.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(120);

    /// <summary>The floating tab bar's bottom margin over a bottom inset of <paramref name="bottom"/> points.</summary>
    public static double DockBottomMargin(double bottom) => bottom > 0 ? 12 : 16;


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
