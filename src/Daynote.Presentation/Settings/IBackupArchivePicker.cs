namespace Daynote.App.Settings;

/// <summary>
/// Save/open dialogs for backup archives, framework-neutral and asynchronous. The WPF app keeps its
/// synchronous <c>IBackupFilePicker</c>; this is the port the Avalonia settings use.
/// </summary>
public interface IBackupArchivePicker
{
    /// <returns>The chosen destination path, or null when cancelled.</returns>
    Task<string?> PickSaveZipAsync(string defaultFileName, CancellationToken cancellationToken = default);

    /// <returns>The chosen archive path, or null when cancelled.</returns>
    Task<string?> PickOpenZipAsync(CancellationToken cancellationToken = default);
}
