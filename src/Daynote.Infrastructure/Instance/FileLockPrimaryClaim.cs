namespace Daynote.Infrastructure.Instance;

/// <summary>
/// A primary claim backed by an exclusively opened lock file. The OS releases the lock when the
/// process exits, however it exits, so a crashed primary never leaves a stale claim behind — the same
/// property the named mutex gives the WPF app.
/// </summary>
public sealed class FileLockPrimaryClaim : IPrimaryClaim
{
    private readonly string _path;
    private FileStream? _lock;

    public FileLockPrimaryClaim(string path)
    {
        _path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Lock path required.", nameof(path)) : path;
    }

    public bool TryClaim()
    {
        if (_lock is not null)
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            _lock = new FileStream(
                _path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            return true;
        }
        catch (IOException)
        {
            // Another process holds the exclusive handle: it is the primary.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _lock?.Dispose();
        _lock = null;
    }
}
