using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Platform;

/// <summary>The files panel's open dialog over Avalonia's storage provider (native NSOpenPanel on macOS).</summary>
public sealed class AvaloniaFilePicker : IFilePicker
{
    private readonly Func<TopLevel?> _topLevel;

    public AvaloniaFilePicker(Func<TopLevel?> topLevel)
    {
        _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken = default)
    {
        if (_topLevel() is not { StorageProvider: { CanOpen: true } provider })
        {
            return [];
        }

        IReadOnlyList<IStorageFile> files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Daynote.App.Localization.AppStrings.AddFile,
        }).ConfigureAwait(true);

        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }
}
