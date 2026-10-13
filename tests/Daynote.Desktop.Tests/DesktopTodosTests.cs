using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Time;
using Daynote.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The desktop's to-do surfaces as the v2 design draws them (Daynote Desktop B v2): the day panel's
/// 일정 and 할 일 sections, the quick-add lines, the creation card's 언제, the "M/D에 추가됨" notice,
/// the sidebar's lists, and the timeline carrying events and to-dos beside the notes.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DesktopTodosTests
{
    private const int Width = 1440;
    private const int Height = 900;

    [TestMethod]
    public void The_day_panel_puts_events_above_the_to_dos_and_hides_what_is_done()
    {
        WithSample((window, shell) =>
        {
            string[] events = [.. shell.DayEvents.Select(row => row.Text)];
            Assert.AreEqual("분기 마감", events[0], "The all-day event does not lead.");
            CollectionAssert.IsSubsetOf(new[] { "점심 약속", "디자인 리뷰" }, events);
            Assert.IsTrue(shell.DayEvents.All(row => row.IsEvent));
            Assert.IsTrue(shell.DayTodos.All(row => row.IsTask), "An event is in the to-do list.");
            CollectionAssert.IsSubsetOf(new[] { "회의실 예약 확인", "릴리즈 노트 작성", "우유 사기", "비타민 먹기" },
                shell.DayTodos.Select(row => row.Text).ToArray());
            Assert.AreEqual(2, shell.DayTodosDone.Count, "The finished to-dos are not under 완료.");
            Assert.AreEqual("완료 2", shell.DayDoneCountText);

            ItemsControl done = Named<ItemsControl>(window, "DayDoneList");
            Assert.IsFalse(done.IsVisible, "완료 opens by itself.");
            Click(window, "DayDoneToggle");
            Assert.IsTrue(done.IsVisible, "완료 does not open.");

            TodoItemViewModel milk = shell.DayTodos.Single(row => row.Text == "우유 사기");
            TodoItemViewModel review = shell.DayTodos.Single(row => row.Text == "릴리즈 노트 작성");
            Assert.AreNotEqual(milk.ListColor, review.ListColor, "Two lists draw their rings in one colour.");
            Assert.IsTrue(shell.DayTodos.Single(row => row.Text == "비타민 먹기").IsRepeat);
        });
    }

    [TestMethod]
    public void The_events_plus_opens_the_card_on_an_event()
    {
        WithSample((window, shell) =>
        {
            Click(window, "DayAddEvent");
            Assert.IsTrue(shell.IsAddTodoOpen);
            Assert.IsTrue(shell.TodoEntry.IsEvent, "일정 + opened a to-do.");
            Assert.AreEqual(LocalDates.ToDateOnly(shell.SelectedDate), shell.TodoEntry.Date);
        });
    }

    [TestMethod]
    public void A_quick_add_line_makes_a_to_do_on_the_day()
    {
        WithSample((window, shell) =>
        {
            LocalDate other = LocalDates.AddDays(shell.SelectedDate, 3);
            Wait(shell.SelectDateAsync(other));
            shell.DayQuickText = "택배 보내기";
            Wait(shell.QuickAddDayCommand.ExecuteAsync(null));
            PumpUntil(() => shell.DayTodos.Any(row => row.Text == "택배 보내기"), "The quick-add line did not add to the day.");

            AgendaItem made = Items().Single(item => item.Title == "택배 보내기");
            Assert.AreEqual(new WallClock(LocalDates.ToDateOnly(other).ToDateTime(TimeOnly.MinValue)), made.DueAt);
            Assert.AreEqual(AgendaList.DefaultId, made.ListId);
            Assert.IsNull(made.SourceNoteId);
            Assert.AreEqual(string.Empty, shell.DayQuickText, "The line kept its text.");
            Assert.IsFalse(shell.IsNoticeShown, "A to-do on the day being looked at was announced as going elsewhere.");
        });
    }

    [TestMethod]
    public void The_list_views_line_files_into_the_list_on_today()
    {
        WithSample((window, shell) =>
        {
            AgendaListRowViewModel shopping = shell.Todo.Lists.Single(list => list.Name == AgendaSeed.Shopping);
            Wait(shell.ShowAgendaListCommand.ExecuteAsync(shopping));
            Assert.IsTrue(shell.IsListMode && shell.TabIsTodo);
            Assert.AreEqual(AgendaSeed.Shopping, shell.ListTitle);
            Assert.IsFalse(shell.IsNavTodo, "할 일 is lit while a list narrows it.");
            Assert.IsTrue(shell.Todo.Lists.Single(list => list.Name == AgendaSeed.Shopping).IsCurrent, "The list's row is not lit.");

            shell.ListQuickText = "두부";
            Wait(shell.QuickAddListCommand.ExecuteAsync(null));
            AgendaItem made = Items().Single(item => item.Title == "두부");
            Assert.AreEqual(shopping.Id, made.ListId);
            Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now), DateOnly.FromDateTime(made.DueAt!.Value.Value));
            PumpUntil(() => shell.Todo.TodayItems.Any(row => row.Text == "두부"), "The list does not show it.");

            Wait(shell.ShowAllTodosCommand.ExecuteAsync(null));
            Assert.IsNull(shell.Todo.SelectedListId, "할 일 did not widen to every list.");
            Assert.IsTrue(shell.IsNavTodo);
        });
    }

    [TestMethod]
    public void When_reads_a_phrase_into_the_fields_and_says_it_back()
    {
        WithSample((window, shell) =>
        {
            Click(window, "DayAddTodo");
            shell.TodoEntry.Title = "견적서 보내기";
            shell.TodoEntry.When = "내일 3시";
            Pump(window);

            DateOnly tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
            Assert.AreEqual(tomorrow, shell.TodoEntry.Date);
            Assert.AreEqual(new TimeOnly(15, 0), shell.TodoEntry.Time);
            StringAssert.Contains(shell.TodoEntry.Readback, "오후 3:00");
            Assert.IsTrue(Named<Button>(window, "AddTodoCommit").IsEffectivelyEnabled);

            shell.TodoEntry.When = "매주 월 7시";
            Assert.AreEqual(TodoRepeat.Weekly, shell.TodoEntry.Repeat);
            Assert.AreEqual(DayOfWeek.Monday, shell.TodoEntry.Date!.Value.DayOfWeek);
            Assert.IsTrue(shell.TodoEntry.ShowsTimelineChoice, "A repeating to-do is not asked about the timeline.");

            shell.TodoEntry.When = "언젠가";
            Pump(window);
            Assert.IsTrue(shell.TodoEntry.IsWhenUnreadable);
            Assert.AreEqual("날짜로 읽지 못했어요", shell.TodoEntry.Readback);
            Assert.IsFalse(Named<Button>(window, "AddTodoCommit").IsEffectivelyEnabled, "만들기 is live on an unreadable 언제.");

            shell.TodoEntry.When = "내일 3시 회의실";
            Assert.HasCount(1, shell.TodoEntry.WhenWarnings);
            StringAssert.Contains(shell.TodoEntry.WhenWarnings[0], "회의실");
            Wait(shell.CommitAddTodoCommand.ExecuteAsync(null));
            AgendaItem made = Items().Single(item => item.Title == "견적서 보내기");
            Assert.AreEqual(new WallClock(tomorrow.ToDateTime(new TimeOnly(15, 0))), made.DueAt);
            Assert.IsTrue(made.HasDueTime);
        });
    }

    [TestMethod]
    public void An_event_with_a_day_and_no_hour_is_all_day()
    {
        WithSample((window, shell) =>
        {
            Click(window, "DayAddTodo");
            shell.TodoEntry.Title = "워크숍";
            shell.TodoEntry.When = "내일";
            shell.TodoEntry.SelectKindCommand.Execute(AgendaKind.Event);
            Assert.IsNull(shell.TodoEntry.Time, "Switching to 일정 gave a day-only 언제 an hour.");
            Wait(shell.CommitAddTodoCommand.ExecuteAsync(null));

            AgendaItem made = Items().Single(item => item.Title == "워크숍");
            DateOnly tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
            Assert.AreEqual(AgendaKind.Event, made.Kind);
            Assert.AreEqual(new WallClock(tomorrow.ToDateTime(TimeOnly.MinValue)), made.StartsAt);
            Assert.IsNull(made.EndsAt, "A day-only event is not all day.");
        });
    }

    [TestMethod]
    public void A_bare_time_stays_on_the_day_the_card_opened_on()
    {
        WithSample((window, shell) =>
        {
            LocalDate other = LocalDates.AddDays(shell.SelectedDate, 5);
            Wait(shell.SelectDateAsync(other));
            Click(window, "DayAddTodo");
            shell.TodoEntry.When = "오전 9시";
            Assert.AreEqual(LocalDates.ToDateOnly(other), shell.TodoEntry.Date, "A bare time moved off the panel's day.");
            Assert.AreEqual(new TimeOnly(9, 0), shell.TodoEntry.Time);

            shell.TodoEntry.When = string.Empty;
            Assert.AreEqual(LocalDates.ToDateOnly(other), shell.TodoEntry.Date);
            Assert.IsNull(shell.TodoEntry.Time, "Clearing 언제 left the time.");
        });
    }

    [TestMethod]
    public void Making_one_for_another_day_says_where_it_went_and_view_goes_there()
    {
        WithSample((window, shell) =>
        {
            shell.NoticeDelay = static (_, token) => Task.Delay(Timeout.Infinite, token);
            Click(window, "DayAddTodo");
            shell.TodoEntry.Title = "다음 주 준비";
            shell.TodoEntry.When = "모레";
            Wait(shell.CommitAddTodoCommand.ExecuteAsync(null));

            DateOnly target = DateOnly.FromDateTime(DateTime.Now).AddDays(2);
            Assert.IsTrue(shell.IsNoticeShown && shell.IsNoticeInPanel, "No notice for a to-do made on another day.");
            Assert.AreEqual(target.ToString("M'/'d", CultureInfo.InvariantCulture) + "에 추가됨", shell.NoticeText);
            Shoot(window, "desktop-todos-notice-light");

            Wait(shell.ViewNoticeCommand.ExecuteAsync(null));
            Assert.AreEqual(LocalDates.FromDateOnly(target), shell.SelectedDate, "보기 did not go to the day.");
            Assert.IsFalse(shell.IsNoticeShown);
            PumpUntil(() => shell.DayTodos.Any(row => row.Text == "다음 주 준비"), "The day it went to does not list it.");
        });
    }

    [TestMethod]
    public void With_the_panel_closed_the_notice_is_in_the_footer()
    {
        WithSample((window, shell) =>
        {
            shell.NoticeDelay = static (_, token) => Task.Delay(Timeout.Infinite, token);
            shell.RightCollapsed = true;
            shell.DayQuickText = string.Empty;
            Wait(shell.AddDayTodoCommand.ExecuteAsync(null));
            shell.TodoEntry.Title = "오늘 할 것";
            Wait(shell.CommitAddTodoCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsNoticeInFooter, "A closed panel's notice is not in the footer.");
            StringAssert.StartsWith(shell.NoticeText, "할 일 추가됨 · ");
        });
    }

    [TestMethod]
    public void The_edit_card_deletes_and_saves_the_timeline_choice()
    {
        WithSample((window, shell) =>
        {
            TodoItemViewModel vitamins = shell.DayTodos.Single(row => row.Text == "비타민 먹기");
            Wait(vitamins.JumpCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsRepeatChoiceOpen, "A repeat's edit did not ask which.");
            Wait(shell.ChooseEveryRepeatCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsAddTodoOpen && shell.TodoEntry.IsEditing);
            Assert.IsTrue(shell.TodoEntry.ShowsTimelineChoice);
            Assert.AreEqual(TimelineVisibility.Auto, shell.TodoEntry.TimelineVisibility);
            Shoot(window, "desktop-todos-edit-light");

            shell.TodoEntry.PickTimelineCommand.Execute(TimelineVisibility.Always);
            Wait(shell.CommitAddTodoCommand.ExecuteAsync(null));
            Assert.AreEqual(TimelineVisibility.Always, Items().Single(item => item.Title == "비타민 먹기" && item.IsSeries).TimelineVisibility);

            TodoItemViewModel milk = shell.DayTodos.Single(row => row.Text == "우유 사기");
            Wait(milk.JumpCommand.ExecuteAsync(null));
            Assert.IsTrue(Named<Button>(window, "AddTodoDelete").IsEffectivelyVisible, "The edit card has no 삭제.");
            Click(window, "AddTodoDelete");
            PumpUntil(() => Items().All(item => item.Title != "우유 사기"), "삭제 in the card did not delete.");
            Assert.IsFalse(shell.IsAddTodoOpen);
            Assert.IsTrue(shell.IsUndoShown, "No 실행 취소 after the card's delete.");
        });
    }

    [TestMethod]
    public void The_timeline_carries_events_and_to_dos_on_their_days()
    {
        WithSample((window, shell) =>
        {
            Wait(shell.ShowTimelineCommand.ExecuteAsync(null));
            PumpUntil(() => shell.TimelineGroups.Count > 0, "The timeline did not fold.");

            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            TimelineDayGroup todayGroup = shell.TimelineGroups.Single(group => LocalDates.ToDateOnly(group.Date) == today);
            string[] untimed = [.. todayGroup.Untimed.Select(row => row.Text)];
            CollectionAssert.IsSubsetOf(new[] { "분기 마감", "릴리즈 노트 작성", "우유 사기", "주간 회고 작성" }, untimed);
            Assert.IsFalse(untimed.Contains("비타민 먹기") || todayGroup.Timed.Any(slot => slot.Row?.Text == "비타민 먹기"),
                "A daily to-do on 자동 is in the timeline.");

            string[] timed = [.. todayGroup.Timed.Where(slot => !slot.IsNow).Select(slot => slot.Row!.Text)];
            CollectionAssert.IsSubsetOf(new[] { "점심 약속", "디자인 리뷰", "회의실 예약 확인" }, timed);
            Assert.AreEqual(1, todayGroup.Timed.Count(slot => slot.IsNow), "Today has no now line.");
            StringAssert.Contains(todayGroup.CountText, "일정");

            TimelineDayGroup tomorrow = shell.TimelineGroups.Single(group => LocalDates.ToDateOnly(group.Date) == today.AddDays(1));
            Assert.IsEmpty(tomorrow.Notes, "Tomorrow has no notes but its group invented some.");
            CollectionAssert.IsSubsetOf(new[] { "회의자료 초안 공유", "프론트엔드 2차 면접" },
                tomorrow.Timed.Select(slot => slot.Row?.Text).ToArray());

            // A tick in the timeline is the day panel's tick.
            TodoItemViewModel row = todayGroup.Untimed.Single(item => item.Text == "릴리즈 노트 작성");
            Wait(row.ToggleCommand.ExecuteAsync(null));
            PumpUntil(() => shell.DayTodosDone.Any(item => item.Text == "릴리즈 노트 작성"), "A tick in the timeline did not reach the day.");
        });
    }

    [TestMethod]
    public void A_new_list_opens_for_its_name_and_can_be_deleted()
    {
        WithSample((window, shell) =>
        {
            int before = shell.Todo.Lists.Count;
            Assert.IsFalse(shell.AreSidebarListsShown, "The lists are open before anyone opened them.");
            Click(window, "SidebarListsToggle");
            Assert.IsTrue(shell.AreSidebarListsShown, "The chevron did not open the lists.");
            Click(window, "NewAgendaListButton");
            PumpUntil(() => shell.Todo.Lists.Count == before + 1, "+ 새 리스트 made nothing.");
            AgendaListRowViewModel made = shell.Todo.Lists.Single(list => list.Id == shell.Todo.SelectedListId);
            Assert.IsTrue(made.IsRenaming, "The new list is not open for its name.");
            Assert.IsTrue(shell.IsListMode && shell.TabIsTodo);

            made.DraftName = "독서";
            Wait(made.CommitRenameCommand.ExecuteAsync(null));
            PumpUntil(() => shell.ListTitle == "독서", "The heading does not carry the new name.");
            Assert.IsTrue(shell.CanDeleteSelectedList);

            Click(window, "DeleteListButton");
            PumpUntil(() => shell.Todo.Lists.Count == before, "리스트 삭제 did not delete.");
            Assert.IsNull(shell.Todo.SelectedListId);
        });
    }

    [TestMethod]
    public void The_v2_frames_render_light_and_dark()
    {
        WithSample((window, shell) =>
        {
            foreach (bool dark in new[] { false, true })
            {
                string variant = dark ? "dark" : "light";
                shell.IsDark = dark;
                Wait(shell.Todo.RefreshAsync());
                shell.ShowEditorCommand.Execute(null);
                shell.IsDayDoneOpen = true;
                Shoot(window, $"desktop-todos-day-{variant}");
                shell.IsDayDoneOpen = false;

                Wait(shell.AddDayTodoCommand.ExecuteAsync(null));
                shell.TodoEntry.Title = "회의자료 초안 공유";
                shell.TodoEntry.When = "내일 3시";
                Shoot(window, $"desktop-todos-create-{variant}");
                shell.TodoEntry.When = "다음에 언젠가";
                Shoot(window, $"desktop-todos-create-unreadable-{variant}");
                shell.CloseAddTodoCommand.Execute(null);

                Wait(shell.ShowTimelineCommand.ExecuteAsync(null));
                PumpUntil(() => shell.TimelineGroups.Count > 0, "The timeline did not fold.");
                Shoot(window, $"desktop-todos-timeline-{variant}");

                Wait(shell.ShowAllTodosCommand.ExecuteAsync(null));
                Shoot(window, $"desktop-todos-list-{variant}");
                Wait(shell.ShowAgendaListCommand.ExecuteAsync(shell.Todo.Lists.Single(list => list.Name == AgendaSeed.Work)));
                Shoot(window, $"desktop-todos-list-work-{variant}");
                Wait(shell.ShowAllTodosCommand.ExecuteAsync(null));
            }

            shell.IsDark = false;
        });
    }

    /// <summary>The shell at the design's size, over the design's notes and to-dos, on today in the editor.</summary>
    private static void WithSample(Action<Avalonia.Controls.Window, DesktopShellViewModel> body)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            TestServices.WithInitialisedShell(Width, Height, (window, shell) =>
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);

                // The app's language sets the thread's culture; a test thread that was already
                // Korean in the catalogue may not have had it set.
                CultureInfo.CurrentCulture = LocalizationService.Instance.Culture;
                Seed(shell);
                Pump(window);
                body(window, shell);
            });
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    private static void Seed(DesktopShellViewModel shell)
    {
        var agenda = TestServices.CurrentProvider!.GetRequiredService<IAgendaRepository>();
        Guid work = Wait(shell.Todo.CreateListAsync(AgendaSeed.Work)).Id;
        Guid life = Wait(shell.Todo.CreateListAsync(AgendaSeed.Life)).Id;
        Guid shopping = Wait(shell.Todo.CreateListAsync(AgendaSeed.Shopping)).Id;
        Wait(shell.Todo.SelectListAsync(shell.Todo.SelectedListId));

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        foreach (AgendaItem item in AgendaSeed.Sample(today, work, life, shopping))
        {
            Wait(agenda.SaveAsync(item).AsTask());
        }

        Wait(shell.NewNoteCommand.ExecuteAsync(null));
        shell.Notes.EditorText = "이번 주 주간회의 안건 정리\n\n1. 2분기 지표 리뷰\n2. 신규 기능 우선순위 논의\n3. 채용 진행 상황 공유";
        Wait(shell.Notes.FlushAsync(Daynote.Core.Notes.FlushReason.NoteChange));
        Wait(shell.Notes.RenameAsync(shell.Notes.SelectedTab, "주간회의 준비"));
        Wait(shell.Todo.RefreshAsync());
        Wait(shell.Timeline.LoadAsync());
        Pump();
    }

    private static IReadOnlyList<AgendaItem> Items()
    {
        var agenda = TestServices.CurrentProvider!.GetRequiredService<IAgendaRepository>();
        return Wait(agenda.GetAllAsync().AsTask());
    }

    private static T Named<T>(Avalonia.Controls.Window window, string name)
        where T : Control
    {
        Pump(window);
        return window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    }

    private static void Click(Avalonia.Controls.Window window, string name, bool required = true)
    {
        Pump(window);
        Button? button = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(control => control.Name == name);
        if (button is null && !required)
        {
            return;
        }

        Assert.IsNotNull(button, $"{name} is not on screen.");
        Assert.IsTrue(button.IsEffectivelyVisible && button.IsEffectivelyEnabled, $"{name} cannot be clicked.");
        button.Command!.Execute(button.CommandParameter);
        Pump(window);
    }

    private static void Shoot(Avalonia.Controls.Window window, string name)
    {
        Pump(window);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        string directory = Environment.GetEnvironmentVariable("DAYNOTE_FRAMES_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "frames", "desktop-todos");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
        work.GetAwaiter().GetResult();
    }

    private static T Wait<T>(Task<T> work)
    {
        Wait((Task)work);
        return work.Result;
    }

    private static void PumpUntil(Func<bool> settled, string because)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!settled())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, because);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    private static void Pump(Avalonia.Controls.Window? window = null)
    {
        for (int i = 0; i < 20; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            window?.UpdateLayout();
            Thread.Sleep(5);
        }
    }
}
