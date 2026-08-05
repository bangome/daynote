using Daynote.App.Lifecycle;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Instance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Lifecycle;

[TestClass]
public sealed class AppLifecycleCoordinatorTests
{
    private sealed class Harness
    {
        public RecordingTrayPresenter Tray { get; } = new();

        public RecordingWindowHost Window { get; } = new();

        public RecordingApplicationExit Exit { get; } = new();

        public SingleInstanceCoordinator? SingleInstance { get; }

        public RecordingActivationChannel? Channel { get; }

        public Func<FlushReason, CancellationToken, Task<FlushResult>> Flush { get; set; } =
            (_, _) => Task.FromResult(FlushResult.Proceed);

        public AppLifecycleCoordinator Coordinator { get; }

        public Harness(bool withSingleInstance = false)
        {
            if (withSingleInstance)
            {
                Channel = new RecordingActivationChannel();
                SingleInstance = new SingleInstanceCoordinator(new AlwaysPrimaryClaim(), Channel);
                SingleInstance.Start();
            }

            Coordinator = new AppLifecycleCoordinator(
                Tray,
                Window,
                Exit,
                (reason, token) => Flush(reason, token),
                log: null,
                singleInstance: SingleInstance);
        }
    }

    [TestMethod]
    public void Test_HideToTray_hides_window()
    {
        var harness = new Harness();

        harness.Coordinator.HideToTray();

        Assert.AreEqual(1, harness.Window.HideCount);
        Assert.AreEqual(false, harness.Tray.LastWindowShown);
    }

    [TestMethod]
    public void Test_TrayShow_activates_the_window()
    {
        var harness = new Harness();

        harness.Tray.RaiseShow();

        Assert.AreEqual(1, harness.Window.ShowCount);
        Assert.AreEqual(true, harness.Tray.LastWindowShown);
    }

    [TestMethod]
    public async Task Test_Quit_when_flush_fails_stays_running_and_re_shows_window()
    {
        var harness = new Harness(withSingleInstance: true);
        harness.Flush = (_, _) => Task.FromResult(
            FlushResult.Block(new RecoverableNoteError(NoteFailureCode.StorageUnavailable, "save failed")));

        bool quit = await harness.Coordinator.QuitAsync();

        Assert.IsFalse(quit, "A failed flush must keep the app running.");
        Assert.AreEqual(0, harness.Exit.ShutdownCount);
        Assert.IsFalse(harness.Tray.IsDisposed);
        Assert.IsFalse(harness.Channel!.IsDisposed);
        Assert.AreEqual(1, harness.Window.ShowCount, "The window is re-shown so the user can retry.");
    }

    [TestMethod]
    public async Task Test_Quit_after_flush_failure_succeeds_on_retry_and_disposes_resources()
    {
        var harness = new Harness(withSingleInstance: true);

        bool failing = true;
        harness.Flush = (_, _) => Task.FromResult(failing
            ? FlushResult.Block(new RecoverableNoteError(NoteFailureCode.StorageUnavailable, "save failed"))
            : FlushResult.Proceed);

        Assert.IsFalse(await harness.Coordinator.QuitAsync());

        failing = false;
        bool quit = await harness.Coordinator.QuitAsync();

        Assert.IsTrue(quit);
        Assert.AreEqual(1, harness.Exit.ShutdownCount);
        Assert.IsTrue(harness.Tray.IsDisposed, "A successful quit disposes the tray.");
        Assert.IsTrue(harness.Channel!.IsDisposed, "A successful quit disposes the single-instance pipe.");
    }

    [TestMethod]
    public void Test_TrayQuit_command_drives_the_quit_flush()
    {
        var harness = new Harness();
        var flushReasons = new List<FlushReason>();
        harness.Flush = (reason, _) =>
        {
            flushReasons.Add(reason);
            return Task.FromResult(FlushResult.Proceed);
        };

        harness.Tray.RaiseQuit();

        CollectionAssert.Contains(flushReasons, FlushReason.Quit);
        Assert.AreEqual(1, harness.Exit.ShutdownCount);
    }
}
