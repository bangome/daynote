using System.ComponentModel;
using Daynote.App.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// Decides when a signed-in device syncs without being asked (docs/CLOUD_SYNC.md §7.5): once at
/// start, when the app comes back to the foreground, a short while after a clean save, and on an
/// idle timer. Without it the only sync was the button, so two devices could sit side by side for a
/// day and never exchange a note.
/// </summary>
/// <remarks>
/// Every trigger funnels into one run that stands aside while the editor holds unsaved text: a pull
/// must never race a buffer the user is still typing into. The save that ends that edit re-arms the
/// debounce, so a skipped run is only postponed.
/// <para>
/// All callbacks are expected on the UI thread and the delays resume on the context they started on,
/// which keeps the run on that thread too — the account view model it drives is bound to the UI.
/// </para>
/// </remarks>
public sealed class SyncScheduler : IDisposable
{
    /// <summary>After a clean save. Long enough that a burst of edits is one sync, not one each.</summary>
    public static readonly TimeSpan AfterSaveDelay = TimeSpan.FromSeconds(10);

    /// <summary>With nothing else happening, so a device left open still picks up the others' notes.</summary>
    public static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Coming back to the foreground within this long of the last run does not start another:
    /// switching windows back and forth is not a reason to hit the network each time.
    /// </summary>
    public static readonly TimeSpan ResumeCooldown = TimeSpan.FromSeconds(30);

    private readonly Func<Task<bool>> _sync;
    private readonly Func<bool> _hasPendingEdits;
    private readonly IAutosaveScheduler _delays;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _afterSave;
    private CancellationTokenSource? _idle;
    private Action? _detach;
    private DateTimeOffset? _lastRun;
    private bool _running;
    private bool _runAgain;
    private bool _disposed;

    /// <param name="sync">One background run; returns false when it was skipped.</param>
    /// <param name="hasPendingEdits">True while the editor holds text not yet saved.</param>
    /// <param name="delays">The delay seam; tests pass one they release by hand.</param>
    /// <param name="clock">Reads "now" for the resume cooldown.</param>
    public SyncScheduler(
        Func<Task<bool>> sync,
        Func<bool> hasPendingEdits,
        IAutosaveScheduler? delays = null,
        TimeProvider? clock = null)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _hasPendingEdits = hasPendingEdits ?? throw new ArgumentNullException(nameof(hasPendingEdits));
        _delays = delays ?? new SystemAutosaveScheduler();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Wires a scheduler to the account and the note workspace a shell owns: clean saves arm the
    /// debounce, and a run that wrote to the database has <paramref name="refreshViews"/> re-read
    /// what is on screen. Disposing the result unhooks both.
    /// </summary>
    /// <param name="refreshViews">
    /// Re-reads everything that lists or counts notes — calendar, rails, files — after the workspace
    /// itself has been reloaded, told how that reload went so a shell can close an editor whose
    /// note another device deleted.
    /// </param>
    public static SyncScheduler Attach(
        AccountViewModel account,
        NoteWorkspaceViewModel notes,
        Func<SyncReloadResult, Task> refreshViews,
        IAutosaveScheduler? delays = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(refreshViews);

        var scheduler = new SyncScheduler(account.SyncInBackgroundAsync, () => notes.HasPendingEdits, delays, clock);

        // A run can pull while the editor holds unsaved text (the button does not wait), and then the
        // workspace is left alone. It is re-read once that text is safely saved, or the pulled notes
        // would stay invisible until the user happened to navigate.
        bool refreshOwed = false;

        async Task RefreshAsync()
        {
            // Owed until it has actually happened, so a failure below is retried on the next save.
            refreshOwed = true;
            try
            {
                SyncReloadResult reload = await notes.ReloadAfterSyncAsync().ConfigureAwait(true);
                await refreshViews(reload).ConfigureAwait(true);
                refreshOwed = reload == SyncReloadResult.Skipped;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Nothing awaits this; the views keep what they showed, which is where they were
                // before sync learned to refresh them.
                System.Diagnostics.Debug.WriteLine($"Refresh after sync failed: {exception}");
            }
        }

        void OnNotesChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NoteWorkspaceViewModel.SaveStatus) && notes.SaveStatus == SaveStatusKind.Saved)
            {
                scheduler.NotifySaved();
                if (refreshOwed && !scheduler._disposed)
                {
                    _ = RefreshAsync();
                }
            }
        }

        void OnSynced(object? sender, SyncReport report)
        {
            if (report.ChangedLocalData && !scheduler._disposed)
            {
                _ = RefreshAsync();
            }
        }

        notes.PropertyChanged += OnNotesChanged;
        account.Synced += OnSynced;
        scheduler._detach = () =>
        {
            notes.PropertyChanged -= OnNotesChanged;
            account.Synced -= OnSynced;
        };
        return scheduler;
    }

    /// <summary>Starting the app counts as coming to the foreground; this also arms the idle timer.</summary>
    public void Start() => _ = RunAsync();

    /// <summary>A save just landed. Restarts the debounce, so only the last of a burst syncs.</summary>
    public void NotifySaved()
    {
        if (_disposed)
        {
            return;
        }

        Replace(ref _afterSave, out CancellationToken token);
        _ = RunAfterAsync(AfterSaveDelay, token);
    }

    /// <summary>The app came back to the foreground (window activated, phone app resumed).</summary>
    public void NotifyResumed()
    {
        if (_disposed)
        {
            return;
        }

        if (_lastRun is { } last && _clock.GetUtcNow() - last < ResumeCooldown)
        {
            return;
        }

        _ = RunAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detach?.Invoke();
        _detach = null;
        Cancel(ref _afterSave);
        Cancel(ref _idle);
    }

    private async Task RunAfterAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await _delays.DelayAsync(delay, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        await RunAsync().ConfigureAwait(true);
    }

    private async Task RunAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_running)
        {
            // A save or a return to the foreground during a slow run is still news: the run in
            // flight may already have pushed before it happened. Remember it rather than drop it.
            _runAgain = true;
            return;
        }

        try
        {
            if (_hasPendingEdits())
            {
                return;
            }

            _running = true;
            if (await _sync().ConfigureAwait(true))
            {
                _lastRun = _clock.GetUtcNow();
            }
        }
        finally
        {
            _running = false;
            if (_runAgain && !_disposed)
            {
                _runAgain = false;
                _ = RunAsync();
            }

            // Every exit re-arms the idle timer, skipped runs included, so the next check is always
            // a full interval after the last one rather than piling up behind it.
            if (!_disposed)
            {
                Replace(ref _idle, out CancellationToken token);
                _ = RunAfterAsync(IdleInterval, token);
            }
        }
    }

    private static void Replace(ref CancellationTokenSource? source, out CancellationToken token)
    {
        Cancel(ref source);
        source = new CancellationTokenSource();
        token = source.Token;
    }

    private static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }
}
