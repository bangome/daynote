using Daynote.Infrastructure.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Mcp;

/// <summary>
/// Package-identity detection, which decides whether MCP clients are handed the app execution alias
/// or a path on disk.
/// </summary>
/// <remarks>
/// This used to be fenced with <c>#if WINDOWS</c> and hard-coded to false everywhere else, on the
/// reasoning that only the MSIX build has an identity. That held while the package's entry point was
/// <c>Daynote.App</c>. It stopped holding when the entry point became <c>Daynote.Desktop</c>, which
/// targets plain <c>net10.0</c> and therefore links the build where the fence removed the check — so
/// a packaged app would have handed clients a path under <c>WindowsApps</c> that they cannot
/// traverse, and nothing would have failed.
/// <para>
/// It is now a Win32 P/Invoke, so the failure worth testing is the binding itself: a wrong entry
/// point name or wrong marshalling throws or returns nonsense, and this project is the one both CI
/// legs run.
/// </para>
/// </remarks>
[TestClass]
public sealed class PackageIdentityTests
{
    [TestMethod]
    public void The_check_answers_without_throwing_in_an_unpackaged_process()
    {
        // A test host is never packaged, on any platform, so the answer is known.
        Assert.IsFalse(McpServerCommand.IsPackaged(), "A test host reported a package identity.");
    }

    [TestMethod]
    public void An_unpackaged_process_is_not_offered_the_alias()
    {
        Assert.AreNotEqual(
            McpServerCommand.PackagedAlias,
            McpServerCommand.Resolve(isPackaged: false, baseDirectory: Path.Combine("C:", "nowhere")),
            "The alias only works from inside the package.");
        Assert.AreEqual(
            McpServerCommand.PackagedAlias,
            McpServerCommand.Resolve(isPackaged: true, baseDirectory: Path.Combine("C:", "nowhere")),
            "A packaged process must be given the alias, not a path under WindowsApps.");
    }
}
