using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Daynote.App.Shell.Product;
using Daynote.Core.Files;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Platform;

/// <summary>The files panel's picker on a phone, over Avalonia's storage provider.</summary>
/// <remarks>
/// <para>
/// Not the desktop picker, because of one detail that decides everything: on a phone the file the
/// user picks usually has no path this process may open. Android hands back a <c>content://</c> URI
/// owned by another app, and iOS a security-scoped URL outside the sandbox, so
/// <c>TryGetLocalPath()</c> returns null for both and the desktop implementation would silently drop
/// every pick. The bytes are copied into the app's own cache directory first and that copy's path is
/// what the panel receives; the store then content-addresses it as usual and the copy is disposable.
/// </para>
/// <para>
/// Saving a copy out works the other way round: the provider's own save picker writes wherever the
/// user chose (Files, Drive) through a stream, so the caller is handed a cache path to write into and
/// the contents are pushed to the chosen destination afterwards.
/// </para>
/// <para>
/// Each pick is copied into a folder of its own under its own name, so the name the day's list shows
/// is the one the user picked rather than a staging name. A copy stops one byte past
/// <see cref="FileCapturePolicy.MaxFileBytes"/>: the caller sees the file is too large from its
/// length, and a 4 GB video never lands in the cache whole.
/// </para>
/// </remarks>
public sealed class MobileFilePicker(
    Func<TopLevel?> topLevel,
    Func<CancellationToken, Task<IReadOnlyList<string>>>? pickPhotos = null) : IFilePicker, IPhotoPicker
{
    private readonly Func<TopLevel?> _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

    public string StagingDirectory => InboxDirectory;

    /// <summary>Where picked bytes land before the asset store takes them. Cleared by the OS under pressure.</summary>
    private static string InboxDirectory
    {
        get
        {
            string directory = Path.Combine(Path.GetTempPath(), "daynote-picker");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken = default) =>
        PickAsync(imagesOnly: false, cancellationToken);

    /// <summary>The platform's photo picker when it has one; otherwise the document picker, images only.</summary>
    public Task<IReadOnlyList<string>> PickPhotosAsync(CancellationToken cancellationToken = default) =>
        pickPhotos is not null ? pickPhotos(cancellationToken) : PickAsync(imagesOnly: true, cancellationToken);

    private async Task<IReadOnlyList<string>> PickAsync(bool imagesOnly, CancellationToken cancellationToken)
    {
        if (_topLevel() is not { StorageProvider: { CanOpen: true } provider })
        {
            return [];
        }

        IReadOnlyList<IStorageFile> files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Daynote.App.Localization.AppStrings.AddFile,
            FileTypeFilter = imagesOnly ? [FilePickerFileTypes.ImageAll] : null,
        }).ConfigureAwait(true);

        var paths = new List<string>(files.Count);
        foreach (IStorageFile file in files)
        {
            if (await CopyIntoInboxAsync(file, cancellationToken).ConfigureAwait(true) is { } path)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        if (_topLevel() is not { StorageProvider: { CanSave: true } provider })
        {
            return null;
        }

        IStorageFile? destination = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Daynote.App.Localization.AppStrings.SaveFileTitle,
            SuggestedFileName = suggestedFileName,
            // No DefaultExtension: the suggested name already carries it, and Android's picker appends
            // the default to it again ("photo.png.png").
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);

        if (destination is null)
        {
            return null;
        }

        if (destination.TryGetLocalPath() is { Length: > 0 } local)
        {
            return local;
        }

        // No path to hand back, so give the caller a staging file and forward it once it is written.
        string staged = Path.Combine(NewSlot(), Sanitize(suggestedFileName));
        _pendingExports[staged] = destination;
        return staged;
    }

    /// <summary>Staged save targets, keyed by the path handed to the caller.</summary>
    private readonly Dictionary<string, IStorageFile> _pendingExports = [];

    /// <summary>
    /// Pushes a staged export to the place the user picked. The files panel writes its copy to the
    /// path it was given and then asks for this; a path that was a real local one is already done.
    /// </summary>
    public async Task CompleteSaveAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!_pendingExports.Remove(path, out IStorageFile? destination))
        {
            return;
        }

        try
        {
            await using Stream source = File.OpenRead(path);
            await using Stream target = await destination.OpenWriteAsync().ConfigureAwait(true);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            destination.Dispose();
            TryDelete(path);
        }
    }

    private static async Task<string?> CopyIntoInboxAsync(IStorageFile file, CancellationToken cancellationToken)
    {
        // A file that really is local (a phone's own Downloads on Android, the app's container on
        // iOS) needs no copy; the store reads it where it is.
        if (file.TryGetLocalPath() is { Length: > 0 } local && File.Exists(local))
        {
            return local;
        }

        string destination = Path.Combine(NewSlot(), Sanitize(file.Name));
        try
        {
            await using Stream source = await file.OpenReadAsync().ConfigureAwait(true);
            await using Stream target = File.Create(destination);
            await CopyCappedAsync(source, target, FileCapturePolicy.MaxFileBytes + 1, cancellationToken).ConfigureAwait(true);
            return destination;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(destination);
            return null;
        }
        finally
        {
            file.Dispose();
        }
    }

    /// <summary>A new empty folder in the inbox, so a copy can keep its own name beside any other.</summary>
    private static string NewSlot()
    {
        string slot = Path.Combine(InboxDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slot);
        return slot;
    }

    /// <summary>Copies at most <paramref name="limit"/> bytes: enough to tell a file is over the cap.</summary>
    private static async Task CopyCappedAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long copied = 0;
        while (copied < limit)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - copied)), cancellationToken)
                .ConfigureAwait(true);
            if (read == 0)
            {
                return;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);
            copied += read;
        }
    }

    /// <summary>Strips anything a foreign provider might have put in the name that is not a file name.</summary>
    private static string Sanitize(string name)
    {
        string trimmed = Path.GetFileName(name);
        return string.IsNullOrWhiteSpace(trimmed)
            ? "attachment"
            : string.Concat(trimmed.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
