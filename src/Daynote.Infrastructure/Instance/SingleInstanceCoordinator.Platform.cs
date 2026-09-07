namespace Daynote.Infrastructure.Instance;

public sealed partial class SingleInstanceCoordinator
{
    /// <summary>
    /// The single-instance primitives this OS should use, so both apps make the same choice.
    /// </summary>
    /// <remarks>
    /// Windows gets the named mutex and pipe rather than the portable lock file and socket. Both work
    /// there, but only the mutex is seen by the WPF shell, which uses it with the same base name —
    /// so while the two builds coexist, launching either one activates whichever is already running
    /// instead of putting two processes on the same SQLite database. Verified on 2026-09-07 in all
    /// three directions (Avalonia/Avalonia, WPF/Avalonia, Avalonia/WPF).
    /// <para>
    /// One pairing this does <b>not</b> cover: the Microsoft Store build. A packaged app's named
    /// kernel objects live in its own namespace, so its mutex is invisible from outside the package —
    /// measured by holding the Store build open and finding no <c>Local\Daynote-&lt;SID&gt;</c>, while
    /// the same code unpackaged creates one. An unpackaged build can therefore run beside an
    /// installed Store build, on the same data folder, and neither will notice. That is a migration
    /// hazard rather than a bug here; docs/WINDOWS_ON_AVALONIA.md §2 carries it.
    /// </para>
    /// </remarks>
    public static SingleInstanceCoordinator ForCurrentUserOnThisPlatform(string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        return OperatingSystem.IsWindows()
            ? ForCurrentUser(baseName)
            : ForCurrentUserPortable(baseName);
    }

    /// <summary>What <see cref="ForCurrentUserOnThisPlatform"/> picked, for tests and diagnostics.</summary>
    public IPrimaryClaim PrimaryClaim => _claim;
}
