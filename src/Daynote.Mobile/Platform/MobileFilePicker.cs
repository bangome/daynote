using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Daynote.App.Shell.Product;

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
/// </remarks>
public sealed class MobileFilePicker(Func<TopLevel?> topLevel) : IFilePicker
{
    private readonly Func<TopLevel?> _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));

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
            DefaultExtension = Path.GetExtension(suggestedFileName).TrimStart('.'),
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
        string staged = Path.Combine(InboxDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(suggestedFileName)}");
        _pendingExports[staged] = destination;
        return staged;
    }

    /// <summary>Staged save targets, keyed by the path handed to the caller.</summary>
    private readonly Dictionary<string, IStorageFile> _pendingExports = [];

    /// <summary>
    /// Pushes a staged export to the place the user picked. The files panel writes its copy to the
    /// path it was given and then asks for this; a path that was a real local one is already done.
    /// </summary>
    public async Task CompleteExportAsync(string path, CancellationToken cancellationToken = default)
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

        string destination = Path.Combine(InboxDirectory, $"{Guid.NewGuid():N}-{Sanitize(file.Name)}");
        try
        {
            await using Stream source = await file.OpenReadAsync().ConfigureAwait(true);
            await using Stream target = File.Create(destination);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(true);
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
