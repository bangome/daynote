using Avalonia;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Time;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The menu bar popover's rules (menu bar design §01, Motion M9): the count beside the item, the
/// order of the list, red past due, the box with and without <c>@</c>, and that it stays open.
/// </summary>
/// <remarks>
/// Over the app's real agenda store in a throwaway database, at the design's moment: Wednesday
/// 7 October 2026, 14:30 in Seoul. The fixture is the design's own list, so a failure here reads
/// the same as a difference from the drawing.
/// </remarks>
[TestClass]
public sealed class MenuBarTests
{
    internal static readonly DateTimeOffset DesignNow = new(2026, 10, 7, 14, 30, 0, TimeSpan.FromHours(9));

    private static readonly DateOnly Today = DateOnly.FromDateTime(DesignNow.DateTime);

    [TestMethod]
    public void The_count_is_todays_open_to_dos_and_nothing_else()
    {
        WithMenuBar((model, _) =>
        {
            // Four to-dos owed today; the done one, tomorrow's and the event do not count.
            Assert.AreEqual(4, model.RemainingCount);
            Assert.AreEqual("할 일 · 남은 4", model.TodosHeader);
        });
    }

    [TestMethod]
    public void Timed_to_dos_come_first_in_time_order_then_the_rest_by_title()
    {
        WithMenuBar((model, _) =>
        {
            CollectionAssert.AreEqual(
                new[] { "회의실 예약 확인", "퇴근 전 로그 확인", "릴리즈 노트 작성", "비타민 먹기" },
                model.Todos.Select(static row => row.Title).ToArray());
            Assert.AreEqual("14:00", model.Todos[0].TimeText);
            Assert.AreEqual("18:00", model.Todos[1].TimeText);
            Assert.IsFalse(model.Todos[2].HasTime);
            Assert.IsTrue(model.Todos[3].IsRepeat, "The daily rule's occurrence is not marked as a repeat.");
        });
    }

    [TestMethod]
    public void A_time_already_past_is_overdue_and_one_still_ahead_is_not()
    {
        WithMenuBar((model, _) =>
        {
            Assert.IsTrue(model.Todos[0].ShowsOverdue, "14:00 at 14:30 is not shown as overdue.");
            Assert.IsFalse(model.Todos[1].ShowsOverdue, "18:00 at 14:30 is shown as overdue.");
            Assert.IsFalse(model.Todos[2].ShowsOverdue, "A to-do with no time cannot be late.");
        });
    }

    [TestMethod]
    public void Rings_take_their_lists_colour()
    {
        WithMenuBar((model, _) =>
        {
            Assert.IsTrue(model.Todos[0].IsToneSun, "The built-in list is not the sun.");
            Assert.IsTrue(model.Todos[1].IsToneIndigo, "The first other list is not indigo.");
            Assert.IsTrue(model.Todos[3].IsToneGreen, "The second other list is not green.");
        });
    }

    [TestMethod]
    public void The_next_event_says_when_and_how_long_until()
    {
        WithMenuBar((model, _) =>
        {
            Assert.IsTrue(model.HasNextEvent);
            Assert.AreEqual("디자인 리뷰", model.NextEventTitle);
            Assert.AreEqual("16:00–17:00 · 1시간 30분 후", model.NextEventDetail);
            Assert.AreEqual("오늘 · 10월 7일 수요일", model.HeaderText);
        });
    }

    [TestMethod]
    public void With_an_at_Enter_makes_the_to_do_and_it_arrives_on_top_as_just_now()
    {
        WithMenuBar((model, agenda) =>
        {
            int closes = 0;
            model.CloseRequested += (_, _) => closes++;

            Type(model, "회의자료 초안 공유 @오늘 5시");
            Assert.IsTrue(model.IsReadbackVisible, "The @ phrase was not read back.");
            Assert.IsFalse(model.IsAppendHintVisible, "The note hint shows while an @ is being read.");
            Assert.AreEqual("10월 7일 (수) 오후 5:00 마감", model.Capture.TaskLine);

            Assert.IsTrue(Wait(model.SubmitAsync()));

            AgendaItem made = Wait(agenda.GetAllAsync()).Single(item => item.Title == "회의자료 초안 공유");
            Assert.AreEqual(AgendaKind.Task, made.Kind);
            Assert.AreEqual(new DateTime(2026, 10, 7, 17, 0, 0), made.DueAt?.Value);
            Assert.IsNull(made.SourceNoteId, "Typed in the menu bar, yet it claims a note it came from.");

            Assert.AreEqual("회의자료 초안 공유", model.Todos[0].Title);
            Assert.IsTrue(model.Todos[0].IsJustAdded, "The new row is not marked 방금.");
            Assert.AreEqual(5, model.RemainingCount);
            Assert.AreEqual(string.Empty, model.Draft, "The box was not cleared for the next one.");

            // M9: making does not close.
            Assert.IsTrue(model.IsOpen);
            Assert.AreEqual(0, closes);
        });
    }

    [TestMethod]
    public void Tab_switches_to_an_event_and_Enter_makes_that()
    {
        WithMenuBar((model, agenda) =>
        {
            Type(model, "팀 점심 @오늘 3시");
            Assert.IsTrue(model.ToggleKind(), "Tab was not taken while a reading was up.");
            Assert.IsTrue(model.Capture.IsEventSelected);

            Assert.IsTrue(Wait(model.SubmitAsync()));
            AgendaItem made = Wait(agenda.GetAllAsync()).Single(item => item.Title == "팀 점심");
            Assert.AreEqual(AgendaKind.Event, made.Kind);
            Assert.AreEqual("팀 점심", model.NextEventTitle, "An event at 15:00 is not the next one before 16:00's.");
        });
    }

    [TestMethod]
    public void Something_for_another_day_says_where_it_went_and_offers_to_go_there()
    {
        WithMenuBar((model, _) =>
        {
            Type(model, "보고서 제출 @내일");
            Assert.IsTrue(Wait(model.SubmitAsync()));

            Assert.AreEqual("10/8에 추가됨", model.NoticeText);
            Assert.AreEqual(LocalDates.FromDateOnly(Today.AddDays(1)), model.NoticeDate);
            Assert.IsTrue(model.HasNoticeAction);
            Assert.IsFalse(model.Todos.Any(static row => row.Title == "보고서 제출"), "Tomorrow's to-do is in today's list.");
        });
    }

    [TestMethod]
    public void Without_an_at_Enter_hands_the_line_to_todays_note()
    {
        var lines = new List<(string Line, bool NewNote)>();
        WithMenuBar(
            (model, _) =>
            {
                Type(model, "  아이디어: 온보딩 3단계로  ");
                Assert.IsTrue(model.IsAppendHintVisible);
                Assert.IsFalse(model.IsReadbackVisible);

                Assert.IsTrue(Wait(model.SubmitAsync()));
                Assert.AreEqual("오늘 노트에 추가됨", model.NoticeText);

                Type(model, "새 노트로");
                Assert.IsTrue(Wait(model.SubmitAsync(newNote: true)));
                Assert.AreEqual("오늘 새 노트에 추가됨", model.NoticeText);
                Assert.IsTrue(model.IsOpen);
            },
            append: (line, newNote) =>
            {
                lines.Add((line, newNote));
                return Task.FromResult(newNote ? MenuBarAppendResult.NewNote : MenuBarAppendResult.Appended);
            });

        CollectionAssert.AreEqual(
            new[] { ("아이디어: 온보딩 3단계로", false), ("새 노트로", true) },
            lines.ToArray());
    }

    [TestMethod]
    public void A_line_that_could_not_be_written_stays_in_the_box()
    {
        WithMenuBar(
            (model, _) =>
            {
                Type(model, "남겨 둘 문장");
                Assert.IsFalse(Wait(model.SubmitAsync()));
                Assert.AreEqual("남겨 둘 문장", model.Draft);
                Assert.AreEqual("노트에 추가하지 못했어요. 다시 시도해 주세요.", model.NoticeText);
            },
            append: (_, _) => Task.FromResult(MenuBarAppendResult.Failed));
    }

    [TestMethod]
    public void An_at_with_nothing_read_yet_makes_nothing()
    {
        WithMenuBar((model, agenda) =>
        {
            int before = Wait(agenda.GetAllAsync()).Count;
            Type(model, "제목만 @");
            Assert.IsTrue(model.IsPromptVisible);
            Assert.IsFalse(Wait(model.SubmitAsync()));
            Assert.AreEqual(before, Wait(agenda.GetAllAsync()).Count);
        });
    }

    [TestMethod]
    public void Esc_dismisses_the_readback_first_and_only_then_asks_to_close()
    {
        WithMenuBar((model, _) =>
        {
            int closes = 0;
            model.CloseRequested += (_, _) => closes++;

            Type(model, "회의 @내일 3시");
            model.Cancel();
            Assert.IsFalse(model.IsReadbackVisible);
            Assert.AreEqual("회의 @내일 3시", model.Draft, "Esc changed what was typed.");
            Assert.AreEqual(0, closes);

            model.Cancel();
            Assert.AreEqual(1, closes);
        });
    }

    [TestMethod]
    public void A_ticked_row_stays_while_open_and_is_gone_next_time()
    {
        WithMenuBar((model, agenda) =>
        {
            MenuBarTodoRowViewModel first = model.Todos[0];
            Wait(first.ToggleCommand.ExecuteAsync(null));

            Assert.IsTrue(first.IsDone);
            Assert.IsFalse(first.ShowsOverdue, "A ticked row is still red.");
            Assert.AreEqual(3, model.RemainingCount);
            Assert.AreSame(first, model.Todos[0], "The ticked row moved while the popover was open.");
            Assert.AreEqual(
                AgendaStatus.Completed,
                Wait(agenda.GetAllAsync()).Single(item => item.Title == "회의실 예약 확인").Status);

            model.Close();
            Wait(model.OpenAsync());
            Assert.IsFalse(model.Todos.Any(static row => row.Title == "회의실 예약 확인"), "The ticked row came back.");
        });
    }

    [TestMethod]
    public void Ticking_a_repeat_completes_only_todays_occurrence()
    {
        WithMenuBar((model, agenda) =>
        {
            MenuBarTodoRowViewModel vitamins = model.Todos.Single(static row => row.Title == "비타민 먹기");
            Wait(vitamins.ToggleCommand.ExecuteAsync(null));

            IReadOnlyList<AgendaItem> all = Wait(agenda.GetAllAsync());
            Assert.AreEqual(1, all.Count(static item => item.Title == "비타민 먹기" && item.IsOverride));
            Assert.IsTrue(all.Single(static item => item.Title == "비타민 먹기" && item.IsSeries).Status == AgendaStatus.NeedsAction);

            // Unticking goes through the override the tick made, not the rule.
            Wait(vitamins.ToggleCommand.ExecuteAsync(null));
            Assert.IsFalse(vitamins.IsDone);
            Assert.AreEqual(1, Wait(agenda.GetAllAsync()).Count(static item => item.Title == "비타민 먹기" && item.IsOverride));
        });
    }

    [TestMethod]
    public void The_header_reads_in_English_too()
    {
        WithMenuBar(
            (model, _) =>
            {
                Assert.AreEqual("Today · Wed, Oct 7", model.HeaderText);
                Assert.AreEqual("To-dos · 4 left", model.TodosHeader);
                Assert.AreEqual("16:00–17:00 · in 1 h 30 min", model.NextEventDetail);
            },
            language: AppLanguage.English);
    }

    [TestMethod]
    public void Durations_round_up_and_drop_empty_parts()
    {
        var localization = LocalizationService.Instance;
        AppLanguage original = localization.Language;
        try
        {
            localization.SetLanguage(AppLanguage.Korean);
            Assert.AreEqual("1분", MenuBarViewModel.Duration(TimeSpan.FromSeconds(10)));
            Assert.AreEqual("45분", MenuBarViewModel.Duration(TimeSpan.FromMinutes(45)));
            Assert.AreEqual("2시간", MenuBarViewModel.Duration(TimeSpan.FromHours(2)));
            Assert.AreEqual("1시간 30분", MenuBarViewModel.Duration(TimeSpan.FromMinutes(90)));
        }
        finally
        {
            localization.SetLanguage(original);
        }
    }

    [TestMethod]
    public void The_chord_is_written_the_way_the_platform_writes_it()
    {
        Assert.IsTrue(Hotkey.TryParse(Daynote.Core.Settings.ShortcutSettings.CaptureHotkeyDefaultMac, out Hotkey mac));
        Assert.IsTrue(Hotkey.TryParse(Daynote.Core.Settings.ShortcutSettings.CaptureHotkeyDefaultWindows, out Hotkey windows));
        Assert.AreEqual(HotkeyModifiers.Alt | HotkeyModifiers.Meta, mac.Modifiers);
        Assert.AreEqual(HotkeyKey.Space, mac.Key);

        if (OperatingSystem.IsMacOS())
        {
            Assert.AreEqual("⌥⌘Space", MenuBarViewModel.FormatChord(mac));
        }
        else
        {
            Assert.AreEqual("Ctrl+Alt+Space", MenuBarViewModel.FormatChord(windows));
        }

        Assert.AreEqual(string.Empty, MenuBarViewModel.FormatChord(null));
    }

    [TestMethod]
    public void A_line_lands_at_the_end_of_todays_last_note_or_in_a_new_one()
    {
        WithServices(provider =>
        {
            var shell = provider.GetRequiredService<DesktopShellViewModel>();
            Wait(shell.InitializeAsync());
            var append = new AppendNoteLine(
                provider.GetRequiredService<INoteRepository>(),
                provider.GetRequiredService<Func<Daynote.Core.Domain.Notes.NoteId>>());

            // An empty day gets a note made for it.
            Assert.AreEqual(MenuBarAppendResult.Appended, Wait(shell.AppendLineToTodayAsync(append, "첫 줄", newNote: false)));
            Assert.AreEqual("첫 줄", shell.Notes.EditorText, "The editor did not pick up the line it is showing.");

            // The editor's unsaved typing is saved first, then the line goes under it.
            shell.Notes.EditorText = "첫 줄\n편집 중";
            Assert.AreEqual(MenuBarAppendResult.Appended, Wait(shell.AppendLineToTodayAsync(append, "둘째 줄", newNote: false)));
            Assert.AreEqual("첫 줄\n편집 중\n둘째 줄", shell.Notes.EditorText);

            Assert.AreEqual(MenuBarAppendResult.NewNote, Wait(shell.AppendLineToTodayAsync(append, "새 노트", newNote: true)));
            DayWorkspace day = Wait(provider.GetRequiredService<INoteRepository>()
                .GetDayWorkspaceStateAsync(shell.SelectedDate).AsTask());
            CollectionAssert.AreEqual(
                new[] { "첫 줄\n편집 중\n둘째 줄", "새 노트" },
                day.Notes.Notes.Select(static note => note.Body).ToArray());

            // And the next plain line goes on the last note, which is now the new one.
            Wait(shell.AppendLineToTodayAsync(append, "끝", newNote: false));
            day = Wait(provider.GetRequiredService<INoteRepository>().GetDayWorkspaceStateAsync(shell.SelectedDate).AsTask());
            Assert.AreEqual("새 노트\n끝", day.Notes.Notes[^1].Body);
        });
    }

    [TestMethod]
    public void A_second_Enter_while_the_first_is_writing_makes_nothing_more()
    {
        var gate = new GatedAgenda();
        int appended = 0;
        WithMenuBar(
            (model, agenda) =>
            {
                Type(model, "회의 @내일 3시");
                gate.Hold();
                Task<bool> first = model.SubmitAsync();
                Task<bool> second = model.SubmitAsync();
                Task<bool> third = model.SubmitAsync(newNote: true);

                Assert.IsTrue(second.IsCompleted && !second.Result, "A second Enter was let through mid-write.");
                Assert.IsTrue(third.IsCompleted && !third.Result, "A third Enter was let through mid-write.");

                gate.Release();
                Assert.IsTrue(Wait(first));
                Assert.AreEqual(1, Wait(agenda.GetAllAsync()).Count(static item => item.Title == "회의"));
            },
            append: (_, _) =>
            {
                appended++;
                return Task.FromResult(MenuBarAppendResult.Appended);
            },
            wrap: inner => gate.Over(inner));

        Assert.AreEqual(0, appended, "The @ phrase went into today's note as text.");
    }

    [TestMethod]
    public void A_plain_line_is_added_once_however_often_Enter_repeats()
    {
        var release = new TaskCompletionSource<MenuBarAppendResult>();
        int appended = 0;
        WithMenuBar(
            (model, _) =>
            {
                Type(model, "한 번만");
                Task<bool> first = model.SubmitAsync();
                Assert.IsFalse(Wait(model.SubmitAsync()));
                release.SetResult(MenuBarAppendResult.Appended);
                Assert.IsTrue(Wait(first));
            },
            append: (_, _) =>
            {
                appended++;
                return release.Task;
            });

        Assert.AreEqual(1, appended);
    }

    [TestMethod]
    public void An_item_the_store_refuses_keeps_its_text_and_says_so()
    {
        var gate = new GatedAgenda { FailSaves = true };
        WithMenuBar(
            (model, _) =>
            {
                Type(model, "회의 @내일 3시");
                Assert.IsFalse(Wait(model.SubmitAsync()));
                Assert.AreEqual("회의 @내일 3시", model.Draft);
                Assert.AreEqual("저장하지 못했어요. 다시 시도해 주세요.", model.NoticeText);
                Assert.IsTrue(model.IsReadbackVisible, "The readback did not come back for a retry.");
            },
            wrap: inner => gate.Over(inner));
    }

    [TestMethod]
    public void A_line_the_host_throws_on_is_reported_not_lost()
    {
        WithMenuBar(
            (model, _) =>
            {
                Type(model, "던져질 문장");
                Assert.IsFalse(Wait(model.SubmitAsync()));
                Assert.AreEqual("던져질 문장", model.Draft);
                Assert.AreEqual("노트에 추가하지 못했어요. 다시 시도해 주세요.", model.NoticeText);
            },
            append: (_, _) => throw new IOException("disk gone"));
    }

    [TestMethod]
    public void A_tick_the_store_refuses_is_taken_back()
    {
        var gate = new GatedAgenda();
        WithMenuBar(
            (model, _) =>
            {
                gate.FailSaves = true;
                MenuBarTodoRowViewModel first = model.Todos[0];
                Wait(first.ToggleCommand.ExecuteAsync(null));

                Assert.IsFalse(first.IsDone, "The ring stayed filled after the write failed.");
                Assert.IsFalse(first.IsHighlighted);
                Assert.AreEqual(4, model.RemainingCount);
                Assert.AreEqual("저장하지 못했어요. 다시 시도해 주세요.", model.NoticeText);
            },
            wrap: inner => gate.Over(inner));
    }

    [TestMethod]
    public void A_chord_someone_else_holds_at_start_up_is_reported_not_shown()
    {
        using var data = new TempDataRoot();
        HeadlessAppFixture.OnUiThread(() =>
        {
            Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", data.Path);
            var services = new ServiceCollection();
            services.AddDaynoteDesktop(
                Daynote.App.Composition.DaynoteAppOptions.ForCurrentUser(), Application.Current!, () => null, () => { });
            services.AddSingleton<IGlobalHotkeyService>(new RefusingHotkeys());
            ServiceProvider provider = services.BuildServiceProvider();
            var localization = LocalizationService.Instance;
            AppLanguage original = localization.Language;
            try
            {
                localization.SetLanguage(AppLanguage.Korean);
                DesktopSettingsViewModel settings = provider.GetRequiredService<DesktopShellViewModel>().SettingsViewModel!;
                Wait(settings.LoadMenuBarAsync());

                Assert.AreEqual("설정 안 됨", settings.CaptureHotkeyDisplay, "A chord that was never registered is shown as if it works.");
                Assert.AreEqual(AppStrings.HotkeyConflict, settings.CaptureHotkeyStatusText);
            }
            finally
            {
                localization.SetLanguage(original);
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    /// <summary>The design's list, written through the store the app reads.</summary>
    internal static void SeedDesignDay(IAgendaRepository agenda, bool english = false)
    {
        string T(string korean, string inEnglish) => english ? inEnglish : korean;

        Guid work = Guid.NewGuid();
        Guid health = Guid.NewGuid();
        Wait(agenda.CreateListAsync(work, T("업무", "Work")).AsTask());
        Wait(agenda.CreateListAsync(health, T("건강", "Health")).AsTask());

        Save(agenda, ToDo(T("회의실 예약 확인", "Confirm room booking"), AgendaList.DefaultId, Today, new TimeOnly(14, 0)));
        Save(agenda, ToDo(T("퇴근 전 로그 확인", "Check logs before leaving"), work, Today, new TimeOnly(18, 0)));
        Save(agenda, ToDo(T("릴리즈 노트 작성", "Write release notes"), AgendaList.DefaultId, Today, null));
        Save(agenda, ToDo(T("비타민 먹기", "Take vitamins"), health, Today, null) with
        {
            Rrule = "FREQ=DAILY",
            StartsAt = new WallClock(Today.AddDays(-3).ToDateTime(TimeOnly.MinValue)),
            DueAt = null,
        });
        Save(agenda, ToDo(T("이미 한 일", "Already done"), AgendaList.DefaultId, Today, new TimeOnly(9, 0)) with
        {
            Status = AgendaStatus.Completed,
            CompletedUtc = DesignNow,
        });
        Save(agenda, ToDo(T("내일 할 일", "Tomorrow's task"), AgendaList.DefaultId, Today.AddDays(1), new TimeOnly(10, 0)));
        Save(agenda, ToDo(T("디자인 리뷰", "Design review"), AgendaList.DefaultId, Today, null) with
        {
            Kind = AgendaKind.Event,
            StartsAt = new WallClock(Today.ToDateTime(new TimeOnly(16, 0))),
            EndsAt = new WallClock(Today.ToDateTime(new TimeOnly(17, 0))),
            DueAt = null,
            AlarmLeadMinutes = AgendaAlert.None,
        });
    }

    internal static AgendaItem ToDo(string title, Guid list, DateOnly day, TimeOnly? time) => new(
        Guid.NewGuid(),
        list,
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
        AgendaAlert.Default,
        DesignNow,
        DesignNow);

    /// <summary>
    /// A seeded store and an opened popover model, at the design's moment, in Korean unless asked.
    /// </summary>
    internal static void WithMenuBar(
        Action<MenuBarViewModel, IAgendaRepository> body,
        Func<string, bool, Task<MenuBarAppendResult>>? append = null,
        AppLanguage language = AppLanguage.Korean,
        Func<IAgendaRepository, IAgendaRepository>? wrap = null)
    {
        var localization = LocalizationService.Instance;
        AppLanguage original = localization.Language;
        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            WithServices(provider =>
            {
                // The app points the UI thread's culture at its language at start-up; a language
                // that is already set does not do it again, so the test does.
                localization.SetLanguage(language);
                System.Globalization.CultureInfo.CurrentCulture = localization.Culture;
                var agenda = provider.GetRequiredService<IAgendaRepository>();
                SeedDesignDay(agenda, language == AppLanguage.English);
                var model = new MenuBarViewModel(
                    wrap?.Invoke(agenda) ?? agenda,
                    new FixedClock(DesignNow),
                    append ?? ((_, _) => Task.FromResult(MenuBarAppendResult.Appended)),
                    _ => { },
                    () => { });
                Wait(model.OpenAsync());
                body(model, agenda);
            });
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            localization.SetLanguage(original);
        }
    }

    internal static void WithServices(Action<ServiceProvider> body)
    {
        using var data = new TempDataRoot();
        HeadlessAppFixture.OnUiThread(() =>
        {
            ServiceProvider provider = TestServices.Build(data.Path, Application.Current!);
            try
            {
                body(provider);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    /// <summary>Types into the box the way the view does: the text, then the caret at its end.</summary>
    internal static void Type(MenuBarViewModel model, string text)
    {
        model.Draft = text;
        model.UpdateCaret(text.Length);
    }

    private static void Save(IAgendaRepository agenda, AgendaItem item) => Wait(agenda.SaveAsync(item).AsTask());

    internal static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
        work.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    internal static T Wait<T>(Task<T> work)
    {
        Wait((Task)work);
        return work.Result;
    }

    internal static T Wait<T>(ValueTask<T> work) => Wait(work.AsTask());

    /// <summary>The real store, with saves that can be held back or refused.</summary>
    internal sealed class GatedAgenda
    {
        private TaskCompletionSource? held;

        public bool FailSaves { get; set; }

        public void Hold() => held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => held?.TrySetResult();

        public IAgendaRepository Over(IAgendaRepository inner) => new Repository(this, inner);

        private sealed class Repository(GatedAgenda gate, IAgendaRepository inner) : IAgendaRepository
        {
            public async ValueTask SaveAsync(AgendaItem item, CancellationToken cancellationToken = default)
            {
                if (gate.held is { } held)
                {
                    await held.Task.ConfigureAwait(true);
                }

                if (gate.FailSaves)
                {
                    throw new IOException("The store refused the write.");
                }

                await inner.SaveAsync(item, cancellationToken).ConfigureAwait(true);
            }

            public ValueTask<IReadOnlyList<AgendaList>> GetListsAsync(CancellationToken cancellationToken = default) => inner.GetListsAsync(cancellationToken);

            public ValueTask<AgendaList> CreateListAsync(Guid id, string name, CancellationToken cancellationToken = default) => inner.CreateListAsync(id, name, cancellationToken);

            public ValueTask<AgendaList?> RenameListAsync(Guid id, string name, CancellationToken cancellationToken = default) => inner.RenameListAsync(id, name, cancellationToken);

            public ValueTask<int?> DeleteListAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteListAsync(id, cancellationToken);

            public ValueTask<AgendaItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);

            public ValueTask<IReadOnlyList<AgendaItem>> GetForDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) => inner.GetForDateAsync(localDate, cancellationToken);

            public ValueTask<IReadOnlyList<AgendaItem>> GetSeriesAsync(CancellationToken cancellationToken = default) => inner.GetSeriesAsync(cancellationToken);

            public ValueTask<IReadOnlyList<AgendaItem>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);

            public ValueTask<IReadOnlyList<AgendaItem>> GetForListAsync(Guid listId, CancellationToken cancellationToken = default) => inner.GetForListAsync(listId, cancellationToken);

            public ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, cancellationToken);
        }
    }

    /// <summary>A machine where every chord is already taken.</summary>
    private sealed class RefusingHotkeys : IGlobalHotkeyService
    {
        public event EventHandler? Pressed
        {
            add { }
            remove { }
        }

        public event EventHandler? QuickNotePressed
        {
            add { }
            remove { }
        }

        public Hotkey? Current => null;

        public Hotkey? CurrentCapture => null;

        public void Attach(nint hwnd)
        {
        }

        public HotkeySetResult TrySet(Hotkey hotkey) => HotkeySetResult.Conflict;

        public HotkeySetResult TrySetCapture(Hotkey hotkey) => HotkeySetResult.Conflict;

        public void Dispose()
        {
        }
    }

    internal sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public ClockSnapshot Read() => new(now.ToUniversalTime(), now.Offset);
    }
}
