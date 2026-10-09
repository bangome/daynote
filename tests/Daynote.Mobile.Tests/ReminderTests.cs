using Daynote.Core.Agenda;
using System.Globalization;
using System.Reflection;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Settings;
using Daynote.Core.Time;
using Daynote.Mobile.Reminders;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// To-do reminders: which to-dos remind and when (the planner), what a pass tells the platform (the
/// coordinator, against a fake platform), and the settings rows and the tap (the shell, headless).
/// </summary>
/// <remarks>
/// Every test runs at a fixed "now", 2 October 2026 10:00 in Seoul, so a due date written as "10/3"
/// is tomorrow on any day the suite happens to run.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ReminderTests
{
    private static readonly TimeSpan Seoul = TimeSpan.FromHours(9);
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 10, 0, 0, Seoul);
    private static readonly LocalDate Today = LocalDates.FromDateOnly(new DateOnly(2026, 10, 2));

    // ── The planner ──────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_date_and_time_reminds_at_that_time_and_a_date_alone_at_nine()
    {
        IReadOnlyList<Reminder> plan = Plan(Note("주간회의 준비", "-[] 회의자료 공유 (10/3 14:00)\n-[] 회의실 예약 (10/4)"));

        Assert.HasCount(2, plan);
        Assert.AreEqual(new DateTime(2026, 10, 3, 14, 0, 0), plan[0].At);
        Assert.AreEqual("회의자료 공유", plan[0].Title);
        Assert.AreEqual(new DateTime(2026, 10, 4, 9, 0, 0), plan[1].At);
    }

    [TestMethod]
    public void A_date_alone_reminds_at_the_configured_time()
    {
        IReadOnlyList<Reminder> plan = ReminderPlanner.Plan(
            [Note("n", "-[] 장보기 (10/4)\n-[] 통화 (10/4 8:15)")], Now, 64, new TimeSpan(7, 30, 0));

        Assert.AreEqual(new DateTime(2026, 10, 4, 7, 30, 0), plan.Single(r => r.Title == "장보기").At);
        Assert.AreEqual(new DateTime(2026, 10, 4, 8, 15, 0), plan.Single(r => r.Title == "통화").At, "A to-do with its own time ignores the default.");
    }

    [TestMethod]
    public void Past_checked_and_undated_to_dos_do_not_remind()
    {
        IReadOnlyList<Reminder> plan = Plan(Note("n",
            "-[] 이미 지남 (10/2 9:59)\n" +
            "-[] 오늘 아침 (10/2)\n" +      // 09:00 today is already past at 10:00
            "-[x] 끝냄 (10/5 10:00)\n" +
            "-[] 날짜 없음\n" +
            "- 그냥 목록 (10/5 10:00)\n" +
            "-[] 남은 것 (10/2 10:01)"));

        Assert.AreEqual("남은 것", plan.Single().Title);
    }

    [TestMethod]
    public void The_body_is_the_note_title_and_the_due_label()
    {
        WithLanguage(AppLanguage.Korean, () =>
            Assert.AreEqual("주간회의 준비 · 10/3 14:00", Plan(Note("주간회의 준비", "-[] 자료 (10/3 14:00)")).Single().Body));
        WithLanguage(AppLanguage.English, () =>
            Assert.AreEqual("Weekly sync · 10/3", Plan(Note("Weekly sync", "-[] deck (10/3)")).Single().Body));
    }

    [TestMethod]
    public void An_id_survives_moving_the_line_and_changing_its_time_but_not_its_text()
    {
        Guid id = Guid.NewGuid();
        string first = Plan(Note("n", "-[] 전화하기 (10/3 14:00)", id)).Single().Id;

        Assert.AreEqual(first, Plan(Note("n", "메모\n\n위에 줄 추가\n-[] 전화하기 (10/5 9:00)", id)).Single().Id,
            "Moving the line or its time gave the to-do a new id, so it would be cancelled and added again.");
        Assert.AreNotEqual(first, Plan(Note("n", "-[] 문자하기 (10/3 14:00)", id)).Single().Id);
        Assert.AreNotEqual(first, Plan(Note("n", "-[] 전화하기 (10/3 14:00)")).Single().Id, "Another note's to-do shares the id.");
    }

    [TestMethod]
    public void Duplicate_to_dos_in_one_note_keep_their_own_ids_when_one_is_ticked()
    {
        Guid id = Guid.NewGuid();
        IReadOnlyList<Reminder> both = Plan(Note("n", "-[] 물 마시기 (10/3 10:00)\n-[] 물 마시기 (10/3 15:00)", id));
        IReadOnlyList<Reminder> second = Plan(Note("n", "-[x] 물 마시기 (10/3 10:00)\n-[] 물 마시기 (10/3 15:00)", id));

        Assert.HasCount(2, both);
        Assert.AreNotEqual(both[0].Id, both[1].Id);
        Assert.AreEqual(both[1].Id, second.Single().Id);
    }

    [TestMethod]
    public void Only_the_nearest_fit_under_the_cap_in_time_order()
    {
        // 70 to-dos, one per hour from 11:00 today, written in reverse so note order is not time order.
        string body = string.Join('\n', Enumerable.Range(0, 70).Reverse().Select(i =>
        {
            DateTime at = new DateTime(2026, 10, 2, 11, 0, 0).AddHours(i);
            return string.Create(CultureInfo.InvariantCulture, $"-[] 할 일 {i} ({at.Month}/{at.Day} {at.Hour}:00)");
        }));

        IReadOnlyList<Reminder> plan = Plan(Note("n", body));

        Assert.HasCount(64, plan);
        Assert.AreEqual("할 일 0", plan[0].Title);
        Assert.AreEqual("할 일 63", plan[^1].Title);
        CollectionAssert.AreEqual(plan.OrderBy(r => r.At).ToList(), plan.ToList());
    }

    // ── The coordinator ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void A_second_pass_with_nothing_changed_tells_the_platform_nothing()
    {
        using var harness = new Harness();
        harness.Notes.Add(Note("n", "-[] a (10/3 14:00)\n-[] b (10/4)"));

        harness.Run();
        Assert.HasCount(2, harness.Platform.Pending);
        Assert.HasCount(1, harness.Platform.Applied);

        harness.Run();
        Assert.HasCount(1, harness.Platform.Applied, "An unchanged pass rescheduled anyway.");
    }

    [TestMethod]
    public void Ticking_editing_and_deleting_cancel_their_reminders()
    {
        using var harness = new Harness();
        Guid id = Guid.NewGuid();
        harness.Notes.Add(Note("n", "-[] 체크할 것 (10/3 14:00)\n-[] 고칠 것 (10/3 15:00)\n-[] 시간 바꿀 것 (10/3 16:00)", id));
        Guid other = Guid.NewGuid();
        harness.Notes.Add(Note("m", "-[] 지울 노트 (10/3 17:00)", other));
        harness.Run();
        Assert.HasCount(4, harness.Platform.Pending);
        string moved = harness.Platform.Pending.Values.Single(r => r.Title == "시간 바꿀 것").Id;

        harness.Notes[0] = Note("n", "-[x] 체크할 것 (10/3 14:00)\n-[] 고친 것 (10/3 15:00)\n-[] 시간 바꿀 것 (10/5 8:00)", id);
        harness.Notes.RemoveAt(1);
        harness.Run();

        CollectionAssert.AreEquivalent(new[] { "고친 것", "시간 바꿀 것" }, harness.Platform.Pending.Values.Select(r => r.Title).ToArray());
        Assert.AreEqual(new DateTime(2026, 10, 5, 8, 0, 0), harness.Platform.Pending[moved].At, "The new time did not replace the old one.");
        ReminderChanges last = harness.Platform.Applied[^1];
        Assert.HasCount(3, last.Cancel, "Ticked, renamed-away and deleted reminders are the cancellations.");
    }

    [TestMethod]
    public void Switching_off_cancels_everything_and_on_brings_it_back()
    {
        using var harness = new Harness();
        harness.Notes.Add(Note("n", "-[] a (10/3 14:00)\n-[] b (10/4)"));
        harness.Run();

        Wait(harness.Coordinator.SetEnabledAsync(false));
        Assert.IsEmpty(harness.Platform.Pending);
        Assert.AreEqual("off", Wait(harness.Settings.GetAsync(ReminderCoordinator.EnabledKey).AsTask()));

        Wait(harness.Coordinator.SetEnabledAsync(true));
        Assert.HasCount(2, harness.Platform.Pending);
    }

    [TestMethod]
    public void Permission_is_asked_once_and_only_when_there_is_something_to_remind()
    {
        using var harness = new Harness();
        harness.Platform.Permission = ReminderPermission.NotDetermined;
        harness.Platform.AnswerToRequest = ReminderPermission.Denied;

        harness.Notes.Add(Note("n", "-[] 날짜 없음"));
        harness.Run();
        Assert.AreEqual(0, harness.Platform.Requests, "Asked with nothing to remind about.");
        Assert.AreEqual(ReminderPermission.NotDetermined, harness.Coordinator.Permission);

        harness.Notes.Add(Note("m", "-[] 내일 (10/3 9:00)"));
        harness.Run();
        Assert.AreEqual(1, harness.Platform.Requests);
        Assert.IsEmpty(harness.Platform.Pending, "A denial was not respected.");
        Assert.AreEqual(ReminderPermission.Denied, harness.Coordinator.Permission);

        // Android answers "not determined" again after a denial; having asked, that is a denial.
        harness.Platform.Permission = ReminderPermission.NotDetermined;
        harness.Run();
        harness.Restart().Run();
        Assert.AreEqual(1, harness.Platform.Requests, "Asked a second time.");
        Assert.AreEqual(ReminderPermission.Denied, harness.Coordinator.Permission);

        // Allowed later in the system settings: the next pass (on resume) schedules.
        harness.Platform.Permission = ReminderPermission.Granted;
        harness.Run();
        Assert.HasCount(1, harness.Platform.Pending);
        Assert.AreEqual(ReminderPermission.Granted, harness.Coordinator.Permission);
    }

    [TestMethod]
    public void A_new_default_time_reschedules_the_date_only_reminders_at_once_and_is_kept()
    {
        using var harness = new Harness();
        harness.Notes.Add(Note("n", "-[] 날짜만 (10/4)\n-[] 시각도 (10/4 15:00)"));
        harness.Run();
        string timed = harness.Platform.Pending.Values.Single(r => r.Title == "시각도").Id;

        Wait(harness.Coordinator.SetDateOnlyTimeAsync(new TimeSpan(7, 30, 0)));

        Assert.AreEqual(new DateTime(2026, 10, 4, 7, 30, 0), harness.Platform.Pending.Values.Single(r => r.Title == "날짜만").At);
        ReminderChanges change = harness.Platform.Applied[^1];
        Assert.AreEqual("날짜만", change.Schedule.Single().Title, "The timed to-do was rescheduled along with the date-only one.");
        Assert.IsFalse(change.Cancel.Contains(timed));

        // Persisted: a fresh start (same settings store) still uses it.
        Assert.AreEqual(new TimeSpan(7, 30, 0), Wait(ReminderCoordinator.GetDateOnlyTimeAsync(harness.Settings)));
        Harness restarted = harness.Restart();
        harness.Platform.Pending.Clear();
        File.Delete(Path.Combine(harness.Root.Path, ReminderStateStore.FileName));
        restarted.Run();
        Assert.AreEqual(new DateTime(2026, 10, 4, 7, 30, 0), harness.Platform.Pending.Values.Single(r => r.Title == "날짜만").At);
    }

    [TestMethod]
    public void A_profile_switch_clears_the_old_profiles_reminders()
    {
        using var harness = new Harness();
        harness.Notes.Add(Note("old", "-[] 이전 프로필 (10/3 14:00)"));
        harness.Run();
        Assert.HasCount(1, harness.Platform.Pending);

        Wait(harness.Coordinator.ClearAsync());
        Assert.IsEmpty(harness.Platform.Pending);

        // Nothing more runs on the closed profile.
        harness.Run();
        Assert.IsEmpty(harness.Platform.Pending);

        // The new profile's own to-dos, and only those.
        Harness next = harness.Restart();
        harness.Notes.Clear();
        harness.Notes.Add(Note("new", "-[] 새 프로필 (10/3 15:00)"));
        next.Run();
        Assert.AreEqual("새 프로필", harness.Platform.Pending.Values.Single().Title);
    }

    [TestMethod]
    public void A_switch_that_skipped_the_clear_still_cancels_the_old_profiles_reminders()
    {
        using var harness = new Harness();
        harness.Notes.Add(Note("old", "-[] 이전 프로필 (10/3 14:00)"));
        harness.Run();

        // The device-level state still lists the old one; the new profile's first pass cancels it.
        Harness next = harness.Restart();
        harness.Notes.Clear();
        next.Run();
        Assert.IsEmpty(harness.Platform.Pending);
    }

    [TestMethod]
    public void The_state_file_round_trips()
    {
        using var root = new TempDataRoot();
        ReminderStateStore store = ReminderStateStore.InFolder(root.Path);
        var reminder = new Reminder("todo-0123456789abcdef", new DateTime(2026, 10, 3, 14, 0, 0), "t", "b", Today, Guid.NewGuid());
        store.Save(new ReminderState(true, "할 일 알림", "d", [reminder]));

        ReminderState loaded = store.Load();
        Assert.IsTrue(loaded.PermissionAsked);
        Assert.AreEqual("할 일 알림", loaded.ChannelName);
        Assert.AreEqual(reminder, loaded.Scheduled.Single());
        Assert.AreEqual(unchecked((int)0x89abcdef), reminder.RequestCode);

        File.WriteAllText(Path.Combine(root.Path, ReminderStateStore.FileName), "{ not json");
        Assert.IsEmpty(store.Load().Scheduled, "A corrupt file did not read as empty.");
    }

    // ── The shell ────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_settings_rows_switch_reminders_off_and_hide_the_time()
    {
        var platform = new FakeReminderScheduler();
        WithShell(platform, (view, shell) =>
        {
            WriteNote(shell, "회의", "-[] 자료 공유 (10/3 14:00)");
            PumpUntil(() => platform.Pending.Count == 1, "the to-do was never scheduled");

            Assert.IsTrue(shell.HasReminders);
            Assert.IsTrue(shell.RemindersEnabled, "Reminders are not on by default.");
            Assert.IsTrue(shell.ShowReminderTimeRow);
            Assert.AreEqual("09:00", shell.ReminderTimeText);
            Assert.AreEqual(AppStrings.ReminderSettingsHint, shell.ReminderStatusText);

            shell.RemindersEnabled = false;
            PumpUntil(() => platform.Pending.Count == 0, "switching off left reminders scheduled");
            Assert.IsFalse(shell.ShowReminderTimeRow, "The time row stays up with reminders off.");
            ISettingsStore settings = TestServices.CurrentProvider!.GetRequiredService<ISettingsStore>();
            Assert.AreEqual("off", Wait(settings.GetAsync(ReminderCoordinator.EnabledKey).AsTask()));

            shell.RemindersEnabled = true;
            PumpUntil(() => platform.Pending.Count == 1, "switching back on scheduled nothing");
        });
    }

    [TestMethod]
    public void A_denied_permission_shows_on_the_row_and_offers_the_system_settings()
    {
        var platform = new FakeReminderScheduler
        {
            Permission = ReminderPermission.NotDetermined,
            AnswerToRequest = ReminderPermission.Denied,
        };
        WithShell(platform, (_, shell) =>
        {
            Assert.AreEqual(0, platform.Requests, "Asked at launch with nothing to remind about.");
            WriteNote(shell, "회의", "-[] 자료 공유 (10/3 14:00)");
            PumpUntil(() => shell.IsReminderPermissionDenied, "the denial never reached the row");

            Assert.AreEqual(1, platform.Requests);
            Assert.AreEqual(AppStrings.ReminderSettingsDenied, shell.ReminderStatusText);
            shell.OpenNotificationSettingsCommand.Execute(null);
            Assert.AreEqual(1, platform.SettingsOpened);
        });
    }

    [TestMethod]
    public void The_time_sheet_keeps_its_draft_until_done_and_then_reschedules()
    {
        var platform = new FakeReminderScheduler();
        WithShell(platform, (_, shell) =>
        {
            WriteNote(shell, "장보기", "-[] 우유 (10/4)");
            PumpUntil(() => platform.Pending.Count == 1, "the to-do was never scheduled");

            shell.OpenReminderTimeSheetCommand.Execute(null);
            Assert.IsTrue(shell.IsReminderTimeSheetOpen);
            Assert.IsFalse(shell.ShowDock, "The tab bar shows over the sheet.");
            Assert.IsTrue(shell.ReminderHourOptions.Single(o => o.Value == 9).IsCurrent);

            shell.PickReminderHourCommand.Execute(7);
            shell.PickReminderMinuteCommand.Execute(30);
            Assert.AreEqual("07:30", shell.DraftReminderTimeText);
            Assert.AreEqual("09:00", shell.ReminderTimeText, "The draft applied before 완료.");

            // Backing out keeps the old time.
            Assert.IsTrue(Run(shell.GoBackAsync()));
            Assert.IsFalse(shell.IsReminderTimeSheetOpen);
            Assert.AreEqual("09:00", shell.ReminderTimeText);

            shell.OpenReminderTimeSheetCommand.Execute(null);
            shell.PickReminderHourCommand.Execute(7);
            shell.PickReminderMinuteCommand.Execute(30);
            ScreenshotTests.Pump(() => shell.CommitReminderTimeCommand.ExecuteAsync(null));

            Assert.AreEqual("07:30", shell.ReminderTimeText);
            PumpUntil(() => platform.Pending.Values.Single().At == new DateTime(2026, 10, 4, 7, 30, 0), "the reminder kept its old time");
        });
    }

    [TestMethod]
    public void Tapping_a_reminder_opens_its_note_on_the_day_it_is_due()
    {
        var platform = new FakeReminderScheduler();
        WithShell(platform, (_, shell) =>
        {
            LocalDate yesterday = LocalDates.AddDays(Today, -1);
            LocalDate due = LocalDates.AddDays(Today, 1);
            ScreenshotTests.Pump(() => shell.SelectDateAsync(yesterday));
            WriteNote(shell, "어제 쓴 노트", "-[] 내일 할 일 (10/3 9:00)");
            PumpUntil(() => platform.Pending.Count == 1, "the to-do was never scheduled");
            Reminder reminder = platform.Pending.Values.Single();

            // The day it is due, not the day it was written down. A `-[ ]` line had no day of its
            // own, so the reminder borrowed its note's; a to-do has one, and being taken to the
            // day something is owed is what a reminder is for.
            Assert.AreEqual(due, reminder.Date, "A tap would not open the day the to-do is due.");

            ScreenshotTests.Pump(() => shell.SelectDateAsync(Today));
            shell.GoToPageCommand.Execute(MobilePage.Settings);

            ScreenshotTests.Pump(() => shell.OpenReminderAsync(reminder.Date, reminder.NoteId));

            Assert.AreEqual(due, shell.SelectedDate);
            Assert.IsTrue(shell.IsDayPage);

            // The note stays shut, and that is the point. It was written yesterday and the to-do
            // is owed tomorrow; opening it would mean walking straight back off the day the tap
            // just arrived at. The to-do is what the reminder is about, the day panel is showing
            // it, and the note it came from is one tap further on if anybody wants it.
            Assert.IsFalse(shell.IsEditorOpen);
        });
    }

    [TestMethod]
    public void The_precise_row_shows_only_while_exact_alarms_are_not_allowed()
    {
        var platform = new FakeReminderScheduler { ExactAlarms = ExactAlarmState.NotAllowed };
        WithShell(platform, (_, shell) =>
        {
            Assert.IsTrue(shell.ShowPreciseRemindersRow, "The row is missing while reminders can be an hour late.");
            Assert.AreEqual("정확한 시각에 알림", WithLanguageResult(AppLanguage.Korean, () => shell.Strings["ReminderPreciseTitle"]));

            shell.OpenExactAlarmSettingsCommand.Execute(null);
            Assert.AreEqual(1, platform.ExactSettingsOpened);
            Assert.AreEqual(0, platform.Requests, "The row asked for something by itself.");

            shell.RemindersEnabled = false;
            Assert.IsFalse(shell.ShowPreciseRemindersRow, "The row stays up with reminders off.");
            shell.RemindersEnabled = true;
            Assert.IsTrue(shell.ShowPreciseRemindersRow);

            // Allowed on the system page; coming back to the app is what the row notices.
            platform.ExactAlarms = ExactAlarmState.Allowed;
            var changes = new List<string>();
            shell.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);
            shell.NotifyResumed();
            Assert.Contains(nameof(MobileShellViewModel.ShowPreciseRemindersRow), changes);
            Assert.IsFalse(shell.ShowPreciseRemindersRow, "The row stays after exact alarms were allowed.");
        });
    }

    [TestMethod]
    public void The_precise_row_never_shows_where_reminders_are_always_exact()
    {
        var platform = new FakeReminderScheduler { ExactAlarms = ExactAlarmState.NotApplicable };
        WithShell(platform, (_, shell) => Assert.IsFalse(shell.ShowPreciseRemindersRow, "iOS shows the Android-only row."));
    }

    [TestMethod]
    public void Allowing_exact_alarms_arms_every_reminder_again()
    {
        using var harness = new Harness();
        harness.Platform.ExactAlarms = ExactAlarmState.NotAllowed;
        harness.Notes.Add(Note("n", "-[] a (10/3 14:00)\n-[] b (10/4)"));
        harness.Run();
        Assert.HasCount(2, harness.Platform.Applied.Single().Schedule);
        harness.Run();
        int before = harness.Platform.Applied.Count;

        harness.Platform.ExactAlarms = ExactAlarmState.Allowed;
        harness.Run();

        Assert.AreEqual(before + 1, harness.Platform.Applied.Count, "Nothing was re-armed after exact alarms were allowed.");
        Assert.HasCount(2, harness.Platform.Applied[^1].Schedule);

        harness.Run();
        Assert.AreEqual(before + 1, harness.Platform.Applied.Count, "Re-armed again with nothing changed.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<Reminder> Plan(params NoteSummary[] notes) =>
        ReminderPlanner.Plan(notes, Now, 64, ReminderPlanner.DefaultDateOnlyTime);

    private static NoteSummary Note(string title, string body, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Today, title, body, 0, false);

    private static bool Run(Task<bool> task)
    {
        bool result = false;
        ScreenshotTests.Pump(async () => result = await task.ConfigureAwait(true));
        return result;
    }

    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static void Wait(Task task) => task.GetAwaiter().GetResult();

    private static string WithLanguageResult(AppLanguage language, Func<string> read)
    {
        string result = string.Empty;
        WithLanguage(language, () => result = read());
        return result;
    }

    private static void WithLanguage(AppLanguage language, Action body)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            LocalizationService.Instance.SetLanguage(language);
            body();
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    /// <summary>The phone shell at the fixed "now", with <paramref name="platform"/> as its notifications.</summary>
    private static void WithShell(FakeReminderScheduler platform, Action<Views.MainView, MobileShellViewModel> body) =>
        TestServices.WithInitialisedShell(390, 844, new TestServices.ShellSetup(
            Platform: p => p with { Reminders = platform },
            Services: services => services.AddSingleton<IClock>(new FixedClock(Now))), body);

    /// <summary>A new note on the selected day, written and closed through the editor.</summary>
    private static void WriteNote(MobileShellViewModel shell, string title, string body)
    {
        ScreenshotTests.Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
        shell.Notes.EditorText = body;
        ScreenshotTests.Pump(() => shell.Notes.FlushAsync(FlushReason.NoteChange));
        ScreenshotTests.Pump(() => shell.Notes.RenameAsync(shell.Notes.SelectedTab!, title));
        ScreenshotTests.Pump(() => shell.CloseEditorAsync());

        // The body stays written as checkboxes — that is what makes these fixtures readable —
        // but the panels and the reminders read entities now, so the §8 migration's own walk
        // runs over what was just written. The migration itself cannot: it runs when the
        // database is opened, and this note did not exist then.
        ScreenshotTests.CaptureSeededTodos();
        ScreenshotTests.Pump(shell.RefreshAllAsync);
    }

    private static void PumpUntil(Func<bool> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, failure);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    /// <summary>A coordinator over a list of notes, an in-memory settings store and a fake platform.</summary>
    private sealed class Harness : IDisposable
    {
        internal Harness()
            : this(new TempDataRoot(), new FakeReminderScheduler(), new MemorySettings(), [])
        {
        }

        private Harness(TempDataRoot root, FakeReminderScheduler platform, MemorySettings settings, List<NoteSummary> notes)
        {
            Root = root;
            Platform = platform;
            Settings = settings;
            Notes = notes;
            Coordinator = new ReminderCoordinator(
                platform, AgendaOverNotes.Over(notes), settings, new FixedClock(Now), ReminderStateStore.InFolder(root.Path));
        }

        internal TempDataRoot Root { get; }

        internal FakeReminderScheduler Platform { get; }

        internal MemorySettings Settings { get; }

        internal List<NoteSummary> Notes { get; }

        internal ReminderCoordinator Coordinator { get; }

        /// <summary>One pass, reading the notes from the repository as the resume path does.</summary>
        internal void Run() => Wait(Coordinator.RefreshAsync());

        /// <summary>A new coordinator on the same device: the same state file, settings and platform.</summary>
        internal Harness Restart() => new(Root, Platform, Settings, Notes);

        public void Dispose() => Root.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public ClockSnapshot Read() => new(now.ToUniversalTime(), now.Offset);
    }

    internal sealed class MemorySettings : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.TryGetValue(key, out string? value) ? value : null);

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.TryGetValue(key, out string? value) ? bool.Parse(value) : fallback);

        public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default) =>
            SetAsync(key, value.ToString(), cancellationToken);
    }

    /// <summary>
    /// The to-do store the coordinator reads, built from the same note bodies these tests have
    /// always been written in.
    /// </summary>
    /// <remarks>
    /// The fixtures stay as <c>-[] 회의자료 (10/3 14:00)</c> because that is what they are about —
    /// which to-dos a device should be reminded of — and rewriting a dozen of them as entity
    /// literals would make them harder to read without testing anything new. The conversion is
    /// the one the §8 migration performs, so these tests now also exercise the shape that
    /// migration will produce.
    /// <para>
    /// A proxy rather than a hand-written stub, because the interface is wide and the coordinator
    /// touches two methods of it.
    /// </para>
    /// </remarks>
    internal class AgendaOverNotes : DispatchProxy
    {
        private List<NoteSummary> _notes = [];

        internal static IAgendaRepository Over(List<NoteSummary> notes)
        {
            IAgendaRepository proxy = Create<IAgendaRepository, AgendaOverNotes>();
            ((AgendaOverNotes)(object)proxy)._notes = notes;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IAgendaRepository.GetAllAsync) =>
                new ValueTask<IReadOnlyList<AgendaItem>>(Items()),
            nameof(IAgendaRepository.GetListsAsync) =>
                new ValueTask<IReadOnlyList<AgendaList>>(Array.Empty<AgendaList>()),
            _ => throw new NotSupportedException(targetMethod?.Name),
        };

        private IReadOnlyList<AgendaItem> Items()
        {
            var items = new List<AgendaItem>();
            foreach (NoteSummary note in _notes)
            {
                foreach (ScannedTodo todo in TodoBodyScan.Scan(note.Id, note.LocalDate, note.Body))
                {
                    items.Add(new AgendaItem(
                        todo.Id,
                        AgendaList.DefaultId,
                        AgendaKind.Task,
                        todo.Text,
                        string.Empty,
                        "Asia/Seoul",
                        StartsAt: null,
                        EndsAt: null,
                        DueAt: todo.DueAt,
                        HasDueTime: todo.HasDueTime,
                        Rrule: null,
                        SeriesId: null,
                        RecurrenceId: null,
                        todo.Completed ? AgendaStatus.Completed : AgendaStatus.NeedsAction,
                        CompletedUtc: null,
                        Priority: 0,
                        TimelineVisibility.Auto,
                        SourceNoteId: note.Id,
                        ExceptionDates: [],
                        // As the migration does it: a line with a stamp reminded, one without
                        // never did.
                        todo.DueAt is null ? AgendaAlert.None : AgendaAlert.Default,
                        DateTimeOffset.UnixEpoch,
                        DateTimeOffset.UnixEpoch));
                }
            }

            return items;
        }
    }
}

/// <summary>The platform side of reminders, in memory: what is pending, and every change it was told.</summary>
internal sealed class FakeReminderScheduler : IReminderScheduler
{
    public int Capacity { get; set; } = 64;

    public ReminderPermission Permission { get; set; } = ReminderPermission.Granted;

    /// <summary>What the "system prompt" answers.</summary>
    public ReminderPermission AnswerToRequest { get; set; } = ReminderPermission.Granted;

    public int Requests { get; private set; }

    public int SettingsOpened { get; private set; }

    public Dictionary<string, Reminder> Pending { get; } = new(StringComparer.Ordinal);

    public List<ReminderChanges> Applied { get; } = [];

    public Task<ReminderPermission> GetPermissionAsync() => Task.FromResult(Permission);

    public Task<ReminderPermission> RequestPermissionAsync()
    {
        Requests++;
        Permission = AnswerToRequest;
        return Task.FromResult(Permission);
    }

    public Task ApplyAsync(ReminderChanges changes)
    {
        foreach (string id in changes.Cancel)
        {
            Pending.Remove(id);
        }

        foreach (Reminder reminder in changes.Schedule)
        {
            Pending[reminder.Id] = reminder;
        }

        Applied.Add(changes);
        return Task.CompletedTask;
    }

    public void OpenSystemSettings() => SettingsOpened++;

    /// <summary>Exact unless a test says otherwise, as on iOS.</summary>
    public ExactAlarmState ExactAlarms { get; set; } = ExactAlarmState.NotApplicable;

    public int ExactSettingsOpened { get; private set; }

    public void OpenExactAlarmSettings() => ExactSettingsOpened++;
}
