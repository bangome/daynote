using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// A to-do row's swipe, driven with the pointer as a thumb would: left for 편집 and 삭제, right for
/// done, and nothing at all for a scroll.
/// </summary>
/// <remarks>
/// Checked against what reached the agenda store, since that row is what reminders, widgets and
/// sync all read.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class TodoSwipeTests
{
    private const string Report = "보고서 보내기";
    private const string Call = "거래처 전화";
    private const string Stretch = "스트레칭";

    [TestMethod]
    public void A_full_swipe_left_deletes_a_one_off_and_undo_restores_it()
    {
        WithDay((view, shell) =>
        {
            AgendaItem before = Stored(Report);
            Swipe(view, Row(view, Report), -0.75);

            Assert.IsNull(Find(Report), "A full swipe did not delete.");
            Assert.IsFalse(shell.DayTodos.Any(row => row.Item.Text == Report), "The day still lists it.");
            Assert.IsTrue(shell.IsUndoShown, "No 실행 취소 after a delete.");
            Assert.IsTrue(view.FindControl<Border>("UndoToast")!.IsVisible);

            Click(view, view.FindControl<Button>("UndoButton")!);

            AgendaItem back = Find(Report) ?? throw new AssertFailedException("Undo did not put it back.");
            Assert.AreEqual(before.Id, back.Id);
            Assert.IsGreaterThan(before.UpdatedUtc, back.UpdatedUtc, "The restore would lose to its tombstone.");
            Assert.IsTrue(shell.DayTodos.Any(row => row.Item.Text == Report));
            Assert.IsFalse(shell.IsUndoShown);
        });
    }

    [TestMethod]
    public void A_partial_swipe_left_opens_the_two_buttons()
    {
        WithDay((view, shell) =>
        {
            SwipeRow row = Row(view, Report);
            Swipe(view, row, -0.3);

            Assert.IsTrue(row.IsOpen, "A partial swipe did not stay open.");
            Assert.AreEqual(-2 * SwipeRow.ButtonWidth, row.Offset);
            Assert.IsNotNull(Find(Report), "A partial swipe deleted.");

            // A short swipe springs back closed.
            SwipeRow other = Row(view, Call);
            Swipe(view, other, -0.12);
            Assert.IsFalse(other.IsOpen);
            Assert.AreEqual(0, other.Offset);
        });
    }

    [TestMethod]
    public void Only_one_row_is_open_and_a_press_elsewhere_closes_it()
    {
        WithDay((view, shell) =>
        {
            SwipeRow first = Row(view, Report);
            SwipeRow second = Row(view, Call);
            Swipe(view, first, -0.3);
            Swipe(view, second, -0.3);

            Assert.IsTrue(second.IsOpen);
            Assert.IsFalse(first.IsOpen, "Two rows are open at once.");
            Assert.AreEqual(0, first.Offset);

            Click(view, view.GetVisualDescendants().OfType<DayPage>().Single()
                .GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddTodoButton"), press: false);
            Assert.IsFalse(second.IsOpen, "A press elsewhere left the row open.");
        });
    }

    [TestMethod]
    public void A_swipe_right_ticks_and_on_a_done_row_unticks()
    {
        WithDay((view, shell) =>
        {
            Swipe(view, Row(view, Report), 0.4);

            Assert.AreEqual(AgendaStatus.Completed, Stored(Report).Status, "A swipe right did not tick.");
            Assert.IsTrue(shell.DayTodos.Single(row => row.Item.Text == Report).Item.Checked);

            Swipe(view, Row(view, Report), 0.4);
            Assert.AreEqual(AgendaStatus.NeedsAction, Stored(Report).Status, "A swipe right on a done row did not untick it.");
        });
    }

    [TestMethod]
    public void A_vertical_drag_is_a_scroll_not_a_swipe()
    {
        WithDay((view, shell) =>
        {
            SwipeRow row = Row(view, Report);
            Drag(view, row, 0.5, dx: -60, dy: 90);

            Assert.AreEqual(0, row.Offset, "A mostly vertical drag moved the row.");
            Assert.IsFalse(row.IsOpen);
            Assert.IsNotNull(Find(Report));
        });
    }

    [TestMethod]
    public void A_swipe_from_the_screen_edge_is_left_to_the_system_back_gesture()
    {
        WithDay((view, shell) =>
        {
            SwipeRow row = Row(view, Report);
            var window = (Window)TopLevel.GetTopLevel(view)!;
            Point start = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), window)!.Value.WithX(SwipeRow.EdgeGuard / 2);
            DragFrom(view, start, dx: 220, dy: 0);

            Assert.AreEqual(0, row.Offset, "A swipe from the edge moved the row.");
            Assert.AreEqual(AgendaStatus.NeedsAction, Stored(Report).Status);
        });
    }

    [TestMethod]
    public void A_swipe_from_the_right_edge_is_left_to_the_system_too()
    {
        WithDay((view, shell) =>
        {
            SwipeRow row = Row(view, Report);
            var window = (Window)TopLevel.GetTopLevel(view)!;
            Point start = row.TranslatePoint(new Point(0, row.Bounds.Height / 2), window)!.Value
                .WithX(window.Bounds.Width - (SwipeRow.EdgeGuard / 2));
            DragFrom(view, start, dx: -300, dy: 0);

            Assert.AreEqual(0, row.Offset, "A swipe from the right edge moved the row.");
            Assert.IsNotNull(Find(Report));
        });
    }

    [TestMethod]
    public void A_right_click_on_a_tablet_row_opens_its_buttons()
    {
        WithDay(1180, 820, (view, shell) =>
        {
            SwipeRow row = view.GetVisualDescendants().OfType<DayTodoPanel>().Single()
                .GetVisualDescendants().OfType<SwipeRow>()
                .Single(each => (each.DataContext as TodoRowViewModel)?.Item.Text == Call);
            var window = (Window)TopLevel.GetTopLevel(view)!;
            Settle(view);
            Point centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre);
            window.MouseDown(centre, MouseButton.Right);
            window.MouseUp(centre, MouseButton.Right);
            Settle(view);

            Assert.IsTrue(row.IsOpen, "A right-click did not open the row on its buttons.");
            Tap(view, Action(row, "MobileTodoDelete"));
            Assert.IsNull(Find(Call), "삭제 from a right-click did not delete.");
        });
    }

    [TestMethod]
    public void Edit_opens_the_sheet_filled_in_and_saves_over_the_item()
    {
        WithDay((view, shell) =>
        {
            AgendaItem before = Stored(Report);
            SwipeRow row = Row(view, Report);
            Swipe(view, row, -0.3);
            Tap(view, Action(row, "MobileTodoEdit"));

            PumpUntil(() => shell.IsTodoSheetOpen, "편집 did not open the sheet.");
            Assert.IsTrue(shell.Entry.IsEditing);
            Assert.IsTrue(view.FindControl<TextBlock>("TodoEditTitle")!.IsEffectivelyVisible, "The sheet does not say it is editing.");
            Assert.AreEqual(Report, shell.Entry.Title);
            Assert.AreEqual(DateOnly.FromDateTime(before.DueAt!.Value.Value), shell.Entry.Date);
            Assert.AreEqual(new TimeOnly(14, 0), shell.Entry.Time);
            Assert.AreEqual("팀장님 참조", shell.Entry.Description);

            shell.Entry.Title = "보고서 다시 보내기";
            Pump(() => shell.CommitTodoSheetCommand.ExecuteAsync(null));

            AgendaItem after = Stored("보고서 다시 보내기");
            Assert.AreEqual(before.Id, after.Id, "An edit made a second item.");
            Assert.AreEqual(before.SourceNoteId, after.SourceNoteId);
            Assert.IsGreaterThan(before.UpdatedUtc, after.UpdatedUtc);
            Assert.HasCount(3, Items());
        });
    }

    [TestMethod]
    public void A_tap_on_a_row_with_no_note_opens_it_for_editing()
    {
        WithDay((view, shell) =>
        {
            Button body = Row(view, Call).GetVisualDescendants().OfType<Button>().Single(button => button is not TodoCheck);
            Click(view, body);

            PumpUntil(() => shell.IsTodoSheetOpen, "A tap on a to-do with no note did nothing.");
            Assert.IsTrue(shell.Entry.IsEditing);
            Assert.AreEqual(Call, shell.Entry.Title);
        });
    }

    [TestMethod]
    public void A_tap_on_a_row_captured_from_a_note_opens_the_to_do_not_the_note()
    {
        WithDay((view, shell) =>
        {
            Button body = Row(view, Report).GetVisualDescendants().OfType<Button>().Single(button => button is not TodoCheck);
            Click(view, body);

            PumpUntil(() => shell.IsTodoSheetOpen, "A tap on a migrated to-do did not open the sheet.");
            Assert.IsTrue(shell.Entry.IsEditing);
            Assert.AreEqual(Report, shell.Entry.Title);
            Assert.IsFalse(shell.IsEditorOpen, "A tap on a to-do opened a note.");
        });
    }

    [TestMethod]
    public void A_tap_on_a_repeating_row_asks_which_days_before_editing()
    {
        WithDay((view, shell) =>
        {
            Button body = Row(view, Stretch).GetVisualDescendants().OfType<Button>().Single(button => button is not TodoCheck);
            Click(view, body);

            Assert.IsTrue(shell.IsRepeatChoiceOpen, "A tap on a repeat did not ask which days.");
            Assert.IsFalse(shell.IsEditorOpen);
        });
    }

    [TestMethod]
    public void A_tap_in_the_lists_tab_opens_the_to_do_for_editing()
    {
        WithDay((_, shell) =>
        {
            TodoRowViewModel row = shell.TodoGroups.SelectMany(group => group.Items).Single(r => r.Item.Text == Report);
            Pump(() => row.Item.JumpCommand.ExecuteAsync(null));

            PumpUntil(() => shell.IsTodoSheetOpen, "A tap in the lists tab did not open the sheet.");
            Assert.AreEqual(Report, shell.Entry.Title);
            Assert.IsFalse(shell.IsEditorOpen, "A tap in the lists tab opened a note.");
        });
    }

    [TestMethod]
    public void Deleting_one_repeat_asks_then_adds_an_exdate_and_undo_restores_it()
    {
        WithDay((view, shell) =>
        {
            Swipe(view, Row(view, Stretch), -0.75);

            Assert.IsTrue(shell.IsRepeatChoiceOpen, "A repeating to-do was deleted without asking which days.");
            Assert.IsNotNull(Find(Stretch), "It went before the question was answered.");
            Click(view, view.FindControl<Button>("DeleteThisOccurrence")!);

            AgendaItem series = Stored(Stretch);
            Assert.HasCount(1, series.ExceptionDates);
            Assert.IsFalse(shell.DayTodos.Any(row => row.Item.Text == Stretch), "Today's repeat is still listed.");
            DateOnly today = LocalDates.ToDateOnly(shell.SelectedDate);
            Assert.HasCount(1, AgendaDay.For(today.AddDays(1), Items()).Open, "Tomorrow's repeat went too.");

            Pump(() => shell.UndoDeleteCommand.ExecuteAsync(null));
            Assert.IsEmpty(Stored(Stretch).ExceptionDates);
            Assert.IsTrue(shell.DayTodos.Any(row => row.Item.Text == Stretch));
        });
    }

    [TestMethod]
    public void Deleting_every_repeat_removes_the_series_and_its_overrides()
    {
        WithDay((view, shell) =>
        {
            // A ticked day leaves an override behind.
            Swipe(view, Row(view, Stretch), 0.4);
            Assert.HasCount(4, Items());

            Swipe(view, Row(view, Stretch), -0.3);
            Tap(view, Action(Row(view, Stretch), "MobileTodoDelete"));
            Click(view, view.FindControl<Button>("DeleteEveryRepeat")!);

            Assert.IsFalse(Items().Any(item => item.Title == Stretch), "The series or its override survived.");
            Pump(() => shell.UndoDeleteCommand.ExecuteAsync(null));
            Assert.HasCount(2, Items().Where(item => item.Title == Stretch));
        });
    }

    [TestMethod]
    public void Editing_this_repeat_only_writes_an_override()
    {
        WithDay((view, shell) =>
        {
            AgendaItem series = Stored(Stretch);
            Swipe(view, Row(view, Stretch), -0.3);
            Tap(view, Action(Row(view, Stretch), "MobileTodoEdit"));
            Assert.IsTrue(shell.IsRepeatChoiceOpen, "Editing a repeat did not ask which days.");
            Click(view, view.FindControl<Button>("EditThisOccurrence")!);

            PumpUntil(() => shell.IsTodoSheetOpen, "이 항목만 did not open the sheet.");
            Assert.IsTrue(shell.Entry.IsOccurrenceEdit);
            Assert.IsFalse(shell.Entry.CanRepeat, "One day of a rule offered a 반복 of its own.");
            shell.Entry.Title = "스트레칭 20분";
            Pump(() => shell.CommitTodoSheetCommand.ExecuteAsync(null));

            AgendaItem rule = Items().Single(item => item.Id == series.Id);
            Assert.AreEqual((series.Title, series.UpdatedUtc), (rule.Title, rule.UpdatedUtc), "이 항목만 changed the rule.");
            AgendaItem moved = Items().Single(item => item.SeriesId == series.Id);
            Assert.AreEqual("스트레칭 20분", moved.Title);
            Assert.IsTrue(shell.DayTodos.Any(row => row.Item.Text == "스트레칭 20분"));
        });
    }

    [TestMethod]
    public void The_lists_tab_rows_swipe_too()
    {
        WithDay((view, shell) =>
        {
            shell.GoToPageCommand.Execute(MobilePage.Lists);
            Settle(view);
            SwipeRow row = view.GetVisualDescendants().OfType<ListsPage>().Single()
                .GetVisualDescendants().OfType<SwipeRow>()
                .Single(each => (each.DataContext as TodoRowViewModel)?.Item.Text == Call);

            Swipe(view, row, -0.75);

            Assert.IsNull(Find(Call), "A full swipe on the 할 일 tab did not delete.");
            Assert.IsTrue(shell.IsUndoShown);
        });
    }

    [TestMethod]
    public void The_tablet_day_panel_rows_swipe_too()
    {
        WithDay(1180, 820, (view, shell) =>
        {
            Assert.IsTrue(shell.IsTabletLayout);
            SwipeRow row = view.GetVisualDescendants().OfType<DayTodoPanel>().Single()
                .GetVisualDescendants().OfType<SwipeRow>()
                .Single(each => (each.DataContext as TodoRowViewModel)?.Item.Text == Report);

            Swipe(view, row, 0.4);

            Assert.AreEqual(AgendaStatus.Completed, Stored(Report).Status, "A swipe right in the day panel did not tick.");
        });
    }

    /// <summary>
    /// The swipe's frames, to be looked at rather than compared: a row open on 편집 and 삭제, a row
    /// mid-swipe right, the repeat question, the undo line and the editing sheet. They land in
    /// <c>artifacts/mobile-screens</c> beside the other screens.
    /// </summary>
    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void The_swipe_frames_render(string variantName)
    {
        string output = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "mobile-screens");
        Directory.CreateDirectory(output);
        string suffix = variantName.ToLowerInvariant();

        WithDay((view, shell) =>
        {
            shell.IsDark = variantName == "Dark";
            view.PreviewSafeArea = new Thickness(0, 56, 0, 34);
            view.GetVisualDescendants().OfType<DayPage>().Single()
                .GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddTodoButton").BringIntoView();
            Settle(view);

            Swipe(view, Row(view, Report), -0.3);
            Capture(view, output, $"swipe-open-{suffix}");
            SwipeRow.CloseOpen();
            Settle(view);

            Row(view, Call).ShowAt(110);
            Capture(view, output, $"swipe-right-{suffix}");
            Row(view, Call).ShowAt(0);

            Swipe(view, Row(view, Stretch), -0.75);
            Capture(view, output, $"swipe-repeat-choice-{suffix}");
            Click(view, view.FindControl<Button>("DeleteThisOccurrence")!);
            Capture(view, output, $"swipe-undo-{suffix}");
            Pump(() => shell.UndoDeleteCommand.ExecuteAsync(null));

            Swipe(view, Row(view, Report), -0.3);
            Tap(view, Action(Row(view, Report), "MobileTodoEdit"));
            PumpUntil(() => shell.IsTodoSheetOpen, "편집 did not open the sheet.");
            Capture(view, output, $"swipe-edit-sheet-{suffix}");
            shell.IsDark = false;
        });
    }

    private static void Capture(Control view, string directory, string name)
    {
        Settle(view);
        using Avalonia.Media.Imaging.WriteableBitmap? frame = (TopLevel.GetTopLevel(view) as Window)?.CaptureRenderedFrame();
        Assert.IsNotNull(frame, $"The headless platform rendered no frame for {name}.");
        frame.Save(Path.Combine(directory, $"{name}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    /// <summary>
    /// Today on the day screen with three to-dos: a one-off with a note and a time, one with
    /// neither, and a daily repeat.
    /// </summary>
    private static void WithDay(Action<MainView, MobileShellViewModel> body) => WithDay(402, 874, body);

    private static void WithDay(double width, double height, Action<MainView, MobileShellViewModel> body) =>
        TestServices.WithInitialisedShell(width, height, (view, shell) =>
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            DateTimeOffset made = DateTimeOffset.UtcNow.AddHours(-1);
            Pump(async () =>
            {
                await Agenda().SaveAsync(Item(Report, made) with
                {
                    DueAt = new WallClock(today.ToDateTime(new TimeOnly(14, 0))),
                    HasDueTime = true,
                    Description = "팀장님 참조",
                    SourceNoteId = Guid.NewGuid(),
                });
                await Agenda().SaveAsync(Item(Call, made) with { DueAt = new WallClock(today.ToDateTime(TimeOnly.MinValue)) });
                await Agenda().SaveAsync(Item(Stretch, made) with
                {
                    Rrule = "FREQ=DAILY",
                    StartsAt = new WallClock(today.AddDays(-3).ToDateTime(new TimeOnly(7, 0))),
                    DueAt = new WallClock(today.AddDays(-3).ToDateTime(new TimeOnly(7, 0))),
                    HasDueTime = true,
                });
            });
            Pump(() => shell.SelectDateAsync(LocalDates.FromDateOnly(today)));
            Pump(shell.RefreshAllAsync);
            shell.GoToPageCommand.Execute(MobilePage.Day);
            shell.UndoDelay = (_, token) => Task.Delay(Timeout.Infinite, token);
            Settle(view);
            body(view, shell);
        });

    private static AgendaItem Item(string title, DateTimeOffset made) => new(
        Guid.NewGuid(), AgendaList.DefaultId, AgendaKind.Task, title, string.Empty, "Asia/Seoul",
        StartsAt: null, EndsAt: null, DueAt: null, HasDueTime: false, Rrule: null, SeriesId: null, RecurrenceId: null,
        AgendaStatus.NeedsAction, CompletedUtc: null, Priority: 0, TimelineVisibility.Auto, SourceNoteId: null,
        ExceptionDates: [], AgendaAlert.Default, made, made);

    internal static SwipeRow Row(Control view, string title)
    {
        Settle(view);
        return view.GetVisualDescendants().OfType<DayPage>().Single()
            .GetVisualDescendants().OfType<SwipeRow>()
            .Single(row => (row.DataContext as TodoRowViewModel)?.Item.Text == title);
    }

    /// <summary>One of the two buttons an open row uncovers, by its label.</summary>
    private static Border Action(SwipeRow row, string key) =>
        row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == MobileStrings.Get(key)).Parent as Border
            ?? throw new AssertFailedException($"No {key} button.");

    /// <summary>A horizontal swipe across <paramref name="fraction"/> of the row's width, from its middle.</summary>
    internal static void Swipe(Control view, SwipeRow row, double fraction) =>
        Drag(view, row, fraction < 0 ? 0.85 : 0.15, dx: row.Bounds.Width * fraction, dy: 0);

    private static void Drag(Control view, SwipeRow row, double at, double dx, double dy)
    {
        var window = (Window)TopLevel.GetTopLevel(view)!;
        Settle(view);
        Point start = row.TranslatePoint(new Point(row.Bounds.Width * at, row.Bounds.Height / 2), window)!.Value;
        DragFrom(view, start, dx, dy);
    }

    private static void DragFrom(Control view, Point start, double dx, double dy)
    {
        var window = (Window)TopLevel.GetTopLevel(view)!;
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        const int steps = 12;
        for (int i = 1; i <= steps; i++)
        {
            window.MouseMove(start + new Point(dx * i / steps, dy * i / steps), RawInputModifiers.LeftMouseButton);
        }

        Point end = start + new Point(dx, dy);
        window.MouseUp(end, MouseButton.Left);
        window.MouseMove(new Point(1, 1));
        Settle(view);
    }

    /// <summary>A pointer tap, for the uncovered buttons, which answer to a tap rather than a command.</summary>
    private static void Tap(Control view, Control target) => Click(view, target, press: true);

    /// <summary>A pointer press and release on <paramref name="target"/>; with <paramref name="press"/> false, only the press.</summary>
    private static void Click(Control view, Control target, bool press = true)
    {
        var window = (Window)TopLevel.GetTopLevel(view)!;
        Settle(view);
        Point centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(centre);
        window.MouseDown(centre, MouseButton.Left);
        if (press)
        {
            window.MouseUp(centre, MouseButton.Left);
        }
        else
        {
            // Let go somewhere harmless, so the press is all that happened there.
            window.MouseUp(new Point(1, 1), MouseButton.Left);
        }

        window.MouseMove(new Point(1, 1));
        Settle(view);
    }

    private static Daynote.Core.Agenda.IAgendaRepository Agenda() =>
        (IAgendaRepository)TestServices.CurrentProvider!.GetService(typeof(IAgendaRepository))!;

    private static IReadOnlyList<AgendaItem> Items()
    {
        IReadOnlyList<AgendaItem> items = [];
        Pump(async () => items = await Agenda().GetAllAsync().ConfigureAwait(true));
        return items;
    }

    private static AgendaItem? Find(string title) => Items().SingleOrDefault(item => item.Title == title && item.SeriesId is null);

    private static AgendaItem Stored(string title) =>
        Find(title) ?? throw new AssertFailedException($"{title} is not in the store.");

    private static void Settle(Control view)
    {
        // A swipe's command writes to the store off the UI thread and resumes here.
        for (int i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
            Thread.Sleep(5);
        }
    }

    private static void PumpUntil(Func<bool> settled, string because)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!settled())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, because);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void Pump(Func<Task> work) => ScreenshotTests.Pump(work);
}
