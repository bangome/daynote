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
    /// True when the process runs with a packaged identity. <c>Package.Current</c> throws for an
    /// unpackaged process, which is the documented way to ask and mirrors
    /// <see cref="Startup.WindowsStartupTaskGateway"/>'s handling of the same situation.
    /// </summary>
    private static bool IsPackaged()
    {
#if WINDOWS
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException or NotSupportedException or TypeLoadException)
        {
            return false;
        }
#else
        // Only the MSIX build has a package identity; the portable build never does.
        return false;
#endif
    }
}
