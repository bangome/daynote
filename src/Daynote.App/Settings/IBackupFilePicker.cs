namespace Daynote.App.Settings;

/// <summary>Save/open dialogs for backup archives; a test seam parallel to <c>IFilePicker</c>.</summary>
public interface IBackupFilePicker
{
    /// <summary>Prompts for a backup destination; returns the chosen path or null if cancelled.</summary>
    string? PickSaveZip(string defaultFileName);

    /// <summary>Prompts for a backup archive to restore; returns the chosen path or null if cancelled.</summary>
    string? PickOpenZip();
}

/// <summary>Production picker over the WPF save/open-file dialogs.</summary>
public sealed class Win32BackupFilePicker : IBackupFilePicker
{
    public string? PickSaveZip(string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = defaultFileName,
            DefaultExt = ".zip",
            Filter = Localization.AppStrings.BackupZipFilter,
            AddExtension = true,
            OverwritePrompt = true,
            Title = Localization.AppStrings.BackupButton,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickOpenZip()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Filter = Localization.AppStrings.BackupZipFilter,
            Title = Localization.AppStrings.RestoreButton,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
