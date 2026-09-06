namespace Daynote.App.Shell.Product;

/// <summary>
/// Decodes an image file's bytes into whatever the UI framework binds an <c>Image</c> to (a WPF
/// <c>ImageSource</c>, an Avalonia <c>Bitmap</c>). Kept opaque as <see cref="object"/> so the files
/// panel view model stays free of any framework; a null result simply shows the generic image badge.
/// </summary>
public interface IThumbnailLoader
{
    /// <param name="maxPixelWidth">Decode hint: the panel never shows a thumbnail wider than this.</param>
    Task<object?> LoadAsync(byte[] bytes, int maxPixelWidth, CancellationToken cancellationToken);
}
