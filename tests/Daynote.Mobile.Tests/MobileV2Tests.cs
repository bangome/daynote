using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Mobile.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Daynote.Mobile.Tests.ScreenshotTests;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Mobile B v2 (Daynote Mobile B v2.dc.html): the home screen's 일정 / 할 일 / 노트, the dots for the
/// agenda, and the month sheet pushed up into the month's calendar with the day's agenda under it.
/// Frames go to <c>DAYNOTE_FRAMES_DIR</c> (or artifacts/mobile-v2) to be laid beside the design.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MobileV2Tests
{
    [TestMethod]
    public void The_home_screen_puts_events_then_to_dos_then_notes()
    {
        WithSample((view, shell, today) =>
        {
            Assert.AreEqual(2, shell.EventGroups.Count, "Today's events and the next week's.");
            Assert.AreEqual("오늘", shell.EventGroups[0].Label);
            CollectionAssert.AreEqual(new[] { "분기 마감", "스탠드업", "점심 약속", "디자인 리뷰" },
                shell.EventGroups[0].Rows.Select(row => row.Item.Text).ToArray(), "Today's events are not all-day first, then by time.");
            Assert.AreEqual("다가오는 일정 · 7일", shell.EventGroups[1].Label);
            Assert.AreEqual("내일", shell.EventGroups[1].Rows[0].Sub);
            Assert.AreEqual("7", shell.HomeEventCountText);

            HomeAgendaGroup day = shell.TodoGroupsHome[0];
            Assert.IsTrue(day.HasAddLine);
            Assert.IsTrue(day.Rows.All(row => row.Item.IsTask), "An event is among the to-dos.");
            Assert.AreEqual("완료하지 않은 할 일", shell.TodoGroupsHome[1].Label);
            HomeAgendaRow late = shell.TodoGroupsHome[1].Rows.Single(row => row.Item.Text == "서류 제출");
            Assert.IsTrue(late.IsLate, "A to-do two days overdue is not red.");

            shell.ToggleHomeEventsCommand.Execute(null);
            Assert.IsFalse(shell.IsHomeEventsOpen);
            Assert.IsFalse(view.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "HomeEventGroups").IsEffectivelyVisible);
            shell.ToggleHomeEventsCommand.Execute(null);
        });
    }

    [TestMethod]
    public void The_add_line_makes_a_to_do_on_the_day()
    {
        WithSample((view, shell, today) =>
        {
            shell.HomeQuickText = "택배 보내기";
            Pump(() => shell.HomeQuickAddCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.TodoGroupsHome[0].Rows.Any(row => row.Item.Text == "택배 보내기"));
            Assert.AreEqual(string.Empty, shell.HomeQuickText);
        });
    }

    [TestMethod]
    public void A_day_with_a_dated_to_do_has_a_dot()
    {
        WithSample((view, shell, today) =>
        {
            LocalDate dayAfter = LocalDates.AddDays(today, 5);
            if (shell.Week.SingleOrDefault(day => day.Date == dayAfter) is { } pill)
            {
                Assert.IsTrue(pill.HasNotes, "The week strip has no dot for a day with only an event.");
            }

            CalendarDayCellViewModel? cell = shell.Calendar.Cells.SingleOrDefault(c => c.IsInMonth && c.Date == dayAfter);
            if (cell is not null)
            {
                Assert.IsTrue(cell.ShowsDot, "The month sheet has no dot for a day with only an event.");
            }
        });
    }

    [TestMethod]
    public void The_month_sheet_pushes_up_into_the_calendar()
    {
        WithSample((view, shell, today) =>
        {
            shell.OpenMonthPickerCommand.Execute(null);
            Pump(() => shell.ExpandMonthBoardCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsMonthBoardOpen);
            CalendarBoardDay day = shell.MonthBoard.Cells.Single(cell => cell.IsInMonth && cell.Date == today);
            Assert.IsTrue(day.IsSelected && day.IsToday);
            Assert.IsTrue(day.Lines.Count <= 3, "A phone day shows more than three lines.");
            Assert.IsTrue(day.HasMore, "A full day does not say how many more.");
            Assert.AreEqual(day.Events.Count + day.Todos.Count(todo => todo.ShowsInTimeline) - day.Lines.Count, day.MoreCount);
            Assert.IsTrue(shell.MonthAgenda.Count >= 4);
            Assert.IsTrue(shell.MonthAgenda.First().Item.IsEvent, "Events do not lead the agenda.");
            StringAssert.Contains(shell.MonthAgendaSubtitle, "일정 4");

            // A day tapped in the calendar becomes the day; the calendar stays.
            LocalDate other = LocalDates.AddDays(today, today.Day > 15 ? -3 : 3);
            Pump(() => shell.MonthBoard.Cells.First(cell => cell.Date == other).SelectCommand.ExecuteAsync(null));
            Assert.AreEqual(other, shell.SelectedDate);
            Assert.IsTrue(shell.IsMonthBoardOpen && shell.IsMonthPickerOpen);

            shell.OpenMonthAgendaDayCommand.Execute(null);
            Assert.IsFalse(shell.IsMonthPickerOpen, "이 날 보기 left the calendar up.");
            Assert.IsFalse(shell.IsMonthBoardOpen);
        });
    }

    [TestMethod]
    public void The_handle_toggles_and_a_pull_down_goes_back_to_half()
    {
        WithSample((view, shell, today) =>
        {
            shell.OpenMonthPickerCommand.Execute(null);
            Assert.IsNotNull(shell.MonthSheetExpand, "The half sheet cannot be pushed up.");
            Assert.IsNull(shell.MonthSheetCollapse);
            Pump(() => shell.ToggleMonthBoardCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsMonthBoardOpen);
            Assert.IsNull(shell.MonthSheetExpand);
            Assert.IsNotNull(shell.MonthSheetCollapse, "A pull down would close the calendar rather than fold it.");
            shell.MonthSheetCollapse!.Execute(null);
            Assert.IsFalse(shell.IsMonthBoardOpen);
            Assert.IsTrue(shell.IsMonthPickerOpen, "Folding the calendar closed its sheet.");
        });
    }

    [TestMethod]
    public void The_v2_frames_render_light_and_dark()
    {
        WithSample((view, shell, today) =>
        {
            foreach (bool dark in new[] { false, true })
            {
                string variant = dark ? "dark" : "light";
                shell.IsDark = dark;
                Pump(() => shell.RefreshAllAsync());
                shell.GoToPageCommand.Execute(MobilePage.Day);
                Shoot(view, $"mv2-day-{variant}");

                ScrollViewer scroller = view.GetVisualDescendants().OfType<Views.DayPage>().Single()
                    .GetVisualDescendants().OfType<ScrollViewer>().First();
                scroller.Offset = new Vector(0, 600);
                Shoot(view, $"mv2-day-scrolled-{variant}");
                scroller.Offset = default;

                shell.OpenMonthPickerCommand.Execute(null);
                Shoot(view, $"mv2-sheet-{variant}");
                Pump(() => shell.ExpandMonthBoardCommand.ExecuteAsync(null));
                Shoot(view, $"mv2-full-{variant}");
                shell.CloseMonthBoardCommand.Execute(null);
                Shoot(view, $"mv2-closed-{variant}");

                shell.GoToPageCommand.Execute(MobilePage.Lists);
                Shoot(view, $"mv2-lists-{variant}");
                shell.GoToPageCommand.Execute(MobilePage.Search);
                Shoot(view, $"mv2-search-{variant}");
                shell.GoToPageCommand.Execute(MobilePage.Settings);
                Shoot(view, $"mv2-settings-{variant}");
                shell.GoToPageCommand.Execute(MobilePage.Day);
            }

            shell.IsDark = false;
        });
    }

    /// <summary>The design's sample: six notes, four lists' worth of to-dos and events around today.</summary>
    private static void WithSample(Action<Control, MobileShellViewModel, LocalDate> body)
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            view.PreviewSafeArea = new Thickness(0, 56, 0, 34);
            LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));
            SeedSample(shell, today);
            body(view, shell, today);
        });
    }

    private static void SeedSample(MobileShellViewModel shell, LocalDate today)
    {
        (int Offset, string Title, bool Favorite, string Body)[] notes =
        [
            (-6, "월간 보고 초안", true, "월간 보고 핵심 요약\n\n- MAU 12.4만 (+8.2% MoM)\n- 신규 결제 전환율 3.1%"),
            (-4, "뉴스클리핑", false, "오늘의 업계 뉴스 정리\n\n- 모바일 순찰관리 서비스 사례"),
            (-1, "고객 미팅 메모", false, "A사 정기 미팅\n\n- 대시보드 로딩 속도 개선 요청\n- 엑셀 내보내기 기능 문의"),
            (2, "휴가 전 인수인계", false, "휴가 전 정리할 것"),
            (0, "주간회의 준비", true, "이번 주 주간회의 안건 정리\n\n1. 2분기 지표 리뷰\n2. 신규 기능 우선순위 논의"),
            (0, "배포 체크리스트", false, "v2.4.0 배포 전 확인사항"),
        ];
        foreach ((int offset, string title, bool favorite, string body) in notes)
        {
            Pump(() => shell.SelectDateAsync(LocalDates.AddDays(today, offset)));
            Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
            shell.Notes.EditorText = body;
            Pump(() => shell.Notes.FlushAsync(FlushReason.NoteChange));
            Pump(() => shell.Notes.RenameAsync(shell.Notes.SelectedTab!, title));
            if (favorite)
            {
                Pump(() => shell.ToggleFavoriteCommand.ExecuteAsync(null));
            }

            Pump(() => shell.CloseEditorAsync());
        }

        var agenda = (IAgendaRepository)TestServices.CurrentProvider!.GetService(typeof(IAgendaRepository))!;
        Guid work = default, life = default, shop = default;
        Pump(async () =>
        {
            work = (await shell.Todo.CreateListAsync("업무")).Id;
            life = (await shell.Todo.CreateListAsync("개인")).Id;
            shop = (await shell.Todo.CreateListAsync("장보기")).Id;
            await shell.Todo.SelectListAsync(shell.Todo.SelectedListId);
        });

        DateOnly d = LocalDates.ToDateOnly(today);
        AgendaItem[] items =
        [
            Item("회의자료 초안 공유", AgendaKind.Task, d.AddDays(1), new TimeOnly(10, 0), list: work),
            Item("회의실 예약 확인", AgendaKind.Task, d, new TimeOnly(17, 0), list: work),
            Item("지난주 액션아이템 정리", AgendaKind.Task, d, list: work, done: true),
            Item("스테이징 회귀 테스트", AgendaKind.Task, d.AddDays(2), new TimeOnly(14, 0), list: work),
            Item("릴리즈 노트 작성", AgendaKind.Task, d, list: work),
            Item("속도 개선 이슈 티켓 생성", AgendaKind.Task, d.AddDays(-1), new TimeOnly(12, 0), list: work),
            Item("비타민 먹기", AgendaKind.Task, d.AddDays(-30), new TimeOnly(8, 0), rrule: "FREQ=DAILY", list: life),
            Item("우유 사기", AgendaKind.Task, d, list: shop),
            Item("서류 제출", AgendaKind.Task, d.AddDays(-2)),
            Item("스탠드업", AgendaKind.Event, d.AddDays(-30), new TimeOnly(9, 0), new TimeOnly(9, 30), "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", work),
            Item("A사 정기 미팅", AgendaKind.Event, d.AddDays(-1), new TimeOnly(15, 0), new TimeOnly(16, 0), list: work),
            Item("분기 마감", AgendaKind.Event, d, list: work),
            Item("점심 약속", AgendaKind.Event, d, new TimeOnly(12, 30), new TimeOnly(13, 30), list: life),
            Item("디자인 리뷰", AgendaKind.Event, d, new TimeOnly(16, 0), new TimeOnly(17, 0), list: work),
            Item("프론트엔드 2차 면접", AgendaKind.Event, d.AddDays(1), new TimeOnly(14, 0), new TimeOnly(15, 0), list: work),
            Item("팀 회식", AgendaKind.Event, d.AddDays(3), new TimeOnly(19, 0), new TimeOnly(21, 0), list: work),
            Item("치과", AgendaKind.Event, d.AddDays(5), new TimeOnly(10, 0), new TimeOnly(11, 0), list: life),
            Item("분기 리뷰", AgendaKind.Event, d.AddDays(10), new TimeOnly(13, 0), new TimeOnly(15, 0), list: work),
            Item("가족 저녁", AgendaKind.Event, d.AddDays(-5), new TimeOnly(18, 30), new TimeOnly(20, 0), list: life),
        ];
        Pump(async () =>
        {
            foreach (AgendaItem item in items)
            {
                await agenda.SaveAsync(item);
            }
        });

        Pump(() => shell.SelectDateAsync(today));
        Pump(() => shell.RefreshAllAsync());
    }

    private static AgendaItem Item(
        string title, AgendaKind kind, DateOnly day, TimeOnly? time = null, TimeOnly? end = null, string? rrule = null,
        Guid? list = null, bool done = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var at = new WallClock(day.ToDateTime(time ?? TimeOnly.MinValue));
        return new AgendaItem(
            Guid.NewGuid(), list is { } id && id != Guid.Empty ? id : AgendaList.DefaultId, kind, title, string.Empty, AgendaZone.Local(),
            StartsAt: kind == AgendaKind.Event || rrule is not null ? at : null,
            EndsAt: kind == AgendaKind.Event && end is { } stop ? new WallClock(day.ToDateTime(stop)) : null,
            DueAt: kind == AgendaKind.Task ? at : null,
            HasDueTime: kind == AgendaKind.Task && time is not null,
            rrule, SeriesId: null, RecurrenceId: null,
            done ? AgendaStatus.Completed : AgendaStatus.NeedsAction, done ? now : null, 0, TimelineVisibility.Auto,
            SourceNoteId: null, ExceptionDates: [], kind == AgendaKind.Task ? AgendaAlert.Default : AgendaAlert.None, now, now);
    }

    private static void Shoot(Control view, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        TopLevel.GetTopLevel(view)?.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = (TopLevel.GetTopLevel(view) as Window)?.CaptureRenderedFrame();
        Assert.IsNotNull(frame, $"{name}: nothing rendered.");
        string directory = Environment.GetEnvironmentVariable("DAYNOTE_FRAMES_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "frames", "mobile-v2");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
