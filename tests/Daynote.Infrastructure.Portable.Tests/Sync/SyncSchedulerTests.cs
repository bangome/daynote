using System.Globalization;
using Daynote.App.Account;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Sync;

/// <summary>
/// When a device syncs without being asked (docs/CLOUD_SYNC.md §7.5). Every delay is released by
/// hand, so each test says exactly which timer fired and nothing depends on wall-clock time.
/// </summary>
[TestClass]
public sealed class SyncSchedulerTests
{
    private ManualDelays delays = null!;
    private ManualClock clock = null!;
    private int runs;
    private bool pendingEdits;
    private Func<Task<bool>> sync = null!;

    [TestInitialize]
    public void Setup()
    {
        delays = new ManualDelays();
        clock = new ManualClock();
        runs = 0;
        pendingEdits = false;
        sync = () =>
        {
            runs++;
            return Task.FromResult(true);
        };
    }

    private SyncScheduler Create() => new(() => sync(), () => pendingEdits, delays, clock);

    [TestMethod]
    public void Starting_syncs_once_straight_away()
    {
        using SyncScheduler scheduler = Create();

        scheduler.Start();

        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void A_save_syncs_only_after_the_debounce()
    {
        using SyncScheduler scheduler = Create();

        scheduler.NotifySaved();
        Assert.AreEqual(0, runs, "A save must not sync the moment it lands.");

        delays.Release(SyncScheduler.AfterSaveDelay);
        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void A_burst_of_saves_is_one_sync()
    {
        using SyncScheduler scheduler = Create();

        scheduler.NotifySaved();
        scheduler.NotifySaved();
        scheduler.NotifySaved();
        delays.Release(SyncScheduler.AfterSaveDelay);

        Assert.AreEqual(1, runs, "Each save restarts the debounce; only the last one may fire.");
    }

    [TestMethod]
    public void Nothing_runs_over_unsaved_text()
    {
        using SyncScheduler scheduler = Create();
        pendingEdits = true;

        scheduler.Start();
        scheduler.NotifySaved();
        delays.Release(SyncScheduler.AfterSaveDelay);
        delays.Release(SyncScheduler.IdleInterval);

        Assert.AreEqual(0, runs);
    }

    [TestMethod]
    public void The_idle_timer_keeps_syncing_a_device_left_open()
    {
        using SyncScheduler scheduler = Create();
        scheduler.Start();

        delays.Release(SyncScheduler.IdleInterval);
        delays.Release(SyncScheduler.IdleInterval);

        Assert.AreEqual(3, runs);
    }

    [TestMethod]
    public void A_run_skipped_for_unsaved_text_still_rearms_the_idle_timer()
    {
        using SyncScheduler scheduler = Create();
        pendingEdits = true;
        scheduler.Start();

        pendingEdits = false;
        delays.Release(SyncScheduler.IdleInterval);

        Assert.AreEqual(1, runs, "The skipped start must not leave the device without a timer.");
    }

    [TestMethod]
    public void Any_run_restarts_the_idle_timer()
    {
        using SyncScheduler scheduler = Create();
        scheduler.Start();
        Assert.AreEqual(1, delays.Pending(SyncScheduler.IdleInterval));

        scheduler.NotifySaved();
        delays.Release(SyncScheduler.AfterSaveDelay);

        Assert.AreEqual(1, delays.Pending(SyncScheduler.IdleInterval), "The old idle wait is replaced, not stacked.");
    }

    [TestMethod]
    public void Coming_back_soon_after_a_run_does_not_sync_again()
    {
        using SyncScheduler scheduler = Create();
        scheduler.Start();

        clock.Advance(SyncScheduler.ResumeCooldown - TimeSpan.FromSeconds(1));
        scheduler.NotifyResumed();
        Assert.AreEqual(1, runs);

        clock.Advance(TimeSpan.FromSeconds(1));
        scheduler.NotifyResumed();
        Assert.AreEqual(2, runs);
    }

    [TestMethod]
    public void Coming_back_before_any_run_syncs()
    {
        using SyncScheduler scheduler = Create();

        scheduler.NotifyResumed();

        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void A_skipped_run_does_not_start_the_resume_cooldown()
    {
        using SyncScheduler scheduler = Create();
        sync = () =>
        {
            runs++;
            return Task.FromResult(false);
        };
        scheduler.Start();

        scheduler.NotifyResumed();

        Assert.AreEqual(2, runs, "Only a run that happened counts as having synced recently.");
    }

    [TestMethod]
    public void Triggers_during_a_run_become_one_more_run_after_it()
    {
        var inFlight = new TaskCompletionSource<bool>();
        sync = () =>
        {
            runs++;
            return runs == 1 ? inFlight.Task : Task.FromResult(true);
        };
        using SyncScheduler scheduler = Create();

        scheduler.Start();
        scheduler.NotifyResumed();
        scheduler.NotifySaved();
        delays.Release(SyncScheduler.AfterSaveDelay);
        Assert.AreEqual(1, runs, "Nothing may overlap the run in flight.");

        inFlight.SetResult(true);
        Assert.AreEqual(2, runs, "What arrived during the run is caught up once, however many triggers it was.");
    }

    [TestMethod]
    public void Nothing_fires_after_dispose()
    {
        SyncScheduler scheduler = Create();
        scheduler.Start();
        scheduler.NotifySaved();

        scheduler.Dispose();
        delays.Release(SyncScheduler.AfterSaveDelay);
        delays.Release(SyncScheduler.IdleInterval);
        scheduler.NotifyResumed();
        scheduler.NotifySaved();

        Assert.AreEqual(1, runs);
    }

    /// <summary>Delays that complete only when the test releases them, inline on its thread.</summary>
    private sealed class ManualDelays : IAutosaveScheduler
    {
        private readonly List<(TimeSpan Delay, TaskCompletionSource Completion)> pending = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource();
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            pending.Add((delay, completion));
            return completion.Task;
        }

        public int Pending(TimeSpan delay) =>
            pending.Count(entry => entry.Delay == delay && !entry.Completion.Task.IsCompleted);

        public void Release(TimeSpan delay)
        {
            foreach ((TimeSpan _, TaskCompletionSource completion) in pending.Where(entry => entry.Delay == delay).ToList())
            {
                pending.RemoveAll(entry => entry.Completion == completion);
                completion.TrySetResult();
            }
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-09-28T12:00:00Z", CultureInfo.InvariantCulture);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
