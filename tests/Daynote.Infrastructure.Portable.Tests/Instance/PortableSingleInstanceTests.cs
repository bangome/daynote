using Daynote.Infrastructure.Instance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Instance;

[TestClass]
public sealed class PortableSingleInstanceTests
{
    [TestMethod]
    public void FileLock_second_claim_on_the_same_path_fails_until_the_first_is_released()
    {
        using var root = new TempDirectory();
        string path = Path.Combine(root.Path, "daynote.lock");

        using var first = new FileLockPrimaryClaim(path);
        Assert.IsTrue(first.TryClaim());
        Assert.IsTrue(first.TryClaim(), "a re-claim by the holder is idempotent");

        using var second = new FileLockPrimaryClaim(path);
        Assert.IsFalse(second.TryClaim());

        first.Dispose();
        Assert.IsTrue(second.TryClaim(), "released lock is claimable again");
    }

    [TestMethod]
    public async Task Socket_channel_delivers_one_activation_per_signal_and_reports_when_nobody_listens()
    {
        using var root = new TempDirectory();
        string socket = Path.Combine(root.Path, "d.sock");

        await using var idle = new UnixDomainSocketActivationChannel(socket);
        Assert.IsFalse(await idle.SignalAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None), "no listener yet");

        await using var primary = new UnixDomainSocketActivationChannel(socket);
        using var signaled = new SemaphoreSlim(0, 10);
        primary.StartListening(() => signaled.Release());

        await using var secondary = new UnixDomainSocketActivationChannel(socket);
        Assert.IsTrue(await secondary.SignalAsync(TimeSpan.FromSeconds(2), CancellationToken.None));
        Assert.IsTrue(await secondary.SignalAsync(TimeSpan.FromSeconds(2), CancellationToken.None));

        Assert.IsTrue(await signaled.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(await signaled.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(await signaled.WaitAsync(TimeSpan.FromMilliseconds(200)), "exactly two activations");
    }

    [TestMethod]
    public async Task Coordinator_over_portable_primitives_elects_one_primary_and_routes_activation_to_it()
    {
        using var root = new TempDirectory();

        await using SingleInstanceCoordinator primary = SingleInstanceCoordinator.ForCurrentUserPortable("DaynoteTest", root.Path);
        using var activated = new SemaphoreSlim(0, 1);
        primary.ActivationRequested += (_, _) => activated.Release();
        Assert.AreEqual(SingleInstanceRole.Primary, primary.Start());

        await using SingleInstanceCoordinator secondary = SingleInstanceCoordinator.ForCurrentUserPortable("DaynoteTest", root.Path);
        Assert.AreEqual(SingleInstanceRole.Secondary, secondary.Start());
        Assert.IsTrue(await secondary.ActivatePrimaryAsync(TimeSpan.FromSeconds(2)));
        Assert.IsTrue(await activated.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task Listener_reclaims_a_stale_socket_file_left_by_a_dead_primary()
    {
        using var root = new TempDirectory();
        string socket = Path.Combine(root.Path, "stale.sock");
        File.WriteAllText(socket, "not a socket");

        await using var primary = new UnixDomainSocketActivationChannel(socket);
        primary.StartListening(() => { });

        await using var secondary = new UnixDomainSocketActivationChannel(socket);
        Assert.IsTrue(await secondary.SignalAsync(TimeSpan.FromSeconds(2), CancellationToken.None));
    }

    [TestMethod]
    public void Runtime_directory_is_created_and_private_to_the_user()
    {
        string directory = InstanceRuntimeDirectory.ForCurrentUser("DaynotePortableTest");
        try
        {
            Assert.IsTrue(Directory.Exists(directory));
            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode mode = File.GetUnixFileMode(directory);
                Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, mode);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
