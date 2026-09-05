using Avalonia.Media.Imaging;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Platform;

/// <summary>Decodes file bytes into an Avalonia <see cref="Bitmap"/> for the files panel cards.</summary>
public sealed class AvaloniaThumbnailLoader : IThumbnailLoader
{
    public Task<object?> LoadAsync(byte[] bytes, int maxPixelWidth, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(bytes);
        return Task.FromResult<object?>(Bitmap.DecodeToWidth(memory, maxPixelWidth));
    }
}
