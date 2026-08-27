using System.Text.Json.Nodes;
using Daynote.Core.Mcp;
using Daynote.Infrastructure.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Tests.Mcp;

/// <summary>
/// The one-click "register with Claude Desktop" path (docs/MCP.md). Every case runs against a real
/// temporary config file, because the whole point of the feature is that it edits a file the user
/// already owns without losing anything that was in it.
/// </summary>
[TestClass]
public sealed class ClaudeDesktopMcpRegistrationTests
{
    private const string Command = "daynote-mcp.exe";

    private readonly List<string> _directories = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string directory in _directories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [TestMethod]
    public async Task Test_registers_into_a_config_that_does_not_exist_yet()
    {
        string path = NewConfigPath();
        var service = new ClaudeDesktopMcpRegistration(path, Command);

        McpRegistrationResult result = await service.RegisterClaudeDesktopAsync();

        Assert.AreEqual(McpRegistrationOutcome.Registered, result.Outcome);
        Assert.AreEqual(Command, ReadCommand(path));
    }

    [TestMethod]
    public async Task Test_registration_keeps_other_servers_and_unrelated_settings()
    {
        string path = NewConfigPath();
        await File.WriteAllTextAsync(path, """
            {
              "globalShortcut": "Ctrl+Space",
              "mcpServers": {
                "filesystem": { "command": "npx", "args": ["-y", "server-filesystem"] }
              }
            }
            """);
        var service = new ClaudeDesktopMcpRegistration(path, Command);

        McpRegistrationResult result = await service.RegisterClaudeDesktopAsync();

        Assert.AreEqual(McpRegistrationOutcome.Registered, result.Outcome);
        JsonObject root = ReadRoot(path);
        Assert.AreEqual("Ctrl+Space", (string?)root["globalShortcut"]);
        var servers = (JsonObject)root["mcpServers"]!;
        Assert.AreEqual("npx", (string?)servers["filesystem"]!["command"], "an existing server was lost");
        Assert.AreEqual(Command, (string?)servers["daynote"]!["command"]);
    }

    [TestMethod]
    public async Task Test_registering_twice_reports_already_registered_without_rewriting()
    {
        string path = NewConfigPath();
        var service = new ClaudeDesktopMcpRegistration(path, Command);
        await service.RegisterClaudeDesktopAsync();
        DateTime written = File.GetLastWriteTimeUtc(path);

        McpRegistrationResult result = await service.RegisterClaudeDesktopAsync();

        Assert.AreEqual(McpRegistrationOutcome.AlreadyRegistered, result.Outcome);
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path), "the config was rewritten needlessly");
    }

    [TestMethod]
    public async Task Test_a_stale_daynote_command_is_replaced()
    {
        string path = NewConfigPath();
        await File.WriteAllTextAsync(path, """
            { "mcpServers": { "daynote": { "command": "C:\\old\\Daynote.Mcp.exe" } } }
            """);

        McpRegistrationResult result = await new ClaudeDesktopMcpRegistration(path, Command)
            .RegisterClaudeDesktopAsync();

        Assert.AreEqual(McpRegistrationOutcome.Registered, result.Outcome);
        Assert.AreEqual(Command, ReadCommand(path));
    }

    [TestMethod]
    public async Task Test_an_unparsable_config_fails_and_is_left_untouched()
    {
        string path = NewConfigPath();
        const string Broken = "{ this is not json";
        await File.WriteAllTextAsync(path, Broken);

        McpRegistrationResult result = await new ClaudeDesktopMcpRegistration(path, Command)
            .RegisterClaudeDesktopAsync();

        Assert.AreEqual(McpRegistrationOutcome.Failed, result.Outcome);
        Assert.AreEqual(path, result.ConfigPath, "the status line must name the file to fix");
        Assert.AreEqual(Broken, await File.ReadAllTextAsync(path), "a config we cannot parse must not be overwritten");
    }

    [TestMethod]
    public async Task Test_an_empty_config_file_is_treated_as_a_fresh_start()
    {
        string path = NewConfigPath();
        await File.WriteAllTextAsync(path, "   ");

        McpRegistrationResult result = await new ClaudeDesktopMcpRegistration(path, Command)
            .RegisterClaudeDesktopAsync();

        Assert.AreEqual(McpRegistrationOutcome.Registered, result.Outcome);
        Assert.AreEqual(Command, ReadCommand(path));
    }

    [TestMethod]
    public async Task Test_no_server_command_reports_unavailable_and_writes_nothing()
    {
        string path = NewConfigPath();
        var service = new ClaudeDesktopMcpRegistration(path, serverCommand: null);

        McpRegistrationResult result = await service.RegisterClaudeDesktopAsync();

        Assert.AreEqual(McpRegistrationOutcome.Unavailable, result.Outcome);
        Assert.IsFalse(result.IsRegistered);
        Assert.IsNull(service.ServerCommand);
        Assert.IsNull(service.ClaudeCodeCommand);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void Test_claude_code_command_quotes_the_server_command()
    {
        var service = new ClaudeDesktopMcpRegistration(NewConfigPath(), Command);

        Assert.AreEqual($"claude mcp add daynote -- \"{Command}\"", service.ClaudeCodeCommand);
    }

    [TestMethod]
    public void Test_packaged_runs_use_the_app_execution_alias()
    {
        // The alias is what gives the server the package identity, and with it the same virtualized
        // database the app uses. A path into the install folder would not be reachable by a client.
        Assert.AreEqual(
            McpServerCommand.PackagedAlias,
            McpServerCommand.Resolve(isPackaged: true, baseDirectory: @"C:\anywhere"));
    }

    [TestMethod]
    public void Test_unpackaged_runs_fall_back_to_a_sibling_executable_then_to_nothing()
    {
        string directory = NewDirectory();
        Assert.IsNull(
            McpServerCommand.Resolve(isPackaged: false, directory),
            "nothing built next to the app means the feature is unavailable, not misconfigured");

        string exe = Path.Combine(directory, "Daynote.Mcp.exe");
        File.WriteAllBytes(exe, []);
        Assert.AreEqual(exe, McpServerCommand.Resolve(isPackaged: false, directory));
    }

    private static string ReadCommand(string path) =>
        (string?)ReadRoot(path)["mcpServers"]!["daynote"]!["command"] ?? string.Empty;

    private static JsonObject ReadRoot(string path) =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    private string NewConfigPath() => Path.Combine(NewDirectory(), "claude_desktop_config.json");

    private string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "daynote-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        return directory;
    }
}
