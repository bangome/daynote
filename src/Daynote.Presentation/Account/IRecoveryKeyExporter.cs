namespace Daynote.App.Account;

/// <summary>
/// Puts the one-time recovery key on the clipboard or into a file the user chooses. Asynchronous
/// because Avalonia's clipboard and file dialogs are; the WPF implementation completes synchronously.
/// </summary>
public interface IRecoveryKeyExporter
{
    Task<bool> TryCopyToClipboardAsync(string recoveryKey);

    Task<bool> TrySaveToFileAsync(string recoveryKey);
}
