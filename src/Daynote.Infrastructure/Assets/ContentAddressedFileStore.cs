using System.Security.Cryptography;
using Daynote.Core.Files;

namespace Daynote.Infrastructure.Assets;

/// <summary>
/// Content-addressed store for day-file attachments. Bytes stream into a temp file under the files root while the
/// SHA-256 is computed and the hard size cap is enforced; the temp is then atomically renamed to
/// <c>{hash[0..2]}\{hash}{ext}</c>. Identical content is de-duplicated to a single physical file (the
/// first-written extension wins), physical deletes are reference-safe, and reconciliation removes orphans
/// and stale temps confined to the files root.
/// </summary>
public sealed class ContentAddressedFileStore : IFileAssetStore
{
    private const int BufferSize = 64 * 1024;

    private readonly string fileRoot;
    private readonly string fileRootPrefix;
    private readonly IImageAssetFaultInjector? faultInjector;

    public ContentAddressedFileStore(string dataRoot, IImageAssetFaultInjector? faultInjector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        string canonicalDataRoot = Path.GetFullPath(dataRoot);
        fileRoot = Path.GetFullPath(Path.Combine(canonicalDataRoot, "files"));
        string dataRootPrefix = EnsureTrailingSeparator(canonicalDataRoot);
        if (!fileRoot.StartsWith(dataRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The file root must be inside the Daynote data root.", nameof(dataRoot));
        }

        fileRootPrefix = EnsureTrailingSeparator(fileRoot);
        this.faultInjector = faultInjector;
    }

    public async ValueTask<PreparedFileAsset> PrepareAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Fast, allocation-free rejection for the common case of a seekable source (a real file the UI
        // attached): reject before touching the disk so an oversize payload leaves nothing behind. The
        // streaming check below is the safety net for non-seekable sources.
        if (content.CanSeek && content.Length > FileCapturePolicy.MaxFileBytes)
        {
            throw new DayFileTooLargeException(content.Length);
        }

        string safeExtension = SafeExtension(extension);
        Directory.CreateDirectory(fileRoot);
        EnsureNotReparsePoint(fileRoot);

        string temporaryPath = Path.Combine(fileRoot, $".{Guid.NewGuid():N}.tmp");
        string hash;
        long length;
        try
        {
            faultInjector?.At(ImageAssetFaultPoint.TempCreate, temporaryPath);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                long total = 0;
                faultInjector?.At(ImageAssetFaultPoint.Write, temporaryPath);
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total = checked(total + read);
                    if (total > FileCapturePolicy.MaxFileBytes)
                    {
                        throw new DayFileTooLargeException(total);
                    }

                    sha.AppendData(buffer, 0, read);
                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                faultInjector?.At(ImageAssetFaultPoint.Flush, temporaryPath);
                stream.Flush(flushToDisk: true);
                hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                length = total;
            }

            string relativeDirectory = hash[..2];
            string targetDirectory = Resolve(relativeDirectory);
            Directory.CreateDirectory(targetDirectory);

            string? existing = FindExistingByHash(targetDirectory, hash);
            if (existing is not null)
            {
                File.Delete(temporaryPath);
                return new PreparedFileAsset(hash, Path.GetRelativePath(fileRoot, existing), length, CreatedNew: false);
            }

            string relativePath = Path.Combine(relativeDirectory, hash + safeExtension);
            string targetPath = Resolve(relativePath);
            faultInjector?.At(ImageAssetFaultPoint.Rename, targetPath);
            try
            {
                File.Move(temporaryPath, targetPath, overwrite: false);
                return new PreparedFileAsset(hash, relativePath, length, CreatedNew: true);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                File.Delete(temporaryPath);
                return new PreparedFileAsset(hash, relativePath, length, CreatedNew: false);
            }
        }
        catch
        {
            TryDeleteTemporary(temporaryPath);
            throw;
        }
    }

    public ValueTask<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(File.Exists(Resolve(relativePath)));
    }

    public async ValueTask<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        string path = Resolve(relativePath);
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public ValueTask DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Resolve(relativePath);
        faultInjector?.At(ImageAssetFaultPoint.Delete, path);
        File.Delete(path);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<AssetReconciliationResult> ReconcileAsync(
        IReadOnlySet<string> referencedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referencedPaths);
        Directory.CreateDirectory(fileRoot);
        EnsureNotReparsePoint(fileRoot);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relativePath in referencedPaths)
        {
            referenced.Add(Resolve(relativePath));
        }

        int temporaryDeleted = 0;
        int orphanDeleted = 0;
        foreach (string path in EnumerateFilesInsideRoot())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(path), ".tmp", StringComparison.OrdinalIgnoreCase))
            {
                DeleteReconciled(path);
                temporaryDeleted++;
            }
            else if (!referenced.Contains(path))
            {
                DeleteReconciled(path);
                orphanDeleted++;
            }
        }

        return new AssetReconciliationResult(temporaryDeleted, orphanDeleted);
    }

    private static string? FindExistingByHash(string directory, string hash)
    {
        foreach (string candidate in Directory.EnumerateFiles(directory, hash + ".*"))
        {
            if (string.Equals(
                Path.GetFileNameWithoutExtension(candidate), hash, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return File.Exists(Path.Combine(directory, hash)) ? Path.Combine(directory, hash) : null;
    }

    private IEnumerable<string> EnumerateFilesInsideRoot()
    {
        var pending = new Stack<string>();
        pending.Push(fileRoot);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                yield return ResolveAbsolute(file);
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(ResolveAbsolute(child));
                }
            }
        }
    }

    private void DeleteReconciled(string absolutePath)
    {
        faultInjector?.At(ImageAssetFaultPoint.Delete, absolutePath);
        File.Delete(absolutePath);
    }

    private string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Asset paths must be relative.", nameof(relativePath));
        }

        return ResolveAbsolute(Path.Combine(fileRoot, relativePath));
    }

    private string ResolveAbsolute(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fileRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A file asset path escaped the Daynote file root.");
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    private void EnsureNoReparsePoints(string fullPath)
    {
        string relative = Path.GetRelativePath(fileRoot, fullPath);
        string current = fileRoot;
        EnsureNotReparsePoint(current);
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            current = Path.Combine(current, segment);
            EnsureNotReparsePoint(current);
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Reparse points are not allowed in the Daynote file root.");
        }
    }

    private static string SafeExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension) || extension.Length is < 2 or > 17 || extension[0] != '.')
        {
            return string.Empty;
        }

        for (int index = 1; index < extension.Length; index++)
        {
            if (!char.IsAsciiLetterOrDigit(extension[index]))
            {
                return string.Empty;
            }
        }

        return extension.ToLowerInvariant();
    }

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string EnsureTrailingSeparator(string path) =>
        Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
}
