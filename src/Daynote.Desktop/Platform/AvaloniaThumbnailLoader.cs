using Avalonia.Media.Imaging;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Platform;

/// <summary>Decodes file bytes into an Avalonia <see cref="Bitmap"/> for the files panel cards.</summary>
public sealed class AvaloniaThumbnailLoader : IThumbnailLoader
{
    /// <remarks>
    /// A file named .png is not always a PNG. Skia reports bytes it cannot decode as a
    /// NullReferenceException from inside the bitmap constructor rather than as a format error, so that
    /// is caught here and becomes "no thumbnail": the card shows its extension badge instead, and the
    /// attach (a drop, a paste, the picker) goes through rather than failing on a preview.
    /// </remarks>
    public Task<object?> LoadAsync(byte[] bytes, int maxPixelWidth, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(bytes);
        try
        {
            return Task.FromResult<object?>(Bitmap.DecodeToWidth(memory, maxPixelWidth));
        }
        catch (Exception exception) when (exception is NullReferenceException or InvalidOperationException or ArgumentException)
        {
            return Task.FromResult<object?>(null);
        }
    }
}
