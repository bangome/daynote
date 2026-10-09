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

            Pump(shell.DrainGlanceAsync);

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

            Pump(shell.DrainGlanceAsync);

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
