using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Backup;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// Backup to a zip and staged restore, mirroring the WPF settings. A restore is only staged: the app
/// quits (flushing) and relaunches so <c>PendingRestore</c> applies it before the database opens.
/// </summary>
public sealed partial class DesktopSettingsViewModel
{
    [ObservableProperty]
    private string? _backupStatusText;

    public string BackupLabel => AppStrings.SettingsBackupLabel;
    public string BackupDesc => AppStrings.SettingsBackupDesc;
    public string BackupButton => AppStrings.BackupButton;
    public string RestoreButton => AppStrings.RestoreButton;

    [RelayCommand]
    private async Task BackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (!await _flushAsync().ConfigureAwait(true))
            {
                BackupStatusText = AppStrings.BackupFlushBlocked;
                return;
            }

            string defaultName = string.Create(
                CultureInfo.InvariantCulture, $"Daynote-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            string? destination = await _backupPicker.PickSaveZipAsync(defaultName).ConfigureAwait(true);
            if (destination is null)
            {
                return;
            }

            BackupStatusText = AppStrings.BackupInProgress;
            await _backup.CreateBackupAsync(destination).ConfigureAwait(true);
            BackupStatusText = AppStrings.BackupSucceeded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            BackupStatusText = AppStrings.BackupFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        string? source = await _backupPicker.PickOpenZipAsync().ConfigureAwait(true);
        if (source is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            RestoreStageResult result = await _backup.StageRestoreAsync(source).ConfigureAwait(true);
            switch (result.Status)
            {
                case RestoreStageStatus.Staged:
                    BackupStatusText = AppStrings.RestoreStagedRestarting;
                    _requestRestartForRestore();
                    break;
                case RestoreStageStatus.IncompatibleVersion:
                    BackupStatusText = AppStrings.RestoreIncompatible;
                    break;
                case RestoreStageStatus.InvalidArchive:
                    BackupStatusText = AppStrings.RestoreInvalid;
                    break;
                default:
                    BackupStatusText = AppStrings.RestoreFailed;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
