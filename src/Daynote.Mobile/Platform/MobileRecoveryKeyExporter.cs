using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Daynote.App.Account;
using Daynote.App.Localization;

namespace Daynote.Mobile.Platform;

/// <summary>Recovery-key export on a phone: the clipboard, or a file through the system share sheet.</summary>
/// <remarks>
/// Avalonia's save picker maps to the document picker on iOS and the Storage Access Framework on
/// Android, so "save to file" means Files or Drive rather than a folder — which is the right place
/// for this particular file anyway, since a recovery key kept only on the phone is lost with it.
/// </remarks>
public sealed class MobileRecoveryKeyExporter(Func<TopLevel?> topLevel) : IRecoveryKeyExporter
{
    private readonly Func<TopLevel?> _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

    public async Task<bool> TryCopyToClipboardAsync(string recoveryKey)
    {
        if (_topLevel()?.Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(recoveryKey).ConfigureAwait(true);
        return true;
    }

    public async Task<bool> TrySaveToFileAsync(string recoveryKey)
    {
        if (_topLevel() is not { StorageProvider: { CanSave: true } provider })
        {
            return false;
        }

        IStorageFile? file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = AppStrings.RecoveryKeySaveToFile,
            SuggestedFileName = "daynote-recovery-key.txt",
            DefaultExtension = "txt",
        }).ConfigureAwait(true);
        if (file is null)
        {
            return false;
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync(recoveryKey).ConfigureAwait(true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            file.Dispose();
        }
    }
}
