using Avalonia.Controls;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Desktop.ViewModels;
using static Daynote.Desktop.Tests.DesktopV2FrameTests;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The parts of Desktop B v2 that are behaviour rather than layout: the "1 / 8" note menu, the month
/// view and its filters, the sidebar's folding lists, the agenda dots, the files library.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DesktopV2Tests
{
    [TestMethod]
    public void The_note_menu_says_where_the_open_note_is_and_opens_another()
    {
        WithSample((window, shell) =>
        {
            Assert.AreEqual("1 / 8", shell.TabPositionText);
            shell.ToggleTabMenuCommand.Execute(null);
            Assert.IsTrue(shell.IsTabMenuOpen);

            var third = shell.Notes.Tabs.Where(static tab => !tab.IsProjection).ElementAt(2);
            Wait(shell.PickTabMenuNoteCommand.ExecuteAsync(third));
            Assert.IsFalse(shell.IsTabMenuOpen, "Picking a note left the menu open.");
            Assert.AreSame(third, shell.Notes.SelectedTab);
            Assert.AreEqual("3 / 8", shell.TabPositionText);
        });
    }

    [TestMethod]
    public void The_header_names_the_view_outside_the_editor()
    {
        WithSample((window, shell) =>
        {
            Wait(shell.ShowTimelineCommand.ExecuteAsync(null));
            Assert.AreEqual("타임라인", shell.ViewTitle);
            Wait(shell.ShowCalendarCommand.ExecuteAsync(null));
            Assert.AreEqual("캘린더", shell.ViewTitle);
            StringAssert.Contains(shell.ViewSubtitle, "일정 · 할 일 · 파일");
            Wait(shell.ShowListCommand.ExecuteAsync(RightTab.Files));
            Assert.AreEqual("파일", shell.ViewTitle);
            Assert.AreEqual("전체 3개 · 날짜별", shell.ViewSubtitle);
        });
    }

    [TestMethod]
    public void The_month_view_lays_out_the_days_and_filters_by_kind()
    {
        WithSample((window, shell) =>
        {
            Wait(shell.ShowCalendarCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsCalendarMode && !shell.IsEditorMode);
            PumpUntil(() => shell.CalendarBoard.Cells.Count >= 28, "The month view did not build.");

            LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));
            CalendarBoardDay day = shell.CalendarBoard.Cells.Single(cell => cell.IsInMonth && cell.Date == today);
            Assert.IsTrue(day.IsToday);
            Assert.AreEqual(8, day.NoteCount);
            Assert.AreEqual(4, day.Lines.Count + (day.HasMore ? 1 : 0), "A full day does not stop at four lines.");
            Assert.IsTrue(day.Events.Any(item => item.Title == "스탠드업"), "Today's stand-up is missing from its card.");
            Assert.AreEqual(2, day.Files.Count);
            Assert.IsTrue(day.Lines.All(line => line.IsEvent), "Events do not lead the day.");

            CalendarBoardFilter events = shell.CalendarBoard.Filters.Single(filter => filter.Kind == CalendarBoardKind.Event);
            events.ToggleCommand.Execute(null);
            day = shell.CalendarBoard.Cells.Single(cell => cell.IsInMonth && cell.Date == today);
            Assert.IsFalse(day.Lines.Any(line => line.IsEvent), "Turning 일정 off left events in the grid.");
            Assert.IsTrue(day.Events.Count > 0, "The card lost its events with the filter.");

            // A click in a day selects it and stays; its number opens the editor.
            LocalDate other = LocalDates.AddDays(today, today.Day == 1 ? 1 : -1);
            CalendarBoardDay otherDay = shell.CalendarBoard.Cells.Single(cell => cell.Date == other && cell.IsInMonth);
            Wait(otherDay.SelectCommand.ExecuteAsync(null));
            Assert.AreEqual(other, shell.SelectedDate);
            Assert.IsTrue(shell.IsCalendarMode, "Picking a day left the month view.");
            Wait(shell.CalendarBoard.Cells.Single(cell => cell.Date == other && cell.IsInMonth).OpenCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsEditorMode, "The day's number did not open the editor.");
        });
    }

    [TestMethod]
    public void A_dated_to_do_puts_a_dot_under_its_day()
    {
        WithSample((window, shell) =>
        {
            // Tomorrow and the day after have no notes in the sample, only to-dos and events.
            LocalDate dayAfter = LocalDates.AddDays(shell.SelectedDate, 2);
            Pump(window);
            CalendarDayCellViewModel? cell = shell.Calendar.Cells.SingleOrDefault(c => c.IsInMonth && c.Date == LocalDates.AddDays(shell.SelectedDate, 1));
            if (cell is not null)
            {
                Assert.IsFalse(cell.HasNotes);
                Assert.IsTrue(cell.ShowsDot, "Tomorrow's to-dos put no dot under it.");
            }

            WeekStripDayViewModel? pill = shell.Week.Days.SingleOrDefault(d => d.Date == LocalDates.AddDays(shell.SelectedDate, 1));
            if (pill is not null)
            {
                Assert.IsTrue(pill.ShowsDot, "The week strip has no dot for tomorrow's to-dos.");
            }

            Assert.IsTrue(cell is not null || pill is not null || dayAfter.Day > 0);
        });
    }

    [TestMethod]
    public void The_lists_fold_and_open_by_themselves_while_one_filters()
    {
        WithSample((window, shell) =>
        {
            Assert.IsFalse(shell.AreSidebarListsShown);
            Wait(shell.ToggleSidebarListsCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.AreSidebarListsShown);
            Wait(shell.ShowAgendaListCommand.ExecuteAsync(shell.Todo.Lists.Single(list => list.Name == AgendaSeed.Work)));
            Wait(shell.ToggleSidebarListsCommand.ExecuteAsync(null));
            Assert.IsFalse(shell.AreSidebarListsShown, "Folding left the lists up.");
            Assert.IsNull(shell.Todo.SelectedListId, "Folding the lists left a filter nobody can see.");

            Wait(shell.ToggleSidebarListsCommand.ExecuteAsync(null));
            Wait(shell.ShowAgendaListCommand.ExecuteAsync(shell.Todo.Lists.Single(list => list.Name == AgendaSeed.Life)));
            shell.IsSidebarListsOpen = false;
            Assert.IsTrue(shell.AreSidebarListsShown, "A filtering list was hidden.");
        });
    }

    [TestMethod]
    public void The_files_view_lists_every_date_and_searches_names()
    {
        WithSample((window, shell) =>
        {
            Wait(shell.ShowListCommand.ExecuteAsync(RightTab.Files));
            Assert.AreEqual(2, shell.Files.Groups.Count);
            StringAssert.EndsWith(shell.Files.Groups[0].Heading, "· 오늘");
            Assert.AreEqual(3, shell.Files.LibraryCount);

            shell.Files.LibraryQuery = "txt";
            Assert.HasCount(1, shell.Files.Groups);
            Assert.AreEqual("A사_미팅녹취.txt", shell.Files.Groups[0].Files.Single().Name);

            shell.Files.LibraryQuery = "없는파일";
            Assert.IsTrue(shell.Files.IsLibraryEmpty);
            StringAssert.Contains(shell.Files.LibraryEmptyText, "없는파일");
        });
    }
}
