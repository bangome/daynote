namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The phone's second way to pick an attachment: from the photo library rather than from files.
/// </summary>
/// <remarks>
/// A phone keeps its pictures somewhere the document picker does not look (iOS shows only Files),
/// so the attach sheet offers both. Implemented by the phone's file picker beside
/// <see cref="Daynote.App.Shell.Product.IFilePicker"/>, and a picker without it simply offers files.
/// </remarks>
public interface IPhotoPicker
{
    /// <summary>Local paths of the chosen photos, copied where this process can read them; empty when cancelled.</summary>
    Task<IReadOnlyList<string>> PickPhotosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Where this picker leaves the copies it makes. Anything under it is the app's own and may be
    /// deleted once the store has taken the bytes.
    /// </summary>
    string StagingDirectory { get; }
}
