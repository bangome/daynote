namespace Daynote.Infrastructure.Persistence;

/// <summary>
/// Resolves the per-user data root every Daynote process (the app, the MCP server) must agree on.
/// </summary>
/// <remarks>
/// Windows keeps <c>%LocalAppData%\Daynote</c>, which is where every installed copy already has its
/// data. macOS uses <c>~/Library/Application Support/Daynote</c>, the location Finder, Time Machine and
/// the sandbox all treat as "this app's documents"; <c>LocalApplicationData</c> would have put it under
/// the invisible <c>~/.local/share</c>. Linux follows the XDG data directory.
/// </remarks>
public static class DaynoteDataRoot
{
    /// <summary>The QA/dev override, shared with the app's <c>DaynoteAppOptions</c>.</summary>
    public const string EnvironmentVariable = "DAYNOTE_DATA_ROOT";

    public const string FolderName = "Daynote";

    /// <summary>The override when set, else the platform default.</summary>
    public static string Resolve()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(overrideRoot) ? Default() : Path.GetFullPath(overrideRoot);
    }

    public static string Default()
    {
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", FolderName);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);
    }
}
