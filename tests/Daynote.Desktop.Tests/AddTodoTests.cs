using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop.Tests;

/// <summary>
/// To-dos and notes are separate (docs/TODOS.md, 2026-10-10): the editor's text never makes one,
/// and + 할 일 in the day panel or the 할 일 view is the way to one.
/// </summary>
[TestClass]
public sealed class AddTodoTests
{
    private static readonly string FramesDirectory = Path.Combine(AppContext.BaseDirectory, "frames", "add-todo");

    [TestMethod]
    public void Typing_an_at_phrase_in_the_editor_opens_nothing_and_makes_nothing()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.NewNoteCommand.Execute(null);
            Pump(window);
            TextBox editor = window.FindControl<TextBox>("Editor")!;
            editor.Focus();
            editor.CaretIndex = editor.Text?.Length ?? 0;
            Pump(window);

            window.KeyTextInput("@내일 3시");
            Pump(window);

            Assert.Contains("@내일 3시", shell.Notes.EditorText, "The @ phrase did not stay in the note.");
            Assert.IsFalse(shell.IsAddTodoOpen, "Typing in the note opened the add-to-do card.");
            Assert.IsFalse(
                window.GetVisualDescendants().OfType<Popup>().Any(popup => popup.IsOpen),
                "Typing an @ in the note opened a popover.");
            Assert.IsEmpty(Items(), "Typing in a note made a to-do.");
        });
    }

    [TestMethod]
    public void The_day_panel_add_makes_a_to_do_on_its_date_with_no_note()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            LocalDate other = LocalDates.AddDays(shell.SelectedDate, 3);
            Wait(shell.SelectDateAsync(other));
            Pump(window);

            Click(window, "DayAddTodo");
            Assert.IsTrue(shell.IsAddTodoOpen, "+ 할 일 did not open the card.");
            Assert.IsTrue(window.FindControl<TextBox>("AddTodoTitle")!.IsFocused, "The title does not take the keys.");
            Assert.AreEqual(LocalDates.ToDateOnly(other), shell.TodoEntry.Date, "The card is not on the selected date.");
            Assert.AreEqual(AgendaList.DefaultId, shell.TodoEntry.ListId);
            Button commit = window.FindControl<Button>("AddTodoCommit")!;
            Assert.IsFalse(commit.IsEffectivelyEnabled, "추가 is live with no title.");

            shell.TodoEntry.Title = "견적서 보내기";
            shell.TodoEntry.Description = "메일로";
            Pump(window);
            Click(window, "AddTodoCommit");
            PumpUntil(() => !shell.IsAddTodoOpen && Items().Count == 1, "추가 did not write the item.");

            AgendaItem made = Items().Single();
            Assert.AreEqual("견적서 보내기", made.Title);
            Assert.AreEqual("메일로", made.Description);
            Assert.IsNull(made.SourceNoteId, "The item points at a note.");
            Assert.AreEqual(new WallClock(LocalDates.ToDateOnly(other).ToDateTime(TimeOnly.MinValue)), made.DueAt);
            PumpUntil(() => shell.DayTodos.Any(row => row.Text == "견적서 보내기"), "The day panel does not show it.");
        });
    }

    [TestMethod]
    public void An_event_with_a_start_end_and_repeat()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Click(window, "DayAddTodo");
            shell.TodoEntry.Title = "주간 회의";
            shell.TodoEntry.SelectKindCommand.Execute(AgendaKind.Event);
            Wait(shell.TodoEntry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Time));
            shell.TodoEntry.PickHourCommand.Execute(10);
            shell.TodoEntry.PickMinuteCommand.Execute(0);
            Wait(shell.TodoEntry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Repeat));
            shell.TodoEntry.PickRepeatCommand.Execute(TodoRepeat.Weekly);
            Pump(window);
            Click(window, "AddTodoCommit");
            PumpUntil(() => Items().Count == 1, "추가 did not write the event.");

            AgendaItem made = Items().Single();
            DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
            Assert.AreEqual(AgendaKind.Event, made.Kind);
            Assert.AreEqual(new WallClock(day.ToDateTime(new TimeOnly(10, 0))), made.StartsAt);
            Assert.AreEqual(new WallClock(day.ToDateTime(new TimeOnly(11, 0))), made.EndsAt);
            Assert.AreEqual("FREQ=WEEKLY", made.Rrule);
            Assert.IsNull(made.SourceNoteId);
        });
    }

    [TestMethod]
    public void The_list_view_add_defaults_to_today_and_the_list_being_viewed()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Wait(shell.Todo.CreateListAsync("장보기"));
            Wait(shell.Todo.RefreshAsync());
            Guid groceries = shell.Todo.Lists.Single(list => list.Name == "장보기").Id;
            Assert.AreEqual(groceries, shell.Todo.SelectedListId, "Making a list selects it.");
            Wait(shell.SelectDateAsync(LocalDates.AddDays(shell.SelectedDate, -5)));
            shell.ShowListCommand.Execute(RightTab.Todo);
            Pump(window);

            Click(window, "ListAddTodo");
            Assert.IsTrue(shell.IsAddTodoOpen);
            Assert.AreEqual(groceries, shell.TodoEntry.ListId, "The card is not filed in the list being viewed.");
            Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now), shell.TodoEntry.Date, "The 할 일 view's + is not on today.");

            shell.TodoEntry.Title = "두부";
            Pump(window);
            Click(window, "AddTodoCommit");
            PumpUntil(() => Items().Count == 1, "추가 did not write the item.");
            Assert.AreEqual(groceries, Items().Single().ListId);
        });
    }

    [TestMethod]
    public void Escape_closes_the_card_and_makes_nothing()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Click(window, "DayAddTodo");
            shell.TodoEntry.Title = "취소될 일";
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Pump(window);

            Assert.IsFalse(shell.IsAddTodoOpen, "Escape did not close the card.");
            Assert.IsEmpty(Items());
        });
    }

    /// <summary>The day panel with + 할 일, the 할 일 view's button and the card, in both themes.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void The_add_to_do_surfaces_render(bool dark)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        TestServices.WithInitialisedShell((window, shell) =>
        {
            try
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
                shell.IsDark = dark;
                string theme = dark ? "dark" : "light";
                Wait(shell.Todo.CreateListAsync("장보기"));
                Wait(shell.Todo.RefreshAsync());
                Click(window, "DayAddTodo");
                shell.TodoEntry.Title = "견적서 보내기";
                Click(window, "AddTodoCommit");
                PumpUntil(() => Items().Count == 1, "The seed to-do was not written.");
                Shoot(window, $"desktop-day-panel-{theme}-ko");

                Click(window, "DayAddTodo");
                shell.TodoEntry.Title = "회의자료 초안 공유";
                Wait(shell.TodoEntry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Date));
                Shoot(window, $"desktop-add-card-{theme}-ko");
                shell.CloseAddTodoCommand.Execute(null);

                shell.ShowListCommand.Execute(RightTab.Todo);
                Shoot(window, $"desktop-todo-view-{theme}-ko");
            }
            finally
            {
                LocalizationService.Instance.SetLanguage(original);
            }
        });
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
