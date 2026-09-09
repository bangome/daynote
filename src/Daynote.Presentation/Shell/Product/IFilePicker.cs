namespace Daynote.App.Shell.Product;

/// <summary>
/// Abstracts the OS open-file dialog so the files panel is testable without a real dialog. Returns the
/// absolute paths the user chose, or an empty list when cancelled. Asynchronous because Avalonia's
/// dialogs are; the WPF implementation simply wraps its modal dialog in a completed task.
/// </summary>
public interface IFilePicker
{
    Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks where to write a copy of an attachment, seeded with its own name, and returns the chosen
    /// absolute path or null when cancelled. Same seam as the open dialog for the same reason: the
    /// files panel has to be exercised without one.
    /// </summary>
    Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default);
}
