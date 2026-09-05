namespace Daynote.Infrastructure.Portable.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        // Short on purpose: Unix domain socket paths are capped at 104 bytes on macOS, and $TMPDIR
        // already spends ~50 of them.
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dn-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
