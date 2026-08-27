namespace Daynote.Core.Mcp;

/// <summary>
/// What happened when the app tried to register its MCP server with a client.
/// </summary>
public enum McpRegistrationOutcome
{
    /// <summary>No server command exists to register (an unpackaged dev run with no built server).</summary>
    Unavailable = 0,

    /// <summary>The client config now names the Daynote server; it did not before.</summary>
    Registered = 1,

    /// <summary>The client config already named exactly this server command; nothing was written.</summary>
    AlreadyRegistered = 2,

    /// <summary>The config could not be read or written (unparsable file, permissions, I/O).</summary>
    Failed = 3,
}

/// <summary>The outcome plus the config file it applies to, for a status line the user can act on.</summary>
public readonly record struct McpRegistrationResult(McpRegistrationOutcome Outcome, string ConfigPath)
{
    public bool IsRegistered => Outcome is McpRegistrationOutcome.Registered or McpRegistrationOutcome.AlreadyRegistered;
}

/// <summary>
/// Registers Daynote's bundled MCP stdio server with the MCP clients on this machine, so a Store user
/// never has to build or locate an executable. The command handed to clients is deliberately the
/// package's app execution alias (<c>daynote-mcp.exe</c>, declared in Package.appxmanifest): launching
/// through the alias gives the server the package identity, and only then does it see the same
/// virtualized database the app writes. See docs/MCP.md.
/// </summary>
public interface IMcpRegistrationService
{
    /// <summary>
    /// The command an MCP client should launch, or null when this build has no server to offer (an
    /// unpackaged dev run where <c>Daynote.Mcp.exe</c> was never built). Null disables registration
    /// in the UI rather than writing a command that cannot start.
    /// </summary>
    string? ServerCommand { get; }

    /// <summary>The one-line <c>claude mcp add</c> command for Claude Code, or null when unavailable.</summary>
    string? ClaudeCodeCommand { get; }

    /// <summary>The Claude Desktop config file this service writes, whether or not it exists yet.</summary>
    string ClaudeDesktopConfigPath { get; }

    /// <summary>
    /// Adds (or refreshes) the <c>daynote</c> entry in the Claude Desktop config, leaving every other
    /// server and unrelated setting in the file untouched. A file that cannot be parsed is reported as
    /// <see cref="McpRegistrationOutcome.Failed"/> and left exactly as it was: silently replacing a
    /// user's config would cost them their other MCP servers.
    /// </summary>
    ValueTask<McpRegistrationResult> RegisterClaudeDesktopAsync(CancellationToken cancellationToken = default);
}
