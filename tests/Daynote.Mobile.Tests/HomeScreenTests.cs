using Daynote.Core.Agenda;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The home screen and the lists built from the same parse of the notes: the week strip, the note
/// cards, the day's to-dos, the Lists page's bands and tags, recent searches, and the back gesture.
/// </summary>
[TestClass]
public sealed class HomeScreenTests
{
    [TestMethod]
    public void The_week_strip_is_the_selected_days_week_from_Sunday()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Assert.HasCount(7, shell.Week);
            Assert.AreEqual(DayOfWeek.Sunday, LocalDates.ToDateOnly(shell.Week[0].Date).DayOfWeek);
            Assert.AreEqual(shell.SelectedDate, shell.Week.Single(d => d.IsSelected).Date);
            Assert.IsFalse(shell.Week.Any(d => d.IsTodayRing), "Today is selected, so it needs no ring as well.");
            Assert.IsTrue(shell.IsTodaySelected);

            LocalDate before = shell.SelectedDate;
            Pump(() => shell.NextWeekCommand.ExecuteAsync(null));

            Assert.AreEqual(LocalDates.AddDays(before, 7), shell.SelectedDate);
            Assert.IsFalse(shell.IsTodaySelected);
            Assert.IsTrue(shell.Week.Single(d => d.IsSelected).Date == shell.SelectedDate);
            Assert.IsFalse(shell.Week.Any(d => d.IsTodayRing), "Next week does not contain today.");

            Pump(() => shell.PreviousWeekCommand.ExecuteAsync(null));
            Assert.AreEqual(before, shell.SelectedDate);
        });
    }

    [TestMethod]
    public void A_horizontal_week_strip_swipe_pages_exactly_one_week()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            var page = new DayPage { DataContext = shell };
            Grid strip = page.FindControl<Grid>("WeekStrip")!;
            LocalDate before = shell.SelectedDate;

            // Avalonia reports content delta (previous pointer minus current pointer), so a finger
            // moving left produces a positive X delta and SwipeDirection.Left.
            strip.RaiseEvent(new SwipeGestureEventArgs(1, new Vector(80, 0), new Vector(500, 0))
            {
                RoutedEvent = InputElement.SwipeGestureEvent,
            });
            Pump(() => shell.NextWeekCommand.ExecutionTask ?? Task.CompletedTask);
            Assert.AreEqual(LocalDates.AddDays(before, 7), shell.SelectedDate);

            strip.RaiseEvent(new SwipeGestureEventArgs(1, new Vector(20, 0), new Vector(200, 0))
            {
                RoutedEvent = InputElement.SwipeGestureEvent,
            });
            Assert.AreEqual(LocalDates.AddDays(before, 7), shell.SelectedDate, "One swipe paged more than once.");

            strip.RaiseEvent(new SwipeGestureEndedEventArgs(1, new Vector(500, 0))
            {
                RoutedEvent = InputElement.SwipeGestureEndedEvent,
            });
            strip.RaiseEvent(new SwipeGestureEventArgs(2, new Vector(-80, 0), new Vector(-500, 0))
            {
                RoutedEvent = InputElement.SwipeGestureEvent,
            });
            Pump(() => shell.PreviousWeekCommand.ExecutionTask ?? Task.CompletedTask);
            Assert.AreEqual(before, shell.SelectedDate);
        });
    }

    [TestMethod]
    public void A_note_becomes_a_card_with_its_preview_progress_and_dot()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Assert.IsTrue(shell.IsDayEmpty);
            Assert.IsEmpty(shell.DayCards);

            WriteNote(shell, "회의", "안건 정리\n\n-[] 자료 공유 (9/1 10:00)\n-[x] 예약\n-[] 메모");

            DayNoteCardViewModel card = shell.DayCards.Single();
            Assert.AreEqual("회의", card.Tab.Title);
            Assert.AreEqual("안건 정리 · -[] 자료 공유 (9/1 10:00) · -[x] 예약", card.Preview);
            // The body is still exactly what was typed, and a `-[]` in it is now text that looks
            // like a checkbox — the panels read to-do entities. The one-time migration carries an
            // existing user's lines across; a line typed afterwards is prose.
            Assert.AreEqual(0, card.TodoTotal);
            Assert.IsTrue(shell.Week.Single(d => d.IsSelected).HasNotes, "The day's dot did not appear.");
            Assert.IsFalse(shell.IsDayEmpty);
        });
    }

    [TestMethod]
    public void A_cards_progress_counts_the_to_dos_captured_from_that_note()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            WriteNote(shell, "회의", "안건 정리");
            Guid note = shell.Notes.SelectedTab!.Id.Value;

            AddTodo(shell, note, "자료 공유", done: false);
            AddTodo(shell, note, "예약", done: true);
            AddTodo(shell, note, "메모", done: false);

            DayNoteCardViewModel card = shell.DayCards.Single();
            Assert.AreEqual(1, card.TodoDone);
            Assert.AreEqual(3, card.TodoTotal);
            Assert.AreEqual("1 / 3", card.TodoText);
        });
    }

    [TestMethod]
    public void The_days_todos_count_and_toggle_without_opening_the_note()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            WriteNote(shell, "할 일", "메모");
            Guid note = shell.Notes.SelectedTab!.Id.Value;
            AddTodo(shell, note, "하나", done: false);
            AddTodo(shell, note, "둘", done: true);

            Assert.IsTrue(shell.HasDayTodos);
            Assert.HasCount(2, shell.DayTodos);
            Assert.AreEqual("1/2", shell.DayTodoCountText);

            TodoRowViewModel open = shell.DayTodos.First(row => !row.Item.Checked);
            Pump(() => open.Item.ToggleCommand.ExecuteAsync(null));

            Assert.AreEqual("2/2", shell.DayTodoCountText, "Ticking a to-do on the home screen did not stick.");
            Assert.AreEqual(2, shell.DayCards.Single().TodoDone, "The card's progress did not follow.");
            Assert.IsFalse(shell.IsEditorOpen, "Ticking a to-do opened the note.");
        });
    }

    [TestMethod]
    public void To_dos_fall_into_their_bands()
    {
        // A to-do is an entity now rather than a parsed line, so a band is decided by its status
        // and the day it is owed on rather than by flags the parser worked out.

        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9));

        Assert.AreEqual(TodoGroupKind.Done, TodoGroupViewModel.KindOf(Row(true, now.AddDays(-1)), now));
        Assert.AreEqual(TodoGroupKind.Overdue, TodoGroupViewModel.KindOf(Row(false, now.AddHours(-1)), now));
        Assert.AreEqual(TodoGroupKind.Today, TodoGroupViewModel.KindOf(Row(false, now.AddHours(5)), now));
        Assert.AreEqual(TodoGroupKind.Upcoming, TodoGroupViewModel.KindOf(Row(false, now.AddDays(2)), now));
        Assert.AreEqual(TodoGroupKind.NoDate, TodoGroupViewModel.KindOf(Row(false, null), now));

        IReadOnlyList<TodoGroupViewModel> groups = TodoGroupViewModel.Build(
            [Row(false, null), Row(true, null), Row(false, now.AddHours(-1))],
            now,
            row => new TodoRowViewModel(
                new Daynote.App.Shell.Product.TodoItemViewModel(
                    row,
                    Daynote.App.Shell.Product.TodoRowScope.AllDates,
                    string.Empty,
                    now.DateTime,
                    _ => Task.CompletedTask,
                    _ => Task.CompletedTask),
                row),
            kind => kind.ToString());

        CollectionAssert.AreEqual(
            new[] { TodoGroupKind.Overdue, TodoGroupKind.NoDate, TodoGroupKind.Done },
            groups.Select(g => g.Kind).ToArray(),
            "The bands are out of order, or an empty one was kept.");
    }

    [TestMethod]
    public void The_chosen_tag_survives_a_refresh()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            WriteNote(shell, "하나", "본문", "가", "나");
            WriteNote(shell, "둘", "본문", "나");

            Assert.IsNotNull(shell.SelectedTag, "No tag is chosen, so the tag tab would open on nothing.");
            Assert.AreEqual("나", shell.TagChips[0].Name, "The most used tag is not first.");
            TagChipViewModel other = shell.TagChips.Single(chip => chip.Name == "가");
            other.SelectCommand.Execute(null);
            Assert.IsTrue(other.IsSelected);
            Assert.AreEqual("하나", shell.SelectedTag!.Notes.Single().Title);

            Pump(() => shell.RefreshAllAsync());

            Assert.AreEqual("가", shell.SelectedTag?.Name, "A refresh put the tag choice back.");
            Assert.HasCount(1, shell.TagChips.Where(chip => chip.IsSelected));
        });
    }

    [TestMethod]
    public void A_search_that_opens_a_note_is_remembered()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            WriteNote(shell, "배포", "스테이징 확인");
            shell.GoToPageCommand.Execute(MobilePage.Search);
            shell.Search.Query = "스테이징";
            Pump(() => shell.Search.SearchNowAsync("스테이징"));
            Assert.IsNotEmpty(shell.Search.Results);
            Assert.AreEqual(MobileCatalog.ResultCount(shell.Search.Results.Count), shell.SearchResultCountText);

            Pump(() => shell.Search.Results.First().ActivateCommand.ExecuteAsync(null));

            Assert.AreEqual("스테이징", shell.RecentSearches.FirstOrDefault());
            Assert.IsTrue(shell.IsEditorOpen, "The result did not open its note.");
            Assert.AreEqual(MobilePage.Search, shell.Page, "Opening a result left the search page behind the editor.");

            shell.UseSearchTermCommand.Execute("#태그");
            Assert.AreEqual("태그", shell.Search.Query, "A tag shortcut searched with its hash.");
        });
    }

    [TestMethod]
    public void Back_closes_the_topmost_layer_first()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
            Assert.IsFalse(shell.ShowDock, "The tab bar shows over the editor.");
            shell.OpenMonthPickerCommand.Execute(null);

            Assert.IsTrue(Run(shell.GoBackAsync()));
            Assert.IsFalse(shell.IsMonthPickerOpen);
            Assert.IsTrue(shell.IsEditorOpen, "Back closed the editor along with the sheet.");

            Assert.IsTrue(Run(shell.GoBackAsync()));
            Assert.IsFalse(shell.IsEditorOpen);
            Assert.IsTrue(shell.ShowDock);

            Assert.IsFalse(Run(shell.GoBackAsync()), "Back claimed the gesture with nothing left to close.");
        });
    }

    [TestMethod]
    public void Every_phone_label_is_in_both_languages()
    {
        CollectionAssert.AreEquivalent(
            MobileCatalog.Korean.Keys.ToArray(),
            MobileCatalog.English.Keys.ToArray(),
            "A phone label is missing from one language.");
        Assert.AreEqual("1 note", WithLanguage(AppLanguage.English, () => MobileCatalog.NoteCount(1)));
        Assert.AreEqual("노트 3개", WithLanguage(AppLanguage.Korean, () => MobileCatalog.NoteCount(3)));
    }

    private static string WithLanguage(AppLanguage language, Func<string> read)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            LocalizationService.Instance.SetLanguage(language);
            return read();
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    /// <summary>Writes a note on the selected day through the editor, the way a user would.</summary>
    /// <summary>
    /// Captures a to-do against a note, the way the @ command will. Written straight through the
    /// repository because the phone's own capture bar is not built yet (phone §01).
    /// </summary>
    private static void AddTodo(MobileShellViewModel shell, Guid note, string title, bool done)
    {
        var agenda = (IAgendaRepository)TestServices.CurrentProvider!.GetService(typeof(IAgendaRepository))!;
        DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
        var item = new AgendaItem(
            Guid.NewGuid(),
            AgendaList.DefaultId,
            AgendaKind.Task,
            title,
            string.Empty,
            "Asia/Seoul",
            StartsAt: null,
            EndsAt: null,
            DueAt: new WallClock(day.ToDateTime(new TimeOnly(10, 0))),
            HasDueTime: true,
            Rrule: null,
            SeriesId: null,
            RecurrenceId: null,
            done ? AgendaStatus.Completed : AgendaStatus.NeedsAction,
            CompletedUtc: done ? DateTimeOffset.UtcNow : null,
            Priority: 0,
            TimelineVisibility.Auto,
            SourceNoteId: note,
            ExceptionDates: [],
            AgendaAlert.Default,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Pump(() => agenda.SaveAsync(item).AsTask());
        Pump(shell.RefreshAllAsync);
    }

    private static void WriteNote(MobileShellViewModel shell, string title, string body, params string[] tags)
    {
        Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
        shell.Notes.EditorText = body;
        Pump(() => shell.Notes.FlushAsync(FlushReason.NoteChange));
        Pump(() => shell.Notes.RenameAsync(shell.Notes.SelectedTab!, title));
        foreach (string tag in tags)
        {
            shell.TagInput = tag;
            Pump(() => shell.CommitTagCommand.ExecuteAsync(null));
        }

        Pump(() => shell.CloseEditorAsync());
    }

    private static bool Run(Task<bool> task)
    {
        bool result = false;
        Pump(async () => result = await task.ConfigureAwait(true));
        return result;
    }

    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The command did not complete within 20 seconds.");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A to-do owed at <paramref name="due"/>, or owed with no day when it is null.</summary>
    private static AgendaDayRow Row(bool done, DateTimeOffset? due)
    {
        var item = new AgendaItem(
            Guid.NewGuid(),
            AgendaList.DefaultId,
            AgendaKind.Task,
            "t",
            string.Empty,
            "Asia/Seoul",
            StartsAt: null,
            EndsAt: null,
            DueAt: due is { } at ? new WallClock(at.DateTime) : null,
            HasDueTime: due is not null,
            Rrule: null,
            SeriesId: null,
            RecurrenceId: null,
            done ? AgendaStatus.Completed : AgendaStatus.NeedsAction,
            CompletedUtc: null,
            Priority: 0,
            TimelineVisibility.Auto,
            SourceNoteId: null,
            ExceptionDates: [],
            AgendaAlert.Default,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        return new AgendaDayRow(item, null, item.DueAt);
    }
}
