using System.IO;
using System.Windows.Media.Imaging;

namespace Daynote.App.Shell.Product;

/// <summary>Decodes file bytes into a frozen <see cref="BitmapImage"/> for the files panel.</summary>
public sealed class WpfThumbnailLoader : IThumbnailLoader
{
    public Task<object?> LoadAsync(byte[] bytes, int maxPixelWidth, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = maxPixelWidth;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        return Task.FromResult<object?>(image);
    }
}
