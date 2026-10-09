using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The to-do sheet: a to-do or an event made in fields of its own, never typed into the note.
/// </summary>
/// <remarks>
/// Opened the way a thumb opens it — the editor's @ button — and checked against what reached the
/// agenda store, since that row is what reminders, widgets, sync and the day panel all read.
/// </remarks>
[TestClass]
public sealed class TodoSheetTests
{
    private const string Body = "주간 회의 메모";

    [TestMethod]
    public void The_toolbar_has_no_date_or_time_buttons()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenEditor(view, shell);
            EditorPage editor = view.GetVisualDescendants().OfType<EditorPage>().Single();
            string[] labels = [MobileStrings.Get("InsertDate"), MobileStrings.Get("MobileTodoTime")];

            Button[] buttons = [.. editor.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)];
            Assert.IsFalse(
                buttons.Any(button => button.GetVisualDescendants().OfType<TextBlock>().Any(text => labels.Contains(text.Text))),
                "A date or time stamp is still on the editor's toolbar.");
            Assert.IsTrue(buttons.Any(button => button.Name == "AttachTool"), "The paperclip went with them.");
            Assert.IsTrue(buttons.Any(button => button.Name == "TodoTool"), "There is no way to the sheet.");
        });
    }

    [TestMethod]
    public void The_at_button_opens_the_sheet_and_writes_nothing_into_the_note()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            OpenSheet(view, shell);

            Assert.IsTrue(shell.IsTodoSheetOpen);
            Assert.AreEqual(Body, body.Text, "The @ button typed into the note.");
            Assert.IsFalse(shell.Capture.IsOpen, "The inline @ bar opened as well.");
            Assert.IsFalse(shell.ShowDock);
            Assert.IsTrue(view.FindControl<TextBox>("TodoTextBox")!.IsFocused, "The title does not take the keyboard.");

            Assert.IsTrue(Run(shell.GoBackAsync()), "Back did not close the sheet.");
            Assert.IsFalse(shell.IsTodoSheetOpen);
            Assert.IsTrue(shell.IsEditorOpen, "Back closed the note under the sheet too.");
        });
    }

    [TestMethod]
    public void The_date_defaults_to_the_notes_own_day()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            LocalDate other = LocalDates.AddDays(shell.SelectedDate, -4);
            Pump(() => shell.SelectDateAsync(other));
            OpenEditor(view, shell);
            OpenSheet(view, shell);

            Assert.AreEqual(LocalDates.ToDateOnly(other), shell.Entry.Date);
            Assert.IsTrue(shell.Entry.IsTask, "A to-do is what the sheet starts on.");
            Assert.IsNull(shell.Entry.Time, "A to-do starts with no time.");
            Assert.AreEqual(AgendaList.DefaultId, shell.Entry.ListId);
        });
    }

    [TestMethod]
    public void Add_waits_for_a_title()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenEditor(view, shell);
            OpenSheet(view, shell);
            Button add = view.FindControl<Button>("TodoAdd")!;

            Assert.IsFalse(add.IsEffectivelyEnabled, "추가 is live with nothing to call the item.");
            shell.Entry.Text = "   ";
            Settle(view);
            Assert.IsFalse(add.IsEffectivelyEnabled, "Spaces are not a title.");

            Pump(() => shell.CommitTodoSheetCommand.ExecuteAsync(null));
            Assert.IsEmpty(Items(), "An untitled item was written.");
            Assert.IsTrue(shell.IsTodoSheetOpen);

            shell.Entry.Text = "보고서";
            Settle(view);
            Assert.IsTrue(add.IsEffectivelyEnabled);
        });
    }

    [TestMethod]
    public void A_to_do_with_no_date()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            OpenSheet(view, shell);
            shell.Entry.Text = "우유 사기";
            shell.Entry.ClearDateCommand.Execute(null);
            Assert.AreEqual(MobileStrings.Get("MobileTodoNoDate"), shell.Entry.DateText);
            Assert.IsFalse(shell.Entry.CanPickTime, "A time with no day to put it on.");

            AgendaItem made = Add(view, shell);

            Assert.AreEqual(AgendaKind.Task, made.Kind);
            Assert.AreEqual("우유 사기", made.Title);
            Assert.IsNull(made.DueAt);
            Assert.IsNull(made.StartsAt);
            Assert.IsFalse(made.HasDueTime);
            Assert.IsEmpty(made.AlarmLeadMinutes, "An undated to-do has nothing to ring at.");
            AssertFromNote(shell, body, made);
            Assert.IsFalse(shell.IsJustMadeElsewhere, "An undated to-do went to no other day.");
        });
    }

    [TestMethod]
    public void A_to_do_with_a_date_only()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            OpenSheet(view, shell);
            shell.Entry.Text = "보고서 보내기";

            AgendaItem made = Add(view, shell);

            DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
            Assert.AreEqual(new WallClock(day.ToDateTime(TimeOnly.MinValue)), made.DueAt);
            Assert.IsFalse(made.HasDueTime, "No time was given.");
            Assert.IsNull(made.StartsAt);
            Assert.AreSequenceEqual(AgendaAlert.Default.ToArray(), made.AlarmLeadMinutes.ToArray(),
                "A dated to-do starts with its one alert, as an @ one does.");
            AssertFromNote(shell, body, made);
            Assert.AreEqual(MobileStrings.Get("MobileJustNow"), shell.JustMadeWhenText);
        });
    }

    [TestMethod]
    public void A_to_do_with_a_date_and_a_time_on_another_day()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            OpenSheet(view, shell);
            shell.Entry.Text = "업체에 전화";

            // Another day, from the grid under the date row.
            Pump(() => shell.Entry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Date));
            Assert.IsTrue(shell.Entry.IsDatePickerOpen);
            DateOnly noteDay = LocalDates.ToDateOnly(shell.SelectedDate);
            DateOnly target = noteDay.Day == 1 ? noteDay.AddDays(1) : noteDay.AddDays(-1);
            Pump(() => shell.Entry.Calendar.Cells
                .Single(cell => cell.IsInMonth && LocalDates.ToDateOnly(cell.Date) == target).SelectCommand.ExecuteAsync(null));
            Assert.AreEqual(target, shell.Entry.Date);
            Assert.IsFalse(shell.Entry.IsDatePickerOpen, "Picking a day closes the grid.");

            Pump(() => shell.Entry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Time));
            shell.Entry.PickHourCommand.Execute(15);
            Assert.IsTrue(shell.Entry.IsTimePickerOpen, "The grid waits for the minute.");
            shell.Entry.PickMinuteCommand.Execute(30);
            Assert.IsFalse(shell.Entry.IsTimePickerOpen);
            Assert.AreEqual("15:30", shell.Entry.TimeText);

            AgendaItem made = Add(view, shell);

            Assert.AreEqual(new WallClock(target.ToDateTime(new TimeOnly(15, 30))), made.DueAt);
            Assert.IsTrue(made.HasDueTime);
            AssertFromNote(shell, body, made);
            Assert.IsTrue(shell.IsJustMadeElsewhere, "It went to another day; the row should say so.");
            Assert.Contains(target.ToString(MobileStrings.Get("MobileNoteItemsAddedDateFormat"),
                Daynote.App.Localization.LocalizationService.Instance.Culture), shell.JustMadeWhenText);
        });
    }

    [TestMethod]
    public void An_event_with_a_start_and_an_end()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            OpenSheet(view, shell);
            shell.Entry.Text = "디자인 리뷰";

            shell.Entry.SelectKindCommand.Execute(AgendaKind.Event);
            Assert.IsNotNull(shell.Entry.Time, "An event needs a start, so switching gives it one.");
            Assert.AreEqual(0, shell.Entry.Time!.Value.Minute, "The next whole hour.");

            Pump(() => shell.Entry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Time));
            shell.Entry.PickHourCommand.Execute(10);
            shell.Entry.PickMinuteCommand.Execute(0);
            Assert.AreEqual("11:00", shell.Entry.EndText, "An hour long until its end is picked.");

            Pump(() => shell.Entry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.End));
            shell.Entry.PickHourCommand.Execute(11);
            shell.Entry.PickMinuteCommand.Execute(30);
            Assert.AreEqual("11:30", shell.Entry.EndText);

            AgendaItem made = Add(view, shell);

            DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
            Assert.AreEqual(AgendaKind.Event, made.Kind);
            Assert.AreEqual(new WallClock(day.ToDateTime(new TimeOnly(10, 0))), made.StartsAt);
            Assert.AreEqual(new WallClock(day.ToDateTime(new TimeOnly(11, 30))), made.EndsAt);
            Assert.IsNull(made.DueAt);
            Assert.IsEmpty(made.AlarmLeadMinutes, "An event is not nagged about unless asked, as with @.");
            AssertFromNote(shell, body, made);
        });
    }

    [TestMethod]
    public void Switching_back_to_a_to_do_takes_away_the_time_it_filled_in()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenEditor(view, shell);
            OpenSheet(view, shell);
            shell.Entry.ClearDateCommand.Execute(null);

            shell.Entry.SelectKindCommand.Execute(AgendaKind.Event);
            Assert.IsNotNull(shell.Entry.Date, "An event is on a day.");
            shell.Entry.SelectKindCommand.Execute(AgendaKind.Task);

            Assert.IsNull(shell.Entry.Time);
        });
    }

    [TestMethod]
    public void It_is_filed_in_the_list_picked()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            Pump(() => shell.Todo.CreateListAsync("장보기"));
            Pump(() => shell.Todo.RefreshAsync());
            OpenEditor(view, shell);
            OpenSheet(view, shell);
            Assert.IsTrue(shell.Entry.ShowLists);

            TodoListOption groceries = shell.Entry.ListOptions.Single(option => option.Name == "장보기");
            shell.Entry.PickListCommand.Execute(groceries.Id);
            shell.Entry.Text = "두부";

            Assert.AreEqual(groceries.Id, Add(view, shell).ListId);
        });
    }

    [TestMethod]
    public void The_widget_capture_link_opens_the_sheet_on_today_s_note()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            Pump(() => shell.OpenLinkAsync(new Uri("daynote://capture?at=1")));
            Settle(view);

            Assert.IsTrue(shell.IsEditorOpen);
            Assert.IsTrue(shell.IsTodoSheetOpen, "The link did not open the sheet.");
            Assert.DoesNotContain("@", shell.Notes.EditorText, "The link typed into the note.");
            Assert.IsFalse(shell.Capture.IsOpen);

            Pump(() => shell.CloseEditorAsync());
            Pump(() => shell.OpenFromWidgetAsync(WidgetLaunch.Capture));
            Settle(view);

            Assert.IsTrue(shell.IsEditorOpen);
            Assert.IsTrue(shell.IsTodoSheetOpen, "The Android widget's @ 할 일 did not open the sheet.");
        });
    }

    /// <summary>
    /// The title field brings the keyboard up, and the sheet stands on it rather than under it.
    /// </summary>
    [TestMethod]
    public void The_sheet_stands_on_the_keyboard()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            view.PreviewSafeArea = new Avalonia.Thickness(0, 56, 0, 34);
            OpenEditor(view, shell);
            OpenSheet(view, shell);
            const double keyboard = 300;
            view.PreviewKeyboard = keyboard;
            Settle(view);

            Border sheet = view.FindControl<Border>("TodoSheet")!;
            TopLevel root = TopLevel.GetTopLevel(view)!;
            double bottom = sheet.TranslatePoint(new Avalonia.Point(0, sheet.Bounds.Height), root)!.Value.Y;
            Assert.AreEqual(root.ClientSize.Height - 34 - keyboard, bottom, 0.5, "The sheet is not on the keyboard's top edge.");

            view.PreviewKeyboard = null;
            Settle(view);
            bottom = sheet.TranslatePoint(new Avalonia.Point(0, sheet.Bounds.Height), root)!.Value.Y;
            Assert.AreEqual(root.ClientSize.Height, bottom, 0.5, "Without a keyboard it runs under the home indicator like the others.");
        });
    }

    private static TextBox OpenEditor(MainView view, MobileShellViewModel shell)
    {
        // These tests are about what was made, not the flash; waiting it out would cost 2.5 s each.
        shell.FlashDelay = _ => Task.CompletedTask;
        Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
        TextBox body = view.GetVisualDescendants().OfType<EditorPage>().Single()
            .GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "Body");
        body.Text = Body;
        body.CaretIndex = Body.Length;
        Settle(view);
        return body;
    }

    /// <summary>Taps the editor's @ button: its command, once it is on screen and live.</summary>
    internal static void OpenSheet(MainView view, MobileShellViewModel shell)
    {
        Button tool = view.GetVisualDescendants().OfType<EditorPage>().Single()
            .GetVisualDescendants().OfType<Button>().Single(button => button.Name == "TodoTool");
        Assert.IsTrue(tool.IsEffectivelyVisible && tool.IsEffectivelyEnabled, "The @ button cannot be tapped.");
        tool.Command!.Execute(tool.CommandParameter);
        PumpUntil(() => shell.IsTodoSheetOpen, "The @ button did not open the sheet.");
        Settle(view);
    }

    /// <summary>Taps 추가 and answers with the one row it wrote.</summary>
    private static AgendaItem Add(MainView view, MobileShellViewModel shell)
    {
        Button add = view.FindControl<Button>("TodoAdd")!;
        Settle(view);
        Assert.IsTrue(add.IsEffectivelyEnabled, "추가 is not live.");
        add.Command!.Execute(add.CommandParameter);
        PumpUntil(() => !shell.IsTodoSheetOpen && Items().Count == 1 && shell.NoteItems.Count == 1,
            "추가 did not write the item and close the sheet.");
        return Items().Single();
    }

    private static void AssertFromNote(MobileShellViewModel shell, TextBox body, AgendaItem made)
    {
        Assert.AreEqual(shell.Notes.SelectedTab!.Id.Value, made.SourceNoteId, "The item does not point back at its note.");
        Assert.AreEqual(Body, body.Text, "The note's text changed.");
        Assert.AreEqual(Body, shell.Notes.EditorText);
        Assert.AreEqual(made.Title, shell.NoteItems.Single().Text, "The note's own collection does not show it.");
    }

    private static IReadOnlyList<AgendaItem> Items()
    {
        var agenda = (IAgendaRepository)TestServices.CurrentProvider!.GetService(typeof(IAgendaRepository))!;
        IReadOnlyList<AgendaItem> items = [];
        Pump(async () => items = await agenda.GetAllAsync().ConfigureAwait(true));
        return items;
    }

    private static bool Run(Task<bool> task)
    {
        bool result = false;
        Pump(async () => result = await task.ConfigureAwait(true));
        return result;
    }

    private static void Settle(Control view)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
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
