using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daynote.Core.Mcp;

namespace Daynote.Infrastructure.Mcp;

/// <summary>
/// Writes the <c>daynote</c> entry into Claude Desktop's <c>claude_desktop_config.json</c> and hands
/// out the equivalent Claude Code command line. The config path and server command are injected so
/// tests drive it against a temporary file.
/// </summary>
public sealed class ClaudeDesktopMcpRegistration : IMcpRegistrationService
{
    private const string ServersProperty = "mcpServers";
    private const string ServerKey = "daynote";
    private const string CommandProperty = "command";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions ReadOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public ClaudeDesktopMcpRegistration(string configPath, string? serverCommand)
    {
        ClaudeDesktopConfigPath = string.IsNullOrWhiteSpace(configPath)
            ? throw new ArgumentException("Config path required.", nameof(configPath))
            : configPath;
        ServerCommand = string.IsNullOrWhiteSpace(serverCommand) ? null : serverCommand;
    }

    /// <summary>
    /// The default location Claude Desktop reads: <c>%AppData%\Claude\claude_desktop_config.json</c> on
    /// Windows, <c>~/Library/Application Support/Claude/claude_desktop_config.json</c> on macOS.
    /// </summary>
    public static string DefaultConfigPath => Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Claude",
        "claude_desktop_config.json");

    public string? ServerCommand { get; }

    public string ClaudeDesktopConfigPath { get; }

    public string? ClaudeCodeCommand =>
        ServerCommand is null ? null : $"claude mcp add {ServerKey} -- \"{ServerCommand}\"";

    public async ValueTask<McpRegistrationResult> RegisterClaudeDesktopAsync(
        CancellationToken cancellationToken = default)
    {
        if (ServerCommand is null)
        {
            return new McpRegistrationResult(McpRegistrationOutcome.Unavailable, ClaudeDesktopConfigPath);
        }

        try
        {
            JsonObject root;
            if (File.Exists(ClaudeDesktopConfigPath))
            {
                string existing = await File.ReadAllTextAsync(ClaudeDesktopConfigPath, cancellationToken)
                    .ConfigureAwait(false);

                // An empty file is a fresh start; anything non-empty must parse as an object or we
                // refuse to touch it, so a hand-edited config is never traded for our one entry.
                root = string.IsNullOrWhiteSpace(existing)
                    ? []
                    : JsonNode.Parse(existing, nodeOptions: null, ReadOptions) as JsonObject
                        ?? throw new JsonException("Root is not a JSON object.");
            }
            else
            {
                root = [];
            }

            if (root[ServersProperty] is not JsonObject servers)
            {
                // Absent, null, or the wrong shape: a scalar/array here is not a server map, and
                // replacing it is the only way forward.
                servers = [];
                root[ServersProperty] = servers;
            }

            if (servers[ServerKey] is JsonObject current
                && current[CommandProperty]?.GetValue<string>() == ServerCommand)
            {
                return new McpRegistrationResult(
                    McpRegistrationOutcome.AlreadyRegistered, ClaudeDesktopConfigPath);
            }

            // Replace only our own entry, keeping every sibling server as it was.
            servers[ServerKey] = new JsonObject { [CommandProperty] = ServerCommand };

            await WriteAtomicallyAsync(root, cancellationToken).ConfigureAwait(false);
            return new McpRegistrationResult(McpRegistrationOutcome.Registered, ClaudeDesktopConfigPath);
        }
        catch (Exception exception) when (exception is JsonException or IOException
            or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
            or System.Security.SecurityException)
        {
            return new McpRegistrationResult(McpRegistrationOutcome.Failed, ClaudeDesktopConfigPath);
        }
    }

    /// <summary>
    /// Writes through a temporary sibling and moves it into place, so an interrupted write cannot
    /// leave the user with a half-written config that Claude Desktop then refuses to load.
    /// </summary>
    private async Task WriteAtomicallyAsync(JsonObject root, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(ClaudeDesktopConfigPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = ClaudeDesktopConfigPath + ".daynote-tmp";
        await File.WriteAllTextAsync(temporary, root.ToJsonString(WriteOptions), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporary, ClaudeDesktopConfigPath, overwrite: true);
    }
}
