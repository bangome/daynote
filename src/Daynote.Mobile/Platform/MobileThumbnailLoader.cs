using Avalonia.Media.Imaging;
using Daynote.App.Shell.Product;

namespace Daynote.Mobile.Platform;

/// <summary>
/// Decodes attachment bytes into an Avalonia <see cref="Bitmap"/> for the file cards.
/// </summary>
/// <remarks>
/// Decoding happens off the UI thread here, unlike the desktop loader. A phone photo is routinely
/// 12 megapixels and the decode is the one place in this app that blocks long enough to drop frames
/// on a mid-range device.
/// </remarks>
public sealed class MobileThumbnailLoader : IThumbnailLoader
{
    public Task<object?> LoadAsync(byte[] bytes, int maxPixelWidth, CancellationToken cancellationToken) =>
        Task.Run<object?>(
            () =>
            {
                using var memory = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(memory, maxPixelWidth);
            },
            cancellationToken);
}
