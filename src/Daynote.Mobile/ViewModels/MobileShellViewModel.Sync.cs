using Daynote.App.Account;
using Daynote.App.Notes;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// Automatic sync on a phone: <see cref="SyncScheduler"/> decides when, and a run that pulled
/// something re-reads the day, the calendar and the lists so the other device's notes appear
/// without re-selecting the date.
/// </summary>
public sealed partial class MobileShellViewModel
{
    private SyncScheduler? _syncScheduler;

    /// <summary>
    /// Starts syncing on its own, once the stored sign-in has been read. Nothing happens in a build
    /// without sync.
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

    /// <summary>The app came back to the foreground.</summary>
    public void NotifyResumed() => _syncScheduler?.NotifyResumed();

    private async Task RefreshAfterSyncAsync(SyncReloadResult reload)
    {
        // The full-screen editor was showing a note another device has since deleted. Leaving it up
        // would quietly swap in whichever note the day now opens with, so go back to the list.
        if (reload == SyncReloadResult.OpenNoteRemoved && IsEditorOpen)
        {
            IsRenamingTitle = false;
            IsEditorOpen = false;
        }

        await LoadDayFilesAsync(SelectedDate).ConfigureAwait(true);
        await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
    }
}
