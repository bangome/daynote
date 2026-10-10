using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// The to-do sheet: a to-do or an event made in fields of its own. To-dos and notes are separate,
/// so a note never opens it and what it makes points at no note.
/// </summary>
/// <remarks>
/// Opened the way a thumb opens it — the day screen's + 할 일, the 할 일 tab's + — and checked
/// against what reached the agenda store, since that row is what reminders, widgets, sync and the
/// day panel all read.
/// </remarks>
[TestClass]
public sealed class TodoSheetTests
{
    private const string Body = "주간 회의 메모";

    [TestMethod]
    public void The_editor_toolbar_has_only_the_paperclip()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenEditor(view, shell);
            EditorPage editor = view.GetVisualDescendants().OfType<EditorPage>().Single();
            Border toolbar = editor.FindControl<Border>("Toolbar")!;

            Button[] buttons = [.. toolbar.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)];
            Assert.AreSequenceEqual(new[] { "AttachTool" }, buttons.Select(button => button.Name).ToArray(),
                "The editor's toolbar has more than the paperclip: a note never makes a to-do.");
            string[] todoWords = [MobileStrings.Get("MobileTodoAddShort"), MobileStrings.Get("AgendaCaptureTask")];
            Assert.IsFalse(
                editor.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && todoWords.Contains(text.Text)),
                "The editor still offers a to-do.");
        });
    }

    [TestMethod]
    public void The_day_page_add_opens_the_sheet_on_that_day_and_makes_an_item_with_no_note()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            LocalDate other = LocalDates.AddDays(shell.SelectedDate, -4);
            Pump(() => shell.SelectDateAsync(other));
            OpenSheet(view, shell);

            Assert.IsTrue(shell.IsTodoSheetOpen);
            Assert.IsFalse(shell.IsEditorOpen, "Adding a to-do opened a note.");
            Assert.IsFalse(shell.ShowDock);
            Assert.IsTrue(view.FindControl<TextBox>("TodoTextBox")!.IsFocused, "The title does not take the keyboard.");
            Assert.AreEqual(LocalDates.ToDateOnly(other), shell.Entry.Date, "The sheet is not on the day being viewed.");
            Assert.IsTrue(shell.Entry.IsTask, "A to-do is what the sheet starts on.");
            Assert.IsNull(shell.Entry.Time, "A to-do starts with no time.");
            Assert.AreEqual(AgendaList.DefaultId, shell.Entry.ListId);

            shell.Entry.Title = "보고서 보내기";
            AgendaItem made = Add(view, shell);

            Assert.IsNull(made.SourceNoteId, "The item points at a note.");
            Assert.AreEqual(new WallClock(LocalDates.ToDateOnly(other).ToDateTime(TimeOnly.MinValue)), made.DueAt);
            Assert.IsTrue(shell.DayTodos.Any(row => row.Item.Text == "보고서 보내기"), "The day's list does not show it.");
            Assert.IsNull(shell.MadeElsewhereText, "It went to the day on screen.");
        });
    }

    [TestMethod]
    public void Back_closes_the_sheet_and_nothing_under_it()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);

            Assert.IsTrue(Run(shell.GoBackAsync()), "Back did not close the sheet.");
            Assert.IsFalse(shell.IsTodoSheetOpen);
            Assert.IsTrue(shell.IsDayPage);
        });
    }

    [TestMethod]
    public void The_lists_add_defaults_to_today_and_the_list_being_viewed()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            Pump(() => shell.Todo.CreateListAsync("장보기"));
            Pump(() => shell.Todo.RefreshAsync());
            Guid groceries = shell.Todo.Lists.Single(list => list.Name == "장보기").Id;
            Pump(() => shell.SelectDateAsync(LocalDates.AddDays(shell.SelectedDate, 3)));
            shell.GoToPageCommand.Execute(MobilePage.Lists);

            // Making a list lights its chip; start from 전체 and tap it, as a user would.
            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(null));
            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(groceries));
            Assert.AreEqual(groceries, shell.Todo.SelectedListId);
            Settle(view);

            Button add = view.GetVisualDescendants().OfType<ListsPage>().Single()
                .GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddListTodoButton");
            Assert.IsTrue(add.IsEffectivelyVisible && add.IsEffectivelyEnabled, "The 할 일 tab's + cannot be tapped.");
            add.Command!.Execute(add.CommandParameter);
            PumpUntil(() => shell.IsTodoSheetOpen, "The 할 일 tab's + did not open the sheet.");
            Settle(view);

            Assert.AreEqual(groceries, shell.Entry.ListId, "The sheet is not filed in the list being viewed.");
            Assert.IsTrue(shell.Entry.ListOptions.Single(option => option.Id == groceries).IsCurrent);
            Assert.AreEqual(DateOnly.FromDateTime(DateTime.Now), shell.Entry.Date, "The 할 일 tab's + is not on today.");

            shell.Entry.Title = "두부";
            AgendaItem made = Add(view, shell);
            Assert.AreEqual(groceries, made.ListId);
            Assert.IsNull(made.SourceNoteId);
        });
    }

    [TestMethod]
    public void The_lists_add_files_in_the_built_in_list_with_no_filter()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            shell.GoToPageCommand.Execute(MobilePage.Lists);
            Pump(() => shell.AddListTodoCommand.ExecuteAsync(null));

            Assert.IsTrue(shell.IsTodoSheetOpen);
            Assert.AreEqual(AgendaList.DefaultId, shell.Entry.ListId);
        });
    }

    [TestMethod]
    public void Add_waits_for_a_title()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            Button add = view.FindControl<Button>("TodoAdd")!;

            Assert.IsFalse(add.IsEffectivelyEnabled, "추가 is live with nothing to call the item.");
            shell.Entry.Title = "   ";
            Settle(view);
            Assert.IsFalse(add.IsEffectivelyEnabled, "Spaces are not a title.");

            Pump(() => shell.CommitTodoSheetCommand.ExecuteAsync(null));
            Assert.IsEmpty(Items(), "An untitled item was written.");
            Assert.IsTrue(shell.IsTodoSheetOpen);

            shell.Entry.Title = "보고서";
            Settle(view);
            Assert.IsTrue(add.IsEffectivelyEnabled);
        });
    }

    [TestMethod]
    public void A_to_do_with_no_date()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Title = "우유 사기";
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
            AssertNoNote(made);
            Assert.IsNull(shell.MadeElsewhereText, "An undated to-do went to no other day.");
        });
    }

    [TestMethod]
    public void A_to_do_with_a_date_only()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Title = "보고서 보내기";

            AgendaItem made = Add(view, shell);

            DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
            Assert.AreEqual(new WallClock(day.ToDateTime(TimeOnly.MinValue)), made.DueAt);
            Assert.IsFalse(made.HasDueTime, "No time was given.");
            Assert.IsNull(made.StartsAt);
            Assert.AreSequenceEqual(AgendaAlert.Default.ToArray(), made.AlarmLeadMinutes.ToArray(),
                "A dated to-do starts with its one alert, as an @ one does.");
            AssertNoNote(made);
        });
    }

    [TestMethod]
    public void A_to_do_with_a_date_and_a_time_on_another_day()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Title = "업체에 전화";

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
            AssertNoNote(made);
            Assert.IsNotNull(shell.MadeElsewhereText, "It went to another day; the tablet's line should say so.");
            Assert.Contains(target.ToString(MobileStrings.Get("MobileTodoAddedDateFormat"),
                Daynote.App.Localization.LocalizationService.Instance.Culture), shell.MadeElsewhereText);
        });
    }

    [TestMethod]
    public void An_event_with_a_start_and_an_end()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Title = "디자인 리뷰";

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
            AssertNoNote(made);
        });
    }

    [TestMethod]
    public void Switching_back_to_a_to_do_takes_away_the_time_it_filled_in()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
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
            OpenSheet(view, shell);
            Assert.IsTrue(shell.Entry.ShowLists);

            TodoListOption groceries = shell.Entry.ListOptions.Single(option => option.Name == "장보기");
            shell.Entry.PickListCommand.Execute(groceries.Id);
            shell.Entry.Title = "두부";

            Assert.AreEqual(groceries.Id, Add(view, shell).ListId);
        });
    }

    [TestMethod]
    public void The_widget_capture_link_opens_the_sheet_over_today_s_day_page()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            LocalDate today = shell.SelectedDate;
            Pump(() => shell.SelectDateAsync(LocalDates.AddDays(today, -2)));
            shell.GoToPageCommand.Execute(MobilePage.Lists);
            Pump(() => shell.OpenLinkAsync(new Uri("daynote://capture?at=1")));
            Settle(view);

            Assert.IsTrue(shell.IsTodoSheetOpen, "The link did not open the sheet.");
            Assert.IsFalse(shell.IsEditorOpen, "The link opened a note.");
            Assert.IsTrue(shell.IsDayPage, "The sheet is not over the day page.");
            Assert.AreEqual(today, shell.SelectedDate, "The day page is not on today.");
            Assert.AreEqual(LocalDates.ToDateOnly(today), shell.Entry.Date);

            shell.CloseTodoSheetCommand.Execute(null);
            Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
            Pump(() => shell.OpenFromWidgetAsync(WidgetLaunch.Capture));
            Settle(view);

            Assert.IsFalse(shell.IsEditorOpen, "The widget's @ 할 일 left a note open under the sheet.");
            Assert.IsTrue(shell.IsTodoSheetOpen, "The Android widget's @ 할 일 did not open the sheet.");
            Assert.IsTrue(shell.IsDayPage);

            shell.Entry.Title = "위젯에서";
            Assert.IsNull(Add(view, shell).SourceNoteId);
        });
    }

    [TestMethod]
    public void The_widget_new_note_still_opens_a_new_note()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            Pump(() => shell.OpenFromWidgetAsync(WidgetLaunch.NewNote));
            Settle(view);

            Assert.IsTrue(shell.IsEditorOpen);
            Assert.IsFalse(shell.IsTodoSheetOpen);
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

    // ── @ in a note is just a character ──────────────────────────────────────────

    [TestMethod]
    [DataRow("", DisplayName = "at the start of the note")]
    [DataRow("\n", DisplayName = "after a newline")]
    [DataRow("\n  ", DisplayName = "after a newline and spaces")]
    public void An_at_typed_at_the_start_of_a_line_opens_nothing_and_stays_in_the_body(string lead)
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);
            string before = lead.Length == 0 ? string.Empty : Body + lead;
            body.Text = before;
            Settle(view);

            TypeAt(view, body, before.Length);

            Assert.IsFalse(shell.IsTodoSheetOpen, "An @ in the note opened the to-do sheet.");
            Assert.AreEqual(before + "@", body.Text, "The @ did not stay in the note.");
            Assert.AreEqual(before + "@", shell.Notes.EditorText);
            Assert.IsEmpty(Items(), "Typing in a note made a to-do.");
        });
    }

    [TestMethod]
    [DataRow(8, DisplayName = "at the end of a line")]
    [DataRow(2, DisplayName = "inside a word")]
    public void An_at_typed_inside_a_line_is_just_a_character(int caret)
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            TextBox body = OpenEditor(view, shell);

            TypeAt(view, body, caret);

            Assert.IsFalse(shell.IsTodoSheetOpen, "An @ in the middle of a line opened the sheet.");
            Assert.AreEqual(Body.Insert(caret, "@"), body.Text);
        });
    }

    // ── 제목, 내용, 반복 ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void The_title_and_the_description_are_saved_apart()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Title = "  회의자료 초안 공유 ";
            shell.Entry.Description = "슬라이드 12장\n예산표 첨부\n";

            AgendaItem made = Add(view, shell);

            Assert.AreEqual("회의자료 초안 공유", made.Title);
            Assert.AreEqual("슬라이드 12장\n예산표 첨부", made.Description);
            AssertNoNote(made);
        });
    }

    [TestMethod]
    [DataRow(TodoRepeat.None, "", "2026-10-07", "2026-10-08,2026-10-14")]
    [DataRow(TodoRepeat.Daily, "FREQ=DAILY", "2026-10-07,2026-10-08,2026-10-11", "2026-10-06")]
    [DataRow(TodoRepeat.Weekdays, "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "2026-10-07,2026-10-09,2026-10-12", "2026-10-10,2026-10-11")]
    [DataRow(TodoRepeat.Weekly, "FREQ=WEEKLY", "2026-10-07,2026-10-14,2026-10-21", "2026-10-08,2026-10-13")]
    [DataRow(TodoRepeat.Monthly, "FREQ=MONTHLY", "2026-10-07,2026-11-07,2027-01-07", "2026-10-08,2026-11-08")]
    [DataRow(TodoRepeat.Yearly, "FREQ=YEARLY", "2026-10-07,2027-10-07", "2026-11-07,2027-10-08")]
    public void Each_repeat_writes_its_rule_and_lands_on_its_days(TodoRepeat repeat, string rule, string on, string off)
    {
        // 7 October 2026, a Wednesday. An empty rule is no repeat: the one day and no other.
        string? rrule = rule.Length > 0 ? rule : null;
        var anchor = new DateOnly(2026, 10, 7);
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Title = "스트레칭";
            shell.Entry.Date = anchor;
            Pump(() => shell.Entry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Repeat));
            Assert.IsTrue(shell.Entry.IsRepeatPickerOpen);
            shell.Entry.PickRepeatCommand.Execute(repeat);
            Assert.IsFalse(shell.Entry.IsRepeatPickerOpen, "Picking a repeat closes its row.");
            Assert.AreEqual(repeat, shell.Entry.Repeat);

            AgendaItem made = Add(view, shell);

            Assert.AreEqual(rrule, made.Rrule);
            Assert.AreEqual(new WallClock(anchor.ToDateTime(TimeOnly.MinValue)), made.DueAt);
            if (rrule is not null)
            {
                // A repeating to-do anchors its rule in DTSTART, as @ 매일 does (AgendaItem.Anchor).
                Assert.AreEqual(made.DueAt, made.StartsAt);
                Assert.IsTrue(AgendaRecurrence.CanExpand(rrule), "The rule is one the expander cannot read.");
            }

            foreach (string day in on.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.HasCount(1, AgendaDay.For(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), [made]).Open,
                    $"{repeat} is not on {day}.");
            }

            foreach (string day in off.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.IsEmpty(AgendaDay.For(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), [made]).Open,
                    $"{repeat} is on {day}.");
            }
        });
    }

    [TestMethod]
    public void The_repeat_row_spells_the_repeat_out_against_the_day()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Date = new DateOnly(2026, 10, 9);
            Assert.IsFalse(shell.Entry.Repeats);
            Assert.AreEqual(MobileStrings.Get("MobileTodoRepeatNone"), shell.Entry.RepeatText);

            shell.Entry.PickRepeatCommand.Execute(TodoRepeat.Weekly);
            Assert.Contains(
                Daynote.App.Localization.LocalizationService.Instance.Culture.DateTimeFormat.GetDayName(DayOfWeek.Friday),
                shell.Entry.RepeatText);
            shell.Entry.PickRepeatCommand.Execute(TodoRepeat.Monthly);
            Assert.Contains("9", shell.Entry.RepeatText);
        });
    }

    [TestMethod]
    public void A_repeat_needs_a_date()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            OpenSheet(view, shell);
            shell.Entry.Title = "물 마시기";
            shell.Entry.PickRepeatCommand.Execute(TodoRepeat.Daily);

            // 날짜 없음 takes the repeat with it, and the row is greyed until there is a day again.
            shell.Entry.ClearDateCommand.Execute(null);
            Assert.AreEqual(TodoRepeat.None, shell.Entry.Repeat);
            Assert.IsFalse(shell.Entry.CanRepeat);
            Settle(view);
            Assert.IsFalse(view.FindControl<Button>("TodoRepeatRow")!.IsEffectivelyEnabled, "The 반복 row is live with no date.");
            Assert.AreEqual(MobileStrings.Get("MobileTodoRepeatNeedsDate"), shell.Entry.RepeatText);

            shell.Entry.PickRepeatCommand.Execute(TodoRepeat.Weekly);
            Pump(() => shell.Entry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Repeat));
            Assert.AreEqual(TodoRepeat.None, shell.Entry.Repeat, "A repeat was set with no day to count from.");
            Assert.IsFalse(shell.Entry.IsRepeatPickerOpen);

            AgendaItem made = Add(view, shell);
            Assert.IsNull(made.Rrule);
            Assert.IsNull(made.Anchor);
        });
    }

    /// <summary>
    /// Types one @ into the body at <paramref name="caret"/>, through the window's text input as a
    /// keyboard would, and lets whatever it sets off finish.
    /// </summary>
    internal static void TypeAt(Control view, TextBox body, int caret)
    {
        body.Focus();
        body.CaretIndex = caret;
        Settle(view);
        TopLevel.GetTopLevel(view)!.KeyTextInput("@");

        // Opening the sheet reads the month off the database, which resumes here more than once.
        for (int i = 0; i < 40; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        Settle(view);
    }

    private static TextBox OpenEditor(MainView view, MobileShellViewModel shell)
    {
        Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
        TextBox body = view.GetVisualDescendants().OfType<EditorPage>().Single()
            .GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "Body");
        body.Text = Body;
        body.CaretIndex = Body.Length;
        Settle(view);
        return body;
    }

    /// <summary>Taps the day screen's + 할 일: its command, once it is on screen and live.</summary>
    internal static void OpenSheet(MainView view, MobileShellViewModel shell)
    {
        Settle(view);
        Button add = view.GetVisualDescendants().OfType<DayPage>().Single()
            .GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddTodoButton");
        Assert.IsTrue(add.IsEffectivelyVisible && add.IsEffectivelyEnabled, "The day's + 할 일 cannot be tapped.");
        add.Command!.Execute(add.CommandParameter);
        PumpUntil(() => shell.IsTodoSheetOpen, "The day's + 할 일 did not open the sheet.");
        Settle(view);
    }

    /// <summary>Taps 추가 and answers with the one row it wrote.</summary>
    private static AgendaItem Add(MainView view, MobileShellViewModel shell)
    {
        Button add = view.FindControl<Button>("TodoAdd")!;
        Settle(view);
        Assert.IsTrue(add.IsEffectivelyEnabled, "추가 is not live.");
        add.Command!.Execute(add.CommandParameter);
        PumpUntil(() => !shell.IsTodoSheetOpen && Items().Count == 1,
            "추가 did not write the item and close the sheet.");
        return Items().Single();
    }

    private static void AssertNoNote(AgendaItem made) =>
        Assert.IsNull(made.SourceNoteId, "The item points at a note: to-dos and notes are separate.");

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
