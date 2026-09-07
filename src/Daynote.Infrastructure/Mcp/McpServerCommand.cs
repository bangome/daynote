using System.IO;

namespace Daynote.Infrastructure.Mcp;

/// <summary>
/// Resolves the command MCP clients should launch to start Daynote's stdio server.
/// </summary>
/// <remarks>
/// <para>
/// In the shipped (MSIX/Store) build the answer is the package's app execution alias,
/// <c>daynote-mcp.exe</c>. That matters for correctness, not just convenience: the alias starts the
/// server inside the package, so the OS applies the same file-system virtualization to it as to the
/// app and both open the identical <c>daynote.db</c>. It also keeps clients away from the install
/// folder under <c>WindowsApps</c>, whose ACLs a client process cannot traverse.
/// </para>
/// <para>
/// In an unpackaged dev run there is no alias, so we fall back to a <c>Daynote.Mcp.exe</c> sitting
/// next to the app. When neither exists the command is null and the feature reports itself
/// unavailable rather than registering something that cannot start.
/// </para>
/// </remarks>
public static class McpServerCommand
{
    /// <summary>The alias declared by the <c>windows.appExecutionAlias</c> extension in Package.appxmanifest.</summary>
    public const string PackagedAlias = "daynote-mcp.exe";

    /// <summary>The apphost next to the app: <c>Daynote.Mcp.exe</c> on Windows, <c>Daynote.Mcp</c> elsewhere.</summary>
    private static readonly string ExecutableName = OperatingSystem.IsWindows() ? "Daynote.Mcp.exe" : "Daynote.Mcp";

    /// <summary>
    /// The command for this run, or null when no server is reachable. Evaluated once: neither the
    /// package identity nor the neighbouring files change while the app is running.
    /// </summary>
    public static string? Current { get; } = Resolve(IsPackaged(), AppContext.BaseDirectory);

    /// <summary>Testable core: the alias when packaged, else a sibling executable, else null.</summary>
    public static string? Resolve(bool isPackaged, string baseDirectory)
    {
        if (isPackaged)
        {
            return PackagedAlias;
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        string sibling = Path.Combine(baseDirectory, ExecutableName);
        return File.Exists(sibling) ? sibling : null;
    }

    /// <summary>
    /// True when the process runs with a packaged identity. Public so a test can prove the P/Invoke
    /// binds and answers, which is the part that can fail silently.
    /// </summary>
    /// <remarks>
    /// Asked through Win32 <c>GetCurrentPackageFullName</c> rather than WinRT
    /// <c>Package.Current</c>. This used to be fenced with <c>#if WINDOWS</c> and answered false
    /// everywhere else, on the reasoning that only the MSIX build has an identity and the portable
    /// build never does. That stopped being true when the package's entry point became
    /// <c>Daynote.Desktop</c>: the Avalonia shell targets plain <c>net10.0</c>, so it links this
    /// assembly's non-Windows build, where the fence compiled out the only code that could notice —
    /// and a packaged app would have handed MCP clients a path under <c>WindowsApps</c> that they
    /// cannot traverse, with nothing failing to say so.
    /// <para>
    /// <c>GetCurrentPackageFullName</c> needs no Windows target framework and no WinRT projection.
    /// Unpackaged it returns <c>APPMODEL_ERROR_NO_PACKAGE</c>; the entry point is present on every
    /// Windows this app supports, and any other platform is unpackaged by definition.
    /// </para>
    /// </remarks>
    public static bool IsPackaged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>What the API returns for a process with no package identity.</summary>
    private const int AppModelErrorNoPackage = 15700;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
