using Daynote.Infrastructure.Mcp;
using Daynote.Infrastructure.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Persistence;

[TestClass]
public sealed class DaynoteDataRootTests
{
    [TestMethod]
    public void Default_lives_under_Application_Support_on_macOS_and_LocalAppData_elsewhere()
    {
        string root = DaynoteDataRoot.Default();
        Assert.AreEqual("Daynote", Path.GetFileName(root));
        if (OperatingSystem.IsMacOS())
        {
            StringAssert.EndsWith(root, Path.Combine("Library", "Application Support", "Daynote"));
        }
        else
        {
            Assert.AreEqual(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Daynote"), root);
        }
    }

    [TestMethod]
    public void Override_wins_and_is_made_absolute()
    {
        string? previous = Environment.GetEnvironmentVariable(DaynoteDataRoot.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DaynoteDataRoot.EnvironmentVariable, "relative-root");
            Assert.AreEqual(Path.GetFullPath("relative-root"), DaynoteDataRoot.Resolve());

            Environment.SetEnvironmentVariable(DaynoteDataRoot.EnvironmentVariable, "  ");
            Assert.AreEqual(DaynoteDataRoot.Default(), DaynoteDataRoot.Resolve());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DaynoteDataRoot.EnvironmentVariable, previous);
        }
    }

    [TestMethod]
    public void Claude_Desktop_config_path_follows_the_platform()
    {
        string path = ClaudeDesktopMcpRegistration.DefaultConfigPath;
        Assert.AreEqual("claude_desktop_config.json", Path.GetFileName(path));
        if (OperatingSystem.IsMacOS())
        {
            StringAssert.Contains(path, Path.Combine("Library", "Application Support", "Claude"));
        }
    }

    [TestMethod]
    public void Mcp_server_command_finds_the_platform_named_sibling_or_nothing()
    {
        using var root = new TempDirectory();
        Assert.IsNull(McpServerCommand.Resolve(isPackaged: false, root.Path));

        string exe = Path.Combine(root.Path, OperatingSystem.IsWindows() ? "Daynote.Mcp.exe" : "Daynote.Mcp");
        File.WriteAllText(exe, string.Empty);
        Assert.AreEqual(exe, McpServerCommand.Resolve(isPackaged: false, root.Path));
    }
}
