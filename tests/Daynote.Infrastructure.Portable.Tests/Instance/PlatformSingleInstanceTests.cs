using Daynote.Infrastructure.Instance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Instance;

/// <summary>
/// Which single-instance primitive each OS gets, and why Windows is not the portable one.
/// </summary>
/// <remarks>
/// The Avalonia app could use the lock file everywhere — it works on Windows. It uses the named mutex
/// there because that is what the WPF shell holds, under the same base name, so while both builds
/// exist a second launch of either activates the one already running rather than opening a second
/// process on the same database. Swapping this back to the portable pair would compile, pass every
/// other test, and quietly reintroduce that.
/// </remarks>
[TestClass]
public sealed class PlatformSingleInstanceTests
{
    [TestMethod]
    public async Task Windows_takes_the_named_mutex_so_it_meets_the_WPF_shell()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only: there is no other shell to meet elsewhere.");
            return;
        }

        await using SingleInstanceCoordinator coordinator =
            SingleInstanceCoordinator.ForCurrentUserOnThisPlatform("DaynoteTest");

        Assert.IsInstanceOfType<MutexPrimaryClaim>(
            coordinator.PrimaryClaim,
            "Windows must claim the named mutex; the file lock is invisible to the WPF shell.");
    }

    [TestMethod]
    public async Task Everywhere_else_takes_the_lock_file()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Covered by the Windows case above.");
            return;
        }

        await using SingleInstanceCoordinator coordinator =
            SingleInstanceCoordinator.ForCurrentUserOnThisPlatform("DaynoteTest");

        Assert.IsInstanceOfType<FileLockPrimaryClaim>(coordinator.PrimaryClaim);
    }

    [TestMethod]
    public async Task Two_lock_file_claims_in_one_process_still_exclude()
    {
        // Only meaningful off Windows. A named mutex is owned by the THREAD and is recursive, so two
        // claims inside one process both succeed there and an in-process test cannot say anything
        // about exclusion. The Windows path was verified across real processes instead, in all three
        // pairings (Avalonia/Avalonia, WPF/Avalonia, Avalonia/WPF) — see
        // docs/WINDOWS_ON_AVALONIA.md §2.
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("A named mutex is recursive within a process; see the comment.");
            return;
        }

        await using SingleInstanceCoordinator first =
            SingleInstanceCoordinator.ForCurrentUserOnThisPlatform("DaynoteTest");
        await using SingleInstanceCoordinator second =
            SingleInstanceCoordinator.ForCurrentUserOnThisPlatform("DaynoteTest");

        Assert.AreEqual(SingleInstanceRole.Primary, first.Start());
        Assert.AreEqual(SingleInstanceRole.Secondary, second.Start());
    }
}
