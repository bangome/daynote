using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Daynote.App.Localization;
using Daynote.App.Settings;

namespace Daynote.Desktop.Platform;

/// <summary>Backup save/open panels over Avalonia's storage provider (native NSSavePanel/NSOpenPanel).</summary>
public sealed class AvaloniaBackupArchivePicker : IBackupArchivePicker
{
    private static readonly FilePickerFileType Zip = new("Daynote backup") { Patterns = ["*.zip"], MimeTypes = ["application/zip"] };

    private readonly Func<TopLevel?> _topLevel;

    public AvaloniaBackupArchivePicker(Func<TopLevel?> topLevel)
    {
        _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));
    }

    public async Task<string?> PickSaveZipAsync(string defaultFileName, CancellationToken cancellationToken = default)
    {
        if (_topLevel() is not { StorageProvider: { CanSave: true } provider })
        {
            return null;
        }

        IStorageFile? file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppStrings.BackupButton,
            SuggestedFileName = defaultFileName,
            DefaultExtension = "zip",
            FileTypeChoices = [Zip],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenZipAsync(CancellationToken cancellationToken = default)
    {
        if (_topLevel() is not { StorageProvider: { CanOpen: true } provider })
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = AppStrings.RestoreButton,
            AllowMultiple = false,
            FileTypeFilter = [Zip],
        }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
