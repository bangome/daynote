using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop.Tests;

/// <summary>
/// A to-do row's 편집 and 삭제 on the desktop: its right-click menu and its Delete key, through
/// the phone's use cases (DeleteAgendaItem, EditAgendaItem), checked against the store.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TodoRowActionsTests
{
    private static readonly string FramesDirectory = Path.Combine(AppContext.BaseDirectory, "frames", "todo-actions");

    private const string Report = "보고서 보내기";
    private const string Stretch = "스트레칭";

    [TestMethod]
    public void The_row_menu_offers_edit_and_delete_with_live_commands()
    {
        WithDay((window, shell) =>
        {
            Button row = Row(window, Report);
            ContextMenu menu = row.ContextMenu ?? throw new AssertFailedException("The to-do row has no context menu.");
            menu.Open(row);
            try
            {
                List<MenuItem> items = menu.GetVisualDescendants().OfType<MenuItem>().ToList();
                if (items.Count == 0)
                {
                    items = menu.Items.OfType<MenuItem>().ToList();
                }

                Assert.AreSequenceEqual(new[] { "편집", "삭제" }, items.Select(item => item.Header?.ToString()).ToArray());
                Assert.IsTrue(items.All(item => item.Command is not null), "A menu item's binding did not resolve.");
                items[1].Command!.Execute(null);
            }
            finally
            {
                menu.Close();
            }

            PumpUntil(() => Find(Report) is null, "삭제 from the menu did not delete.");
            PumpUntil(() => shell.IsUndoShown, "No 실행 취소 after a delete.");
            Shoot(window, "undo-notice");

            window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "UndoDeleteButton").Command!.Execute(null);
            PumpUntil(() => Find(Report) is not null, "실행 취소 did not put it back.");
            PumpUntil(() => shell.DayTodos.Any(item => item.Text == Report), "The day panel did not get it back.");
            Assert.IsFalse(shell.IsUndoShown);
        });
    }

    [TestMethod]
    public void Delete_on_a_focused_row_deletes_it()
    {
        WithDay((window, shell) =>
        {
            Button row = Row(window, Report);
            row.Focus();
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            PumpUntil(() => Find(Report) is null, "Delete on the focused to-do did not delete it.");
            PumpUntil(() => !shell.DayTodos.Any(item => item.Text == Report), "The day panel still lists it.");
            PumpUntil(() => shell.IsUndoShown, "No 실행 취소 after a delete.");
        });
    }

    [TestMethod]
    public void Deleting_a_repeat_asks_and_this_one_only_adds_an_exdate()
    {
        WithDay((window, shell) =>
        {
            Wait(Row(window, Stretch).DataContext is TodoItemViewModel item ? item.DeleteCommand.ExecuteAsync(null) : Task.CompletedTask);
            Pump(window);
            Assert.IsTrue(shell.IsRepeatChoiceOpen, "A repeating to-do was deleted without asking.");
            Shoot(window, "repeat-choice");

            Click(window, "DeleteThisOccurrence");
            PumpUntil(() => Find(Stretch)?.ExceptionDates.Count == 1, "이 항목만 삭제 did not add an EXDATE.");
            DateOnly today = LocalDates.ToDateOnly(shell.SelectedDate);
            Assert.HasCount(1, AgendaDay.For(today.AddDays(1), Items()).Open, "Tomorrow's repeat went too.");

            Click(window, "UndoDeleteButton");
            PumpUntil(() => Find(Stretch)?.ExceptionDates.Count == 0, "실행 취소 did not take the EXDATE off.");
            PumpUntil(() => shell.DayTodos.Any(item => item.Text == Stretch), "The day panel did not get it back.");
        });
    }

    [TestMethod]
    public void Edit_opens_the_card_on_the_item_and_saves_over_it()
    {
        WithDay((window, shell) =>
        {
            AgendaItem before = Find(Report)!;
            Wait(((TodoItemViewModel)Row(window, Report).DataContext!).EditCommand.ExecuteAsync(null));
            Pump(window);

            Assert.IsTrue(shell.IsAddTodoOpen, "편집 did not open the card.");
            Assert.IsTrue(shell.TodoEntry.IsEditing);
            Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "EditTodoHeading").IsEffectivelyVisible);
            Assert.AreEqual(Report, shell.TodoEntry.Title);
            Assert.AreEqual(new TimeOnly(14, 0), shell.TodoEntry.Time);
            Shoot(window, "edit-card");

            shell.TodoEntry.Title = "보고서 다시 보내기";
            Pump(window);
            Click(window, "AddTodoCommit");
            PumpUntil(() => Find("보고서 다시 보내기") is not null, "저장 did not write the edit.");

            AgendaItem after = Find("보고서 다시 보내기")!;
            Assert.AreEqual(before.Id, after.Id, "An edit made a second item.");
            Assert.AreEqual(before.SourceNoteId, after.SourceNoteId);
            Assert.HasCount(2, Items());
            PumpUntil(() => shell.DayTodos.Any(item => item.Text == "보고서 다시 보내기"), "The day panel did not show the edit.");
        });
    }

    [TestMethod]
    public void Editing_this_repeat_only_writes_an_override()
    {
        WithDay((window, shell) =>
        {
            AgendaItem series = Find(Stretch)!;
            Wait(((TodoItemViewModel)Row(window, Stretch).DataContext!).EditCommand.ExecuteAsync(null));
            Pump(window);
            Assert.IsTrue(shell.IsRepeatChoiceOpen, "Editing a repeat did not ask which days.");
            Click(window, "EditThisOccurrence");
            PumpUntil(() => shell.IsAddTodoOpen, "이 항목만 did not open the card.");

            shell.TodoEntry.Title = "스트레칭 20분";
            Pump(window);
            Click(window, "AddTodoCommit");
            PumpUntil(() => Items().Any(item => item.SeriesId == series.Id), "이 항목만 wrote no override.");

            Assert.AreEqual(Stretch, Find(Stretch)!.Title, "이 항목만 changed the rule.");
            Assert.AreEqual("스트레칭 20분", Items().Single(item => item.SeriesId == series.Id).Title);
            PumpUntil(() => shell.DayTodos.Any(item => item.Text == "스트레칭 20분"), "The day panel did not show the edit.");
        });
    }

    /// <summary>Today with a one-off from a note, at 14:00, and a daily repeat.</summary>
    private static void WithDay(Action<Window, DesktopShellViewModel> body) =>
        TestServices.WithInitialisedShell((window, shell) =>
        {
            DateOnly today = LocalDates.ToDateOnly(shell.SelectedDate);
            DateTimeOffset made = DateTimeOffset.UtcNow.AddHours(-1);
            var agenda = TestServices.CurrentProvider!.GetRequiredService<IAgendaRepository>();
            Wait(agenda.SaveAsync(Item(Report, made) with
            {
                DueAt = new WallClock(today.ToDateTime(new TimeOnly(14, 0))),
                HasDueTime = true,
                SourceNoteId = Guid.NewGuid(),
            }).AsTask());
            Wait(agenda.SaveAsync(Item(Stretch, made) with
            {
                Rrule = "FREQ=DAILY",
                StartsAt = new WallClock(today.AddDays(-3).ToDateTime(new TimeOnly(7, 0))),
                DueAt = new WallClock(today.AddDays(-3).ToDateTime(new TimeOnly(7, 0))),
                HasDueTime = true,
            }).AsTask());
            Wait(shell.Todo.RefreshAsync());
            shell.UndoDelay = (_, token) => Task.Delay(Timeout.Infinite, token);
            PumpUntil(() => shell.DayTodos.Count == 2, "The day panel did not list the seeded to-dos.");
            Pump(window);
            body(window, shell);
        });

    private static AgendaItem Item(string title, DateTimeOffset made) => new(
        Guid.NewGuid(), AgendaList.DefaultId, AgendaKind.Task, title, string.Empty, "Asia/Seoul",
        StartsAt: null, EndsAt: null, DueAt: null, HasDueTime: false, Rrule: null, SeriesId: null, RecurrenceId: null,
        AgendaStatus.NeedsAction, CompletedUtc: null, Priority: 0, TimelineVisibility.Auto, SourceNoteId: null,
        ExceptionDates: [], AgendaAlert.Default, made, made);

    /// <summary>The day panel's row button for <paramref name="title"/>.</summary>
    private static Button Row(Window window, string title)
    {
        Pump(window);
        return window.GetVisualDescendants().OfType<ItemsControl>().Single(list => list.Name == "DayTodoList")
            .GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("rowcontent") && (button.DataContext as TodoItemViewModel)?.Text == title);
    }

    private static void Click(Window window, string name)
    {
        Pump(window);
        Button button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == name);
        Assert.IsTrue(button.IsEffectivelyVisible && button.IsEffectivelyEnabled, $"{name} cannot be clicked.");
        button.Command!.Execute(button.CommandParameter);
        Pump(window);
    }

    private static IReadOnlyList<AgendaItem> Items()
    {
        var agenda = TestServices.CurrentProvider!.GetRequiredService<IAgendaRepository>();
        Task<IReadOnlyList<AgendaItem>> read = agenda.GetAllAsync().AsTask();
        Wait(read);
        return read.Result;
    }

    private static AgendaItem? Find(string title) => Items().SingleOrDefault(item => item.Title == title && item.SeriesId is null);

    private static void Shoot(Window window, string name)
    {
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump(window);
        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        Directory.CreateDirectory(FramesDirectory);
        bitmap.Save(Path.Combine(FramesDirectory, name + ".png"), new PngBitmapEncoderOptions());
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

    private static void Pump(Window window)
    {
        for (int i = 0; i < 20; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Thread.Sleep(5);
        }
    }
}
