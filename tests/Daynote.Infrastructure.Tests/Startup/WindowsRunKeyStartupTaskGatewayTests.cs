using Daynote.Core.Startup;
using Daynote.Infrastructure.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace Daynote.Infrastructure.Tests.Startup;

/// <summary>
/// The unpackaged "start at sign-in" gateway, against the real registry.
/// </summary>
/// <remarks>
/// Against the real <c>HKCU\...\Run</c> key, because a fake would only prove the fake works and the
/// interesting failures are all real ones — a value written under the wrong name, a path that loses
/// its quotes and breaks on a space, a disable that leaves the entry behind. Every test uses a value
/// name of its own and removes it in a finally, so the user's actual Daynote entry is never touched.
/// </remarks>
[TestClass]
public sealed class WindowsRunKeyStartupTaskGatewayTests
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private string _valueName = null!;

    [TestInitialize]
    public void Setup() => _valueName = $"DaynoteTest-{Guid.NewGuid():N}";

    [TestCleanup]
    public void Cleanup()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    [TestMethod]
    public async Task Enable_writes_the_entry_and_disable_takes_it_away()
    {
        WindowsRunKeyStartupTaskGateway gateway = Create(@"C:\Apps\Daynote\Daynote.Desktop.exe");

        Assert.AreEqual(StartupTaskState.Disabled, await gateway.GetStateAsync(default));

        Assert.AreEqual(StartupTaskState.Enabled, await gateway.RequestEnableAsync(default));
        Assert.AreEqual(StartupTaskState.Enabled, await gateway.GetStateAsync(default));
        Assert.IsNotNull(RawValue(), "the entry has to exist in the Run key, not just in our answer");

        Assert.AreEqual(StartupTaskState.Disabled, await gateway.DisableAsync(default));
        Assert.AreEqual(StartupTaskState.Disabled, await gateway.GetStateAsync(default));
        Assert.IsNull(RawValue(), "disable has to remove the value, not blank it");
    }

    [TestMethod]
    public async Task The_command_is_quoted_so_a_path_with_spaces_survives()
    {
        // Unquoted, Windows would run "C:\Program" and pass the rest as arguments.
        const string path = @"C:\Program Files\Daynote\Daynote.Desktop.exe";
        WindowsRunKeyStartupTaskGateway gateway = Create(path);

        await gateway.RequestEnableAsync(default);

        Assert.AreEqual($"\"{path}\"", RawValue());
    }

    [TestMethod]
    public async Task Enabling_twice_leaves_one_entry_pointing_at_this_build()
    {
        WindowsRunKeyStartupTaskGateway first = Create(@"C:\Old\Daynote.Desktop.exe");
        await first.RequestEnableAsync(default);

        // An app that moved — reinstalled elsewhere — must not leave the old path behind.
        WindowsRunKeyStartupTaskGateway moved = Create(@"C:\New\Daynote.Desktop.exe");
        await moved.RequestEnableAsync(default);

        Assert.AreEqual(@"""C:\New\Daynote.Desktop.exe""", RawValue());
    }

    [TestMethod]
    public async Task Disabling_something_that_was_never_enabled_is_not_an_error()
    {
        WindowsRunKeyStartupTaskGateway gateway = Create(@"C:\Apps\Daynote.Desktop.exe");

        Assert.AreEqual(StartupTaskState.Disabled, await gateway.DisableAsync(default));
    }

    private WindowsRunKeyStartupTaskGateway Create(string executable) => new(_valueName, executable);

    private string? RawValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(_valueName) as string;
    }
}
