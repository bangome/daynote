using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Daynote.App.Localization;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Instance;

namespace Daynote.Desktop.Lifecycle;

/// <summary>
/// The resident behaviour for the Avalonia shell, doing what <c>AppLifecycleCoordinator</c> and
/// <c>TrayIconService</c> do for WPF: a status-bar icon with Show/Quit, hide-to-tray on window close,
/// re-show from the icon, the Dock, or a second launch, and an explicit Quit that flushes dirty notes
/// and stays open if the flush fails.
/// </summary>
public sealed class ResidentLifecycle : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly Window _window;
    private readonly Func<FlushReason, CancellationToken, Task<FlushResult>> _flush;
    private readonly SingleInstanceCoordinator? _singleInstance;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _showItem;
    private readonly NativeMenuItem _quitItem;
    private bool _quitting;

    public ResidentLifecycle(
        Application application,
        IClassicDesktopStyleApplicationLifetime desktop,
        Window window,
        WindowIcon icon,
        Func<FlushReason, CancellationToken, Task<FlushResult>> flush,
        SingleInstanceCoordinator? singleInstance)
    {
        ArgumentNullException.ThrowIfNull(application);
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _flush = flush ?? throw new ArgumentNullException(nameof(flush));
        _singleInstance = singleInstance;

        _showItem = new NativeMenuItem(AppStrings.TrayShow);
        _showItem.Click += (_, _) => ShowWindow();
        _quitItem = new NativeMenuItem(AppStrings.TrayQuit);
        _quitItem.Click += (_, _) => _ = QuitAsync();

        _tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Daynote",
            Menu = new NativeMenu { Items = { _showItem, new NativeMenuItemSeparator(), _quitItem } },
        };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(application, [_tray]);

        // The red close button hides; the process stays resident.
        _window.Closing += OnWindowClosing;

        // Cmd+Q / the Dock's Quit: run the same flush-guarded path instead of dying mid-save.
        _desktop.ShutdownRequested += OnShutdownRequested;

        // Clicking the Dock icon while the window is hidden ("Reopen" in AppKit terms).
        if (application.TryGetFeature<IActivatableLifetime>() is { } activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen)
                {
                    ShowWindow();
                }
            };
        }

        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested += OnActivationRequested;
        }

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public void ShowWindow()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    public void HideToTray() => _window.Hide();

    /// <summary>Flushes dirty notes; on failure the window is shown and the process stays alive.</summary>
    public async Task<bool> QuitAsync(CancellationToken cancellationToken = default)
    {
        if (_quitting)
        {
            return false;
        }

        FlushResult flush = await _flush(FlushReason.Quit, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            ShowWindow();
            return false;
        }

        _quitting = true;
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnActivationRequested;
            await _singleInstance.DisposeAsync().ConfigureAwait(true);
        }

        Dispose();
        _desktop.Shutdown();
        return true;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_quitting)
        {
            return;
        }

        e.Cancel = true;
        _ = QuitAsync();
    }

    private void OnActivationRequested(object? sender, EventArgs e) => Dispatcher.UIThread.Post(ShowWindow);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _showItem.Header = AppStrings.TrayShow;
        _quitItem.Header = AppStrings.TrayQuit;
    }

    public void Dispose()
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        _window.Closing -= OnWindowClosing;
        _desktop.ShutdownRequested -= OnShutdownRequested;
        _tray.IsVisible = false;
        _tray.Dispose();
    }
}
