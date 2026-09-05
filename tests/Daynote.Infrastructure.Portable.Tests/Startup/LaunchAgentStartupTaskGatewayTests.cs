using Daynote.Core.Startup;
using Daynote.Infrastructure.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Startup;

[TestClass]
public sealed class LaunchAgentStartupTaskGatewayTests
{
    [TestMethod]
    public async Task Enable_writes_the_plist_and_disable_removes_it()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("The gateway is macOS-only.");
            return;
        }

        using var root = new TempDirectory();
        var gateway = new LaunchAgentStartupTaskGateway(
            "cc.arachat.daynote.test", "/Applications/Daynote.app/Contents/MacOS/Daynote", root.Path, useLaunchctl: false);

        Assert.AreEqual(StartupTaskState.Disabled, await gateway.GetStateAsync(CancellationToken.None));
        Assert.AreEqual(StartupTaskState.Enabled, await gateway.RequestEnableAsync(CancellationToken.None));
        Assert.IsTrue(File.Exists(gateway.PlistPath));
        Assert.AreEqual(StartupTaskState.Enabled, await gateway.GetStateAsync(CancellationToken.None));

        string plist = File.ReadAllText(gateway.PlistPath);
        StringAssert.Contains(plist, "<string>cc.arachat.daynote.test</string>");
        StringAssert.Contains(plist, "<string>/Applications/Daynote.app/Contents/MacOS/Daynote</string>");
        StringAssert.Contains(plist, "<key>RunAtLoad</key>");

        Assert.AreEqual(StartupTaskState.Disabled, await gateway.DisableAsync(CancellationToken.None));
        Assert.IsFalse(File.Exists(gateway.PlistPath));
    }

    [TestMethod]
    public async Task Service_policy_still_refuses_to_enable_from_a_non_disabled_state()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("The gateway is macOS-only.");
            return;
        }

        using var root = new TempDirectory();
        var gateway = new LaunchAgentStartupTaskGateway("cc.arachat.daynote.test", "/bin/true", root.Path, useLaunchctl: false);
        var service = new MsixStartupTaskService(gateway);

        StartupEnableResult first = await service.RequestEnableAsync();
        Assert.IsTrue(first.Changed);
        Assert.IsTrue(first.IsEnabled);

        StartupEnableResult second = await service.RequestEnableAsync();
        Assert.IsFalse(second.Changed, "already enabled: no rewrite");

        StartupEnableResult disabled = await service.RequestDisableAsync();
        Assert.IsTrue(disabled.Changed);
        Assert.AreEqual(StartupTaskState.Disabled, disabled.State);
    }

    [TestMethod]
    public void Plist_escapes_xml_in_paths()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("The gateway is macOS-only.");
            return;
        }

        var gateway = new LaunchAgentStartupTaskGateway("cc.test", "/Apps/A & B/<Daynote>", "/tmp", useLaunchctl: false);
        StringAssert.Contains(gateway.BuildPlist(), "/Apps/A &amp; B/&lt;Daynote&gt;");
    }
}
