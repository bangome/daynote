using Daynote.App.Account;
using Daynote.App.Notes;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// Automatic sync and what it does to the screen: <see cref="SyncScheduler"/> decides when, and a run
/// that pulled something re-reads every surface that shows notes, so a note written on another
/// device appears without the user having to click away and back.
/// </summary>
public sealed partial class DesktopShellViewModel
{
    private SyncScheduler? _syncScheduler;

    /// <summary>
    /// Starts syncing on its own. Called once the stored sign-in has been read, so the first run
    /// knows whether there is anyone to sync for. Nothing happens in a build without sync.
    /// </summary>
    public void StartAutoSync()
    {
        if (Account is not { } account || _syncScheduler is not null || _disposed)
        {
            return;
        }

        _syncScheduler = SyncScheduler.Attach(account, Notes, RefreshAfterSyncAsync);
        _syncScheduler.Start();
    }

    /// <summary>
    /// The window came to the front — back from the tray, the Dock, or another app. Often the
    /// browser, after a payment, so a checkout waiting on the server re-reads the billing state too.
    /// </summary>
    public void NotifyActivated()
    {
        _syncScheduler?.NotifyResumed();
        Account?.NotifyActivated();
    }

    private async Task RefreshAfterSyncAsync(SyncReloadResult reload)
    {
        // A rename in progress was aimed at the note that is gone; committing it now would rename
        // whichever note the reload selected instead.
        if (reload == SyncReloadResult.OpenNoteRemoved)
        {
            IsRenamingTitle = false;
        }

        await Files.LoadForDateAsync(SelectedDate).ConfigureAwait(true);
        await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        if (IsTimelineMode)
        {
            await Timeline.LoadAsync().ConfigureAwait(true);
        }
    }
}
