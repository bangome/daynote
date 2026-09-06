namespace Daynote.Infrastructure.Instance;

public sealed partial class SingleInstanceCoordinator
{
    /// <summary>
    /// The OS-neutral factory: an exclusive lock file as the primary claim and a Unix domain socket as
    /// the activation channel, both inside a directory only the current user can enter. This is what the
    /// Avalonia app uses on macOS and Linux; on Windows it works too, though the WPF app keeps the
    /// mutex/named-pipe pair in <see cref="ForCurrentUser(string)"/>.
    /// </summary>
    public static SingleInstanceCoordinator ForCurrentUserPortable(string baseName, string? runtimeDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        string directory = runtimeDirectory ?? InstanceRuntimeDirectory.ForCurrentUser(baseName);
        return new SingleInstanceCoordinator(
            new FileLockPrimaryClaim(Path.Combine(directory, $"{baseName}.lock")),
            new UnixDomainSocketActivationChannel(Path.Combine(directory, $"{baseName}.sock")));
    }
}

/// <summary>
/// Where the portable single-instance primitives keep their lock file and socket: a per-user
/// directory nobody else can traverse, so no other account can signal or block this user's instance.
/// </summary>
public static class InstanceRuntimeDirectory
{
    /// <summary>
    /// macOS: <c>$TMPDIR</c> (a per-user, mode-0700 folder the OS creates for every login).
    /// Linux: <c>$XDG_RUNTIME_DIR</c> when set, else a uid-suffixed folder under the temp root.
    /// Windows: <c>%LocalAppData%\{baseName}</c>. Socket paths are capped at 104 bytes on macOS,
    /// which every one of these stays well under.
    /// </summary>
    public static string ForCurrentUser(string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), baseName);
        }
        else if (OperatingSystem.IsMacOS())
        {
            root = Environment.GetEnvironmentVariable("TMPDIR") is { Length: > 0 } tmp
                ? tmp
                : Path.GetTempPath();
        }
        else
        {
            root = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Path.GetTempPath(), $"{baseName.ToLowerInvariant()}-{Environment.UserName}");
        }

        string directory = Path.Combine(root, $".{baseName.ToLowerInvariant()}");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return directory;
    }
}
