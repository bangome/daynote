using Daynote.Core.Diagnostics;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Instance;

namespace Daynote.App.Lifecycle;

/// <summary>
/// The resident lifecycle owner. It hides to the tray on close (the process stays alive), activates on
/// Show, and on explicit Quit flushes dirty notes — remaining running on failure — before disposing the
/// tray and single-instance pipe and shutting down (plan Todo 10; DESIGN Sections 1, 5).
/// </summary>
public sealed class AppLifecycleCoordinator : IAsyncDisposable
{
    private readonly ITrayPresenter _tray;
    private readonly IWindowHost _window;
    private readonly IApplicationExit _exit;
    private readonly Func<FlushReason, CancellationToken, Task<FlushResult>> _flush;
    private readonly ISanitizedLog _log;
    private readonly SingleInstanceCoordinator? _singleInstance;
    private readonly SynchronizationContext? _sync;

    private bool _quitting;
    private bool _disposed;

    public AppLifecycleCoordinator(
        ITrayPresenter tray,
        IWindowHost window,
        IApplicationExit exit,
        Func<FlushReason, CancellationToken, Task<FlushResult>> flush,
        ISanitizedLog? log = null,
        SingleInstanceCoordinator? singleInstance = null)
    {
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _flush = flush ?? throw new ArgumentNullException(nameof(flush));
        _log = log ?? NullSanitizedLog.Instance;
        _singleInstance = singleInstance;
        _sync = SynchronizationContext.Current;

        _tray.ShowRequested += OnShowRequested;
        _tray.SettingsRequested += OnSettingsRequested;
        _tray.QuitRequested += OnQuitRequested;
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested += OnActivationRequested;
        }

        _log.Record(LifecycleEvent.PrimaryInstanceStarted);
    }

    /// <summary>Handles the window close as hide-to-tray. The process stays alive.</summary>
    public void HideToTray()
    {
        _window.HideToTray();
        _tray.UpdateWindowShown(false);
        _log.Record(LifecycleEvent.WindowHiddenToTray);
    }

    public void ShowWindow()
    {
        _window.ShowAndActivate();
        _tray.UpdateWindowShown(true);
        _log.Record(LifecycleEvent.WindowShownFromTray);
    }

    /// <summary>
    /// Explicit Quit: flushes dirty notes. On flush failure the app stays open with dirty text retained;
    /// on success it disposes the tray and single-instance pipe and shuts down.
    /// </summary>
    public async Task<bool> QuitAsync(CancellationToken cancellationToken = default)
    {
        if (_quitting)
        {
            return false;
        }

        _log.Record(LifecycleEvent.QuitRequested);
        FlushResult flush = await _flush(FlushReason.Quit, cancellationToken).ConfigureAwait(false);
        if (!flush.CanProceed)
        {
            _log.Record(LifecycleEvent.QuitBlockedByFlushFailure);
            _window.ShowAndActivate();
            _tray.UpdateWindowShown(true);
            return false;
        }

        _quitting = true;
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnActivationRequested;
            await _singleInstance.DisposeAsync().ConfigureAwait(false);
        }

        _tray.Dispose();
        _log.Record(LifecycleEvent.QuitCompleted);
        _exit.Shutdown();
        return true;
    }

    private void OnShowRequested(object? sender, EventArgs e) => ShowWindow();

    private void OnActivationRequested(object? sender, EventArgs e)
    {
        _log.Record(LifecycleEvent.SecondaryInstanceActivatedPrimary);
        Post(ShowWindow);
    }

    private void OnSettingsRequested(object? sender, EventArgs e) => Post(_window.ShowSettings);

    private void OnQuitRequested(object? sender, EventArgs e) => _ = QuitAsync();

    private void Post(Action action)
    {
        if (_sync is null)
        {
            action();
        }
        else
        {
            _sync.Post(_ => action(), null);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _tray.ShowRequested -= OnShowRequested;
        _tray.SettingsRequested -= OnSettingsRequested;
        _tray.QuitRequested -= OnQuitRequested;
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnActivationRequested;
        }

        return ValueTask.CompletedTask;
    }
}
