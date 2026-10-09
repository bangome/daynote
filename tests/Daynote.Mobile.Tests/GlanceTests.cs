using System.Text.Json;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Glance;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The widgets' and watch's side of the phone (docs/APPLE_EXTENSIONS.md): the snapshot it writes,
/// the queue it drains, and the links it follows.
/// </summary>
[TestClass]
public sealed class GlanceTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 14, 30, 0);

    [TestMethod]
    public void The_snapshot_carries_the_day_panel_s_rows_split_into_todos_and_events()
    {
        DateOnly today = DateOnly.FromDateTime(Now);
        AgendaItem room = Task("회의실 예약 확인", today, new TimeOnly(14, 0));
        AgendaItem logs = Task("퇴근 전 로그 확인", today, new TimeOnly(18, 0)) with { ListId = WorkList.Id };
        AgendaItem notes = Task("릴리즈 노트 작성", today, null);
        AgendaItem vitamins = Task("비타민 먹기", today, new TimeOnly(8, 0)) with
        {
            StartsAt = new WallClock(today.AddDays(-3).ToDateTime(new TimeOnly(8, 0))),
            DueAt = new WallClock(today.AddDays(-3).ToDateTime(new TimeOnly(8, 0))),
            Rrule = "FREQ=DAILY",
        };
        AgendaItem review = Event("디자인 리뷰", today, new TimeOnly(16, 0), new TimeOnly(17, 0));

        GlanceSnapshot snapshot = Build([room, logs, notes, vitamins, review]);

        GlanceDay day = snapshot.Days[0];
        Assert.AreEqual("2026-10-07", day.Date);
        CollectionAssert.AreEqual(
            new[] { "비타민 먹기", "회의실 예약 확인", "퇴근 전 로그 확인", "릴리즈 노트 작성" },
            day.Todos.Select(static todo => todo.Title).ToArray(),
            "Timed first and in time order, then the undated ones: the app's own order.");
        Assert.AreEqual("14:00", day.Todos[1].Time);
        Assert.IsNull(day.Todos[3].Time);

        GlanceTodo occurrence = day.Todos[0];
        Assert.IsTrue(occurrence.Repeats, "An occurrence of a rule draws the ↻.");
        Assert.AreEqual(vitamins.Id.ToString("D"), occurrence.SeriesId);
        Assert.AreEqual("2026-10-07T08:00", occurrence.Occurrence);

        Assert.AreEqual(1, day.Events.Count);
        Assert.AreEqual("16:00", day.Events[0].Start);
        Assert.AreEqual("17:00", day.Events[0].End);
        Assert.AreEqual(GlanceSnapshotBuilder.DaysAhead, snapshot.Days.Count);
    }

    [TestMethod]
    public void A_list_s_colour_is_its_place_in_the_sidebar()
    {
        GlanceSnapshot snapshot = Build([]);

        Assert.AreEqual("#ee7f35", snapshot.Lists[0].Color, "The built-in list is the brand orange.");
        Assert.AreEqual("#ff9a52", snapshot.Lists[0].ColorDark);
        Assert.AreEqual("#5a64c4", snapshot.Lists[1].Color);
        Assert.AreEqual(AppStrings.AgendaListDefaultName, snapshot.Lists[0].Name, "The built-in name is translated.");
        Assert.AreEqual("업무", snapshot.Lists[1].Name);
    }

    [TestMethod]
    public void A_locked_account_writes_that_it_is_locked_and_nothing_else()
    {
        DateOnly today = DateOnly.FromDateTime(Now);
        GlanceSnapshot snapshot = GlanceSnapshotBuilder.Build(
            [Task("비밀", today, null)], Lists, [], Now, Now, "Asia/Seoul", AppLanguage.Korean, locked: true);

        Assert.IsTrue(snapshot.Locked);
        Assert.AreEqual(0, snapshot.Days.Count);
        Assert.AreEqual(0, snapshot.Lists.Count);
        Assert.AreEqual(0, snapshot.Favorites.Count);
    }

    [TestMethod]
    public void The_file_is_camel_case_with_hangul_left_as_hangul()
    {
        string json = GlanceSnapshotBuilder.Serialize(Build([Task("회의실 예약 확인", DateOnly.FromDateTime(Now), null)]));

        StringAssert.Contains(json, "\"generatedUtc\":");
        StringAssert.Contains(json, "회의실 예약 확인");
        Assert.IsFalse(json.Contains("\"seriesId\":null", StringComparison.Ordinal), "Nulls are left out.");
    }

    [TestMethod]
    public void A_widget_check_is_drained_into_the_store_and_the_snapshot_follows()
    {
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            Pump(shell.StartGlanceAsync);
            IAgendaRepository agenda = Agenda();
            DateOnly today = LocalDates.ToDateOnly(shell.SelectedDate);
            AgendaItem item = Task("회의실 예약 확인", today, new TimeOnly(23, 59));
            Pump(() => agenda.SaveAsync(item).AsTask());
            Pump(shell.RefreshAllAsync);
            PumpUntil(() => host.Published.Count > 0 && host.Published[^1].Contains("회의실 예약 확인", StringComparison.Ordinal), "the snapshot to carry the to-do");

            // What the widget's intent does: one file in the queue.
            new GlanceFolder(host.Folder).Enqueue(new GlanceAction(
                1, Guid.NewGuid().ToString("D"), GlanceActionTypes.Complete, "2026-10-07T05:30:00Z",
                ItemId: item.Id.ToString("D"), Date: today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));

            shell.NotifyResumed();
            PumpUntil(() => Read(agenda, item.Id)?.Status == AgendaStatus.Completed, "the to-do to be completed");
            PumpUntil(() => Directory.GetFiles(Path.Combine(host.Folder, "actions")).Length == 0, "the queue to empty");
            PumpUntil(() => host.Published[^1].Contains("\"done\":true", StringComparison.Ordinal), "the snapshot to show it done");
        });
    }

    [TestMethod]
    public void A_watch_capture_makes_the_item_the_readback_offered_and_only_once()
    {
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            IAgendaRepository agenda = Agenda();
            var action = new GlanceAction(
                1, Guid.NewGuid().ToString("D"), GlanceActionTypes.Capture, "2026-10-07T05:30:00Z",
                Text: "회의자료 초안 공유 오늘 5시", Kind: "task", CapturedLocal: "2026-10-07T14:30");
            var folder = new GlanceFolder(host.Folder);
            folder.Enqueue(action);
            folder.Enqueue(action);

            Pump(shell.StartGlanceAsync);

            AgendaItem made = Read(agenda, Guid.Parse(action.Id))!;
            Assert.AreEqual("회의자료 초안 공유", made.Title);
            Assert.AreEqual(AgendaKind.Task, made.Kind);
            Assert.AreEqual(new DateTime(2026, 10, 7, 17, 0, 0), made.DueAt!.Value.Value);
            Assert.IsNull(made.SourceNoteId, "Said into a watch, not typed into a note.");
            Assert.AreEqual(1, Get(() => agenda.GetAllAsync().AsTask()).Count(static i => i.Title == "회의자료 초안 공유"));
        });
    }

    [TestMethod]
    public void A_sentence_with_no_date_goes_to_the_end_of_today_s_note()
    {
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            string today = LocalDates.ToDateOnly(shell.SelectedDate).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            new GlanceFolder(host.Folder).Enqueue(new GlanceAction(
                1, Guid.NewGuid().ToString("D"), GlanceActionTypes.Capture, "2026-10-07T05:30:00Z",
                Text: "아이디어 메모", Kind: "note", CapturedLocal: today + "T14:30"));

            Pump(shell.StartGlanceAsync);

            INoteRepository notes = (INoteRepository)TestServices.CurrentProvider!.GetService(typeof(INoteRepository))!;
            NoteSummary note = Get(() => notes.GetAllNotesAsync().AsTask()).Single();
            Assert.AreEqual("아이디어 메모", note.Body, "A day with no note gets one.");
            Assert.IsTrue(shell.DayCards.Count == 1, "The day on screen shows it.");
        });
    }

    [TestMethod]
    public void The_at_link_opens_today_s_note_with_the_bar_up()
    {
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            Pump(() => shell.OpenLinkAsync(new Uri("daynote://capture?at=1")));
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(shell.IsEditorOpen);
            Assert.IsTrue(shell.Notes.EditorText.EndsWith('@'));
            Assert.IsTrue(shell.Capture.IsOpen, "The bar opens on the @, as if it had been typed.");
        });
    }

    [TestMethod]
    public void Nothing_is_written_or_drained_before_the_shell_starts_it()
    {
        // Before the account is read nobody knows whether its lock has the notes sealed, and
        // before the day is read the rows are empty: either would be a wrong file on every widget.
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            new GlanceFolder(host.Folder).Enqueue(Capture("아이디어 메모", "note"));
            Pump(shell.RefreshAllAsync);
            shell.NotifyResumed();
            Pump(shell.DrainGlanceAsync);

            Assert.AreEqual(0, host.Published.Count);
            Assert.AreEqual(1, new GlanceFolder(host.Folder).ReadActions().Count, "The queue waits for the start.");

            Pump(shell.StartGlanceAsync);
            Assert.AreEqual(0, new GlanceFolder(host.Folder).ReadActions().Count);
            Assert.IsTrue(host.Published.Count > 0);
        });
    }

    [TestMethod]
    public void Drains_asked_for_together_apply_each_action_once()
    {
        // Cold launch: the start drains, the activation's resume drains, and a watch transfer
        // posts a third. Run side by side, two passes over one listing wrote the line twice.
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            var folder = new GlanceFolder(host.Folder);
            folder.Enqueue(Capture("아이디어 메모", "note"));

            System.Threading.Tasks.Task start = shell.StartGlanceAsync();
            shell.NotifyResumed();
            System.Threading.Tasks.Task again = shell.DrainGlanceAsync();
            host.Arrive();
            Pump(() => System.Threading.Tasks.Task.WhenAll(start, again));

            // A late arrival during the drain is run, not dropped.
            folder.Enqueue(Capture("두 번째 메모", "note"));
            System.Threading.Tasks.Task first = shell.DrainGlanceAsync();
            System.Threading.Tasks.Task second = shell.DrainGlanceAsync();
            Pump(() => System.Threading.Tasks.Task.WhenAll(first, second));
            PumpUntil(() => folder.ReadActions().Count == 0, "the second action to be applied");

            INoteRepository notes = (INoteRepository)TestServices.CurrentProvider!.GetService(typeof(INoteRepository))!;
            NoteSummary note = Get(() => notes.GetAllNotesAsync().AsTask()).Single();
            Assert.AreEqual("아이디어 메모\n두 번째 메모", note.Body);
        });
    }

    [TestMethod]
    public void An_occurrence_is_matched_by_which_occurrence_it_is_not_by_the_series_id()
    {
        // Every occurrence of a rule carries the series' id until it has an override, so matching
        // on id alone completes whichever occurrence happens to be on the day — here, a widget
        // still showing yesterday's (it had not redrawn past midnight) would have ticked today's.
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            Pump(shell.StartGlanceAsync);
            IAgendaRepository agenda = Agenda();
            DateOnly today = LocalDates.ToDateOnly(shell.SelectedDate);
            AgendaItem pills = Task("약 먹기", today.AddDays(-3), new TimeOnly(8, 0)) with
            {
                StartsAt = new WallClock(today.AddDays(-3).ToDateTime(new TimeOnly(8, 0))),
                Rrule = "FREQ=DAILY",
            };
            Pump(() => agenda.SaveAsync(pills).AsTask());

            string yesterday = new WallClock(today.AddDays(-1).ToDateTime(new TimeOnly(8, 0))).ToString();
            string todays = new WallClock(today.ToDateTime(new TimeOnly(8, 0))).ToString();
            var folder = new GlanceFolder(host.Folder);
            string day = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            folder.Enqueue(new GlanceAction(
                1, Guid.NewGuid().ToString("D"), GlanceActionTypes.Complete, "2026-10-07T05:30:00Z",
                ItemId: pills.Id.ToString("D"), SeriesId: pills.Id.ToString("D"), Occurrence: yesterday, Date: day));
            Pump(shell.DrainGlanceAsync);
            Assert.AreEqual(0, AgendaDay.For(today, Get(() => agenda.GetAllAsync().AsTask())).Done.Count);

            folder.Enqueue(new GlanceAction(
                1, Guid.NewGuid().ToString("D"), GlanceActionTypes.Complete, "2026-10-07T05:30:00Z",
                ItemId: pills.Id.ToString("D"), SeriesId: pills.Id.ToString("D"), Occurrence: todays, Date: day));
            Pump(shell.DrainGlanceAsync);
            AgendaDayView after = AgendaDay.For(today, Get(() => agenda.GetAllAsync().AsTask()));
            Assert.AreEqual(1, after.Done.Count);
            Assert.AreEqual(todays, after.Done[0].RecurrenceId!.Value.ToString());
        });
    }

    [TestMethod]
    public void An_action_that_fails_is_kept_for_a_day_and_an_unknown_one_is_dropped()
    {
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            DateTime now = DateTime.UtcNow;
            var coordinator = new Glance.GlanceCoordinator(
                host, Agenda(), (INoteRepository)TestServices.CurrentProvider!.GetService(typeof(INoteRepository))!,
                (Daynote.Core.Time.IClock)TestServices.CurrentProvider!.GetService(typeof(Daynote.Core.Time.IClock))!, () => now);
            var folder = new GlanceFolder(host.Folder);
            folder.Enqueue(Capture("실패하는 메모", "note"));
            folder.Enqueue(Capture("다시 해 볼 메모", "note") with { Text = "retry" });
            folder.Enqueue(new GlanceAction(1, Guid.NewGuid().ToString("D"), "teleport", "2026-10-07T05:30:00Z"));

            Pump(() => coordinator.StartAsync(
                action => action.Text switch
                {
                    "retry" => System.Threading.Tasks.Task.FromResult(new GlanceApplied(false, Retry: true)),
                    "실패하는 메모" => throw new IOException("database is locked"),
                    _ => System.Threading.Tasks.Task.FromResult(new GlanceApplied(false)),
                },
                () => System.Threading.Tasks.Task.CompletedTask,
                () => false));

            Assert.AreEqual(2, folder.ReadActions().Count, "The failure and the retry stay; the unknown type goes.");

            now = now.AddDays(2);
            Pump(coordinator.DrainAsync);
            Assert.AreEqual(0, folder.ReadActions().Count, "Past a day they are given up on.");
        });
    }

    [TestMethod]
    public void The_lock_rewrites_the_snapshot_both_ways()
    {
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlanceAndAccount(host), (_, shell) =>
        {
            DateOnly today = LocalDates.ToDateOnly(shell.SelectedDate);
            Pump(() => Agenda().SaveAsync(Task("비밀 회의", today, new TimeOnly(23, 59))).AsTask());
            Pump(shell.StartGlanceAsync);
            PumpUntil(() => host.Published.Count > 0 && host.Published[^1].Contains("비밀 회의", StringComparison.Ordinal), "the open snapshot");

            shell.Account!.IsLocked = true;
            PumpUntil(() => host.Published[^1].Contains("\"locked\":true", StringComparison.Ordinal), "the locked snapshot");
            Assert.IsFalse(host.Published[^1].Contains("비밀 회의", StringComparison.Ordinal));

            shell.Account.IsLocked = false;
            PumpUntil(() => host.Published[^1].Contains("비밀 회의", StringComparison.Ordinal), "the unlocked snapshot");
        });
    }

    [TestMethod]
    public void A_widget_s_late_write_is_noticed_because_the_file_is_compared()
    {
        var host = new FakeGlanceHost();
        TestServices.WithInitialisedShell(390, 844, WithGlance(host), (_, shell) =>
        {
            Pump(shell.StartGlanceAsync);
            int published = host.Published.Count;
            string path = Path.Combine(host.Folder, GlanceFolder.SnapshotFileName);

            // The widget's read-modify-write landing after the app's file: an older day.
            File.WriteAllText(path, host.Published[^1].Replace("\"today\":\"", "\"today\":\"1999-", StringComparison.Ordinal));
            Pump(shell.RefreshAllAsync);
            PumpUntil(() => host.Published.Count > published, "the app to write its own again");

            // And an identical file is left alone.
            int now = host.Published.Count;
            Pump(shell.RefreshAllAsync);
            Assert.AreEqual(now, host.Published.Count);
        });
    }

    private static GlanceAction Capture(string text, string kind) => new(
        1, Guid.NewGuid().ToString("D"), GlanceActionTypes.Capture, "2026-10-07T05:30:00Z",
        Text: text, Kind: kind, CapturedLocal: DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    private static TestServices.ShellSetup WithGlanceAndAccount(FakeGlanceHost host) =>
        TestServices.ShellSetup.WithAccount() with
        {
            Platform = platform => platform with
            {
                SecretProtector = new AccountPanelTests.XorProtector(),
                Identity = new AccountPanelTests.NoGoogle(),
                Glance = host,
            },
        };

    private static readonly AgendaList WorkList = new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"), "업무", 1, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static readonly AgendaList[] Lists =
    [
        new(AgendaList.DefaultId, string.Empty, 0, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        WorkList,
    ];

    private static GlanceSnapshot Build(IReadOnlyList<AgendaItem> items) =>
        GlanceSnapshotBuilder.Build(items, Lists, [], Now, Now, "Asia/Seoul", AppLanguage.Korean, locked: false);

    private static AgendaItem Task(string title, DateOnly day, TimeOnly? time) => new(
        Guid.NewGuid(),
        AgendaList.DefaultId,
        AgendaKind.Task,
        title,
        string.Empty,
        "Asia/Seoul",
        StartsAt: null,
        EndsAt: null,
        DueAt: new WallClock(day.ToDateTime(time ?? TimeOnly.MinValue)),
        HasDueTime: time is not null,
        Rrule: null,
        SeriesId: null,
        RecurrenceId: null,
        AgendaStatus.NeedsAction,
        CompletedUtc: null,
        Priority: 0,
        TimelineVisibility.Auto,
        SourceNoteId: null,
        ExceptionDates: [],
        AgendaAlert.None,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private static AgendaItem Event(string title, DateOnly day, TimeOnly start, TimeOnly end) =>
        Task(title, day, null) with
        {
            Kind = AgendaKind.Event,
            DueAt = null,
            StartsAt = new WallClock(day.ToDateTime(start)),
            EndsAt = new WallClock(day.ToDateTime(end)),
        };

    private static TestServices.ShellSetup WithGlance(FakeGlanceHost host) =>
        new(Platform: platform => platform with { Glance = host });

    private static IAgendaRepository Agenda() =>
        (IAgendaRepository)TestServices.CurrentProvider!.GetService(typeof(IAgendaRepository))!;

    private static AgendaItem? Read(IAgendaRepository agenda, Guid id) => Get(() => agenda.GetAsync(id).AsTask());

    private static T Get<T>(Func<Task<T>> work)
    {
        T result = default!;
        Pump(async () => { result = await work().ConfigureAwait(true); });
        return result;
    }

    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The command did not complete within 20 seconds.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    private static void PumpUntil(Func<bool> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, $"Timed out waiting for {failure}.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    /// <summary>A folder in the temp directory standing in for the App Group container.</summary>
    private sealed class FakeGlanceHost : IGlanceHost
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "daynote-glance-tests", Guid.NewGuid().ToString("N"));

        string? IGlanceHost.Folder => Folder;

        public List<string> Published { get; } = [];

        public event EventHandler? ActionsArrived;

        void IGlanceHost.Published(string snapshotJson)
        {
            // Whatever reaches the extensions has to be the file they will read.
            Assert.AreEqual(File.ReadAllText(Path.Combine(Folder, GlanceFolder.SnapshotFileName)), snapshotJson);
            using JsonDocument _ = JsonDocument.Parse(snapshotJson);
            Published.Add(snapshotJson);
        }

        internal void Arrive() => ActionsArrived?.Invoke(this, EventArgs.Empty);
    }
}
