using Daynote.App.Glance;
using Daynote.Desktop.Platform;

namespace Daynote.Desktop.Tests;

/// <summary>
/// When the Mac app writes its widgets' snapshot and how it drains their queue
/// (docs/APPLE_EXTENSIONS.md §10).
/// </summary>
[TestClass]
public sealed class GlanceRelayTests
{
    private string _root = string.Empty;
    private GlanceFolder _folder = null!;
    private GlanceSnapshot _current = null!;
    private int _reloads;
    private int _refreshes;
    private readonly List<string> _applied = [];

    [TestInitialize]
    public void CreateFolder()
    {
        _root = Directory.CreateTempSubdirectory("daynote-glance-").FullName;
        _folder = new GlanceFolder(_root);
        _current = Snapshot("회의실 예약 확인", done: false);
    }

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public async Task Nothing_is_drained_or_published_before_the_shell_has_read_the_day()
    {
        _folder.Enqueue(Complete("a"));
        GlanceRelay relay = Relay();

        await relay.DrainAsync();
        Assert.IsFalse(await relay.PublishAsync(force: true));

        Assert.IsEmpty(_applied);
        Assert.IsFalse(File.Exists(_folder.SnapshotPath));
        Assert.HasCount(1, _folder.ReadActions());

        await relay.StartAsync();

        CollectionAssert.AreEqual(new[] { "a" }, _applied);
        Assert.IsTrue(File.Exists(_folder.SnapshotPath));
    }

    [TestMethod]
    public async Task A_tick_queued_during_a_drain_is_applied_before_the_republish()
    {
        GlanceRelay? relay = null;
        bool queuedSecond = false;
        relay = Relay(apply: async action =>
        {
            _applied.Add(action.Id);
            if (!queuedSecond)
            {
                // The widget ticks again while the first is applied; the watcher's drain call
                // arrives while this one is still running.
                queuedSecond = true;
                _folder.Enqueue(Complete("b"));
                await relay!.DrainAsync();
            }

            return false;
        });
        await relay.StartAsync();
        _folder.Enqueue(Complete("a"));

        await relay.DrainAsync();

        CollectionAssert.AreEqual(new[] { "a", "b" }, _applied);
        Assert.IsEmpty(_folder.ReadActions());
        // The last thing written came after the second tick, so it cannot un-tick it.
        Assert.AreEqual(2, _refreshes);
    }

    [TestMethod]
    public async Task An_action_that_throws_is_kept_and_retried_and_a_broken_one_is_dropped()
    {
        bool busy = true;
        GlanceRelay relay = Relay(apply: action =>
        {
            if (busy)
            {
                throw new InvalidOperationException("database is locked");
            }

            _applied.Add(action.Id);
            return Task.FromResult(false);
        });
        await relay.StartAsync();
        _folder.Enqueue(Complete("a"));
        File.WriteAllText(Path.Combine(_folder.ActionsPath, "000000000000001-broken.json"), "{ not json");

        await relay.DrainAsync();

        Assert.HasCount(1, _folder.ReadActions());
        Assert.IsEmpty(_applied);

        busy = false;
        await relay.DrainAsync();

        CollectionAssert.AreEqual(new[] { "a" }, _applied);
        Assert.IsEmpty(_folder.ReadActions());
    }

    [TestMethod]
    public async Task An_action_the_applier_asks_to_retry_is_kept()
    {
        bool retry = true;
        GlanceRelay relay = Relay(apply: action =>
        {
            _applied.Add(action.Id);
            return Task.FromResult(retry);
        });
        await relay.StartAsync();
        _folder.Enqueue(Complete("a"));

        await relay.DrainAsync();
        Assert.HasCount(1, _folder.ReadActions());

        retry = false;
        await relay.DrainAsync();
        Assert.IsEmpty(_folder.ReadActions());
        CollectionAssert.AreEqual(new[] { "a", "a" }, _applied);
    }

    [TestMethod]
    public async Task An_action_that_keeps_throwing_is_given_up_after_a_day()
    {
        DateTime now = DateTime.UtcNow;
        GlanceRelay relay = Relay(apply: _ => throw new InvalidOperationException("always"), utcNow: () => now);
        await relay.StartAsync();
        _folder.Enqueue(Complete("a"));

        await relay.DrainAsync();
        Assert.HasCount(1, _folder.ReadActions());

        now += GlanceRelay.RetryFor + TimeSpan.FromMinutes(1);
        await relay.DrainAsync();
        Assert.IsEmpty(_folder.ReadActions());
    }

    [TestMethod]
    public async Task A_snapshot_the_widget_wrote_over_ours_is_written_again()
    {
        GlanceRelay relay = Relay();
        await relay.StartAsync();
        int reloads = _reloads;

        // Unchanged content: nothing written, nothing reloaded, whatever the stamp says.
        _current = _current with { GeneratedUtc = "2026-10-07T06:00:00Z" };
        Assert.IsFalse(await relay.PublishAsync());
        Assert.AreEqual(reloads, _reloads);

        // The widget's read-modify-write lands after ours, carrying an older picture.
        _folder.WriteSnapshot(GlanceSnapshotBuilder.Serialize(Snapshot("어제 것", done: true)));

        Assert.IsTrue(await relay.PublishAsync());
        Assert.AreEqual(reloads + 1, _reloads);
        StringAssert.Contains(File.ReadAllText(_folder.SnapshotPath), "회의실 예약 확인");
    }

    private GlanceRelay Relay(Func<GlanceAction, Task<bool>>? apply = null, Func<DateTime>? utcNow = null) => new(
        _folder,
        () => Task.FromResult(_current),
        apply ?? (action =>
        {
            _applied.Add(action.Id);
            return Task.FromResult(false);
        }),
        () =>
        {
            _refreshes++;
            return Task.CompletedTask;
        },
        () => _reloads++,
        utcNow);

    private static GlanceAction Complete(string id) =>
        new(1, id, GlanceActionTypes.Complete, "2026-10-07T05:30:00Z", ItemId: Guid.NewGuid().ToString("D"), Date: "2026-10-07");

    private static GlanceSnapshot Snapshot(string title, bool done) => new(
        GlanceSnapshot.CurrentSchema,
        "2026-10-07T05:30:00Z",
        "Asia/Seoul",
        "ko",
        "2026-10-07",
        false,
        [],
        [new GlanceDay("2026-10-07", [new GlanceTodo("1", null, null, title, "l", "14:00", false, done, null, null)], [])],
        [],
        []);
}
