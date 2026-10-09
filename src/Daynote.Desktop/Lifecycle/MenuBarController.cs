using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.Desktop.Platform;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;

namespace Daynote.Desktop.Lifecycle;

/// <summary>
/// The menu bar status item and its popover on the Mac, the tray icon and its flyout on Windows
/// (menu bar design §01, Motion M9).
/// </summary>
/// <remarks>
/// <b>Placement.</b> On the Mac the popover hangs under the status item, centred on it, which is
/// where <c>NSPopover</c> would put it; the item is asked where it is each time, so a shortcut
/// opening (no click to read a position from) lands in the same place. Without the item — the
/// setting is off — it takes the top-right corner under the bar. On Windows it sits in the corner
/// beside the notification area, on whichever edge the taskbar is.
/// <para>
/// <b>Motion.</b> A click opens with the platform's entrance; the shortcut opens at once, because
/// the person pressing it is already typing. Losing focus closes it with a 100 ms fade. Making
/// something does not close it: the row it made has to be seen arriving.
/// </para>
/// <para>
/// <b>Light and dark</b> follow the system rather than the app's theme switch, like everything
/// else in the menu bar.
/// </para>
/// </remarks>
public sealed class MenuBarController : IDisposable
{
    /// <summary>Space between the bar (or the taskbar) and the card.</summary>
    private const double Gap = 6;

    /// <summary>How close the card may come to the screen's side.</summary>
    private const double Inset = 8;

    /// <summary>
    /// A click on the status item first takes focus from the open popover, which closes it; the
    /// click itself arrives a moment later and must not open it again.
    /// </summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(350);

    private readonly Application application;
    private readonly MenuBarViewModel model;
    private readonly ResidentLifecycle lifecycle;
    private readonly IGlobalHotkeyService hotkeys;
    private readonly DesktopSettingsViewModel? settings;
    private readonly DispatcherTimer clockTimer;
    private readonly MacStatusItem? statusItem;
    private MenuBarPopover? popover;
    private bool closing;
    private bool anchoredBelow = true;
    private DateTime closedAtUtc;
    private int shownCount = -1;
    private bool disposed;

    public MenuBarController(
        Application application,
        MenuBarViewModel model,
        ResidentLifecycle lifecycle,
        IGlobalHotkeyService hotkeys,
        DesktopSettingsViewModel? settings)
    {
        this.application = application ?? throw new ArgumentNullException(nameof(application));
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        this.settings = settings;

        model.PropertyChanged += OnModelChanged;
        model.CloseRequested += OnCloseRequested;
        hotkeys.CapturePressed += OnCapturePressed;
        if (settings is not null)
        {
            settings.PropertyChanged += OnSettingsChanged;
        }

        if (OperatingSystem.IsMacOS())
        {
            statusItem = new MacStatusItem(StatusSymbol.TemplatePng(), "Daynote");
            statusItem.Clicked += (_, _) => Toggle(fromShortcut: false);
            statusItem.ShowRequested += (_, _) => lifecycle.ShowWindow();
            statusItem.QuitRequested += (_, _) => Forget(lifecycle.QuitAsync());
            lifecycle.SetTrayVisible(false);
        }
        else
        {
            lifecycle.TrayClicked = () => Toggle(fromShortcut: false);
        }

        // Nobody writes "it is now 14:01" or "it is now tomorrow", but both change what the item
        // and the popover say: a time goes red, and the count belongs to a new day.
        clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        clockTimer.Tick += (_, _) =>
        {
            if (!model.IsOpen)
            {
                model.Rebuild();
            }
        };
        clockTimer.Start();

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        OnLanguageChanged(null, EventArgs.Empty);
        ShowCount();
    }

    /// <summary>True while the popover is on screen.</summary>
    public bool IsOpen => popover is { IsVisible: true } && !closing;

    /// <summary>Reads the to-dos for the first count and applies the settings once they are loaded.</summary>
    public async Task InitializeAsync()
    {
        ApplySettings();
        await model.RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Something changed the to-dos elsewhere — the window's panels refreshed. Re-read for the
    /// count, unless the popover is open: then its own list is the one being looked at.
    /// </summary>
    public void NotifyAgendaChanged()
    {
        if (!model.IsOpen)
        {
            Forget(model.RefreshAsync());
        }
    }

    /// <summary>A click on the item, or the shortcut: opens the popover, or closes it if open.</summary>
    public void Toggle(bool fromShortcut)
    {
        if (popover is { IsVisible: true })
        {
            if (!closing)
            {
                Forget(CloseAsync(animated: !fromShortcut));
            }

            return;
        }

        if (!fromShortcut && DateTime.UtcNow - closedAtUtc < ReopenGuard)
        {
            return;
        }

        Open(fromShortcut);
    }

    /// <summary>Puts the popover away at once: the popover's own links to the main window.</summary>
    public void HideNow() => Forget(CloseAsync(animated: false));

    private void Open(bool fromShortcut)
    {
        popover ??= CreatePopover();
        popover.RequestedThemeVariant = SystemVariant();
        Forget(model.OpenAsync());

        popover.PlayEntrance(
            fromShortcut ? PopoverEntrance.None : OperatingSystem.IsMacOS() ? PopoverEntrance.Popover : PopoverEntrance.Flyout,
            ReduceMotion());
        Place();
        popover.Show();
        if (OperatingSystem.IsMacOS())
        {
            JoinActiveSpace(popover);
        }

        Place();
        popover.Activate();
        popover.FocusBox();
        if (OperatingSystem.IsMacOS())
        {
            statusItem?.SetHighlighted(true);
        }
    }

    private async Task CloseAsync(bool animated)
    {
        if (popover is not { IsVisible: true } window || closing)
        {
            return;
        }

        closing = true;
        try
        {
            if (animated)
            {
                await window.PlayExitAsync().ConfigureAwait(true);
            }

            window.Hide();
            model.Close();
        }
        finally
        {
            if (OperatingSystem.IsMacOS())
            {
                statusItem?.SetHighlighted(false);
            }

            closedAtUtc = DateTime.UtcNow;
            closing = false;
        }
    }

    private MenuBarPopover CreatePopover()
    {
        var window = new MenuBarPopover { DataContext = model };
        window.Deactivated += (_, _) => Forget(CloseAsync(animated: true));

        // The card grows as the readback, a notice or a new row appears. Below the menu bar it
        // grows downwards by itself; above a taskbar it has to be moved up to keep its bottom put.
        window.SizeChanged += (_, _) =>
        {
            if (!anchoredBelow || OperatingSystem.IsMacOS())
            {
                Place();
            }
        };
        return window;
    }

    /// <summary>Puts the window so the card, not the shadow margin around it, is where it belongs.</summary>
    private void Place()
    {
        if (popover is not { } window || window.Screens.Primary is not { } primary)
        {
            return;
        }

        if (window.CardHeight <= 0)
        {
            window.Measure(Size.Infinity);
        }

        Thickness margin = window.CardMargin;
        double width = window.CardWidth;
        double height = window.CardHeight;

        if (OperatingSystem.IsMacOS())
        {
            PlaceOnMac(window, primary, margin, width);
            return;
        }

        // Windows: the corner by the notification area, on the taskbar's side.
        double scale = primary.Scaling;
        PixelRect area = primary.WorkingArea;
        PixelRect bounds = primary.Bounds;
        anchoredBelow = area.Y > bounds.Y;
        double cardLeft = area.X > bounds.X
            ? area.X + (Gap * 2 * scale)
            : area.Right - ((width + (Gap * 2)) * scale);
        double cardTop = anchoredBelow
            ? area.Y + (Gap * 2 * scale)
            : area.Bottom - ((height + (Gap * 2)) * scale);
        window.Position = new PixelPoint(
            (int)Math.Round(cardLeft - (margin.Left * scale)),
            (int)Math.Round(cardTop - (margin.Top * scale)));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private void PlaceOnMac(MenuBarPopover window, Screen primary, Thickness margin, double width)
    {
        // Cocoa measures in points from the primary screen's bottom-left; Avalonia's screen units
        // are measured here rather than assumed, by comparing the two descriptions of one screen.
        CocoaRect main = ObjC.PrimaryScreenFrame();
        double units = primary.Bounds.Width / main.Width;
        anchoredBelow = true;

        double left;
        double top;
        if (statusItem?.Frame is { } item)
        {
            left = item.X + (item.Width / 2) - (width / 2);
            top = main.Top - item.Y + Gap;
        }
        else
        {
            PixelRect area = primary.WorkingArea;
            left = (area.Right / units) - Inset - width;
            top = (area.Y / units) + Gap;
        }

        // Kept on the screen the item is on, clear of its edges.
        Screen screen = window.Screens.ScreenFromPoint(new PixelPoint((int)(left * units), (int)(top * units))) ?? primary;
        double minimum = (screen.WorkingArea.X / units) + Inset;
        double maximum = (screen.WorkingArea.Right / units) - Inset - width;
        left = Math.Clamp(left, minimum, Math.Max(minimum, maximum));

        window.Position = new PixelPoint(
            (int)Math.Round((left - margin.Left) * units),
            (int)Math.Round((top - margin.Top) * units));
    }

    /// <summary>
    /// Runs a task nobody waits on, and keeps a failure in it from going unobserved: logged, not
    /// rethrown, because there is no caller left to hand it to and the menu bar must stay up.
    /// </summary>
    internal static async void Forget(Task task)
    {
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
        }
    }

    /// <summary>
    /// Opens on the Space the user is on, over a full-screen app too, the way a menu bar item's
    /// popover does — not on whichever desktop the window last lived on.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void JoinActiveSpace(Window window)
    {
        const nint MoveToActiveSpace = 1 << 1;
        const nint FullScreenAuxiliary = 1 << 8;
        if (window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSWindow: var handle } && handle != IntPtr.Zero)
        {
            ObjC.Send(handle, ObjC.Sel("setCollectionBehavior:"), MoveToActiveSpace | FullScreenAuxiliary);
        }
    }

    private void ShowCount()
    {
        int count = model.RemainingCount;
        if (count == shownCount)
        {
            return;
        }

        shownCount = count;
        string tip = string.Format(System.Globalization.CultureInfo.CurrentCulture, AppStrings.MenuBarStatusTooltipFormat, count);
        if (OperatingSystem.IsMacOS() && statusItem is not null)
        {
            statusItem.SetCount(count);
            statusItem.SetToolTip(tip);
        }
        else
        {
            lifecycle.SetTrayIcon(StatusSymbol.TrayIcon(count));
            lifecycle.SetTrayToolTip(tip);
        }
    }

    private void ApplySettings()
    {
        if (OperatingSystem.IsMacOS() && settings is not null)
        {
            statusItem?.SetVisible(settings.MenuBarVisible);
        }

        model.ChordText = MenuBarViewModel.FormatChord(hotkeys.CurrentCapture);
    }

    private ThemeVariant SystemVariant()
    {
        try
        {
            return application.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return ThemeVariant.Light;
        }
    }

    /// <summary>The system's reduce-motion preference: a fade in place of the scale or the slide.</summary>
    private static bool ReduceMotion()
    {
        if (OperatingSystem.IsMacOS())
        {
            return ObjC.ReduceMotion();
        }

        if (OperatingSystem.IsWindows())
        {
            return SystemParametersInfo(SpiGetClientAreaAnimation, 0, out bool animate, 0) && !animate;
        }

        return false;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MenuBarViewModel.RemainingCount))
        {
            ShowCount();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DesktopSettingsViewModel.MenuBarVisible)
            or nameof(DesktopSettingsViewModel.CaptureHotkeyDisplay))
        {
            ApplySettings();
        }
    }

    private void OnCapturePressed(object? sender, EventArgs e) => Toggle(fromShortcut: true);

    private void OnCloseRequested(object? sender, EventArgs e) => Forget(CloseAsync(animated: true));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsMacOS())
        {
            statusItem?.SetMenuTitles(AppStrings.TrayShow, AppStrings.TrayQuit);
        }

        shownCount = -1;
        ShowCount();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        clockTimer.Stop();
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        model.PropertyChanged -= OnModelChanged;
        model.CloseRequested -= OnCloseRequested;
        hotkeys.CapturePressed -= OnCapturePressed;
        if (settings is not null)
        {
            settings.PropertyChanged -= OnSettingsChanged;
        }

        if (OperatingSystem.IsMacOS())
        {
            statusItem?.Dispose();
        }

        popover?.Close();
    }

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
}
