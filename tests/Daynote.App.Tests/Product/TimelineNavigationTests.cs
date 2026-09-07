using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Navigating out of the timeline.
/// </summary>
/// <remarks>
/// The timeline is a view of every note, and both side panels stay on screen while it is open — so a
/// click in either one is a request to look at something, and leaving the timeline up would mean the
/// user picked a note and nothing appeared to happen. Only the row inside the timeline itself used to
/// close it.
/// <para>
/// Three routes in, and they are not the same code path: the calendar and the right-hand panels
/// change the date and funnel through <c>SelectDateAsync</c>, while the day-note list picks a note on
/// the date already selected and never touches it.
/// </para>
/// </remarks>
[TestClass]
public sealed class TimelineNavigationTests
{
    private static readonly LocalDate Today = LocalDate.Parse("2026-07-20").Value;
    private static readonly LocalDate Earlier = LocalDate.Parse("2026-07-14").Value;

    [TestMethod]
    public async Task A_calendar_day_leaves_the_timeline_and_shows_that_day()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "오늘", "본문");
        await context.StoreNoteAsync(Earlier, "지난주", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);
        Assert.IsTrue(harness.Shell.IsTimelineMode, "The timeline did not open.");

        CalendarDayCellViewModel cell = harness.Shell.Calendar.Cells.Single(c => c.IsInMonth && c.Date == Earlier);
        await cell.SelectCommand.ExecuteAsync(null);

        Assert.IsFalse(harness.Shell.IsTimelineMode, "Picking a day left the timeline open.");
        Assert.AreEqual(Earlier, harness.Shell.SelectedDate);
    }

    [TestMethod]
    public async Task The_day_already_selected_still_leaves_the_timeline()
    {
        // The early return for "same date" skipped the exit, so the one day you could not get back to
        // from the calendar was the one you were already on.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "오늘", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);
        CalendarDayCellViewModel cell = harness.Shell.Calendar.Cells.Single(c => c.IsInMonth && c.Date == Today);
        await cell.SelectCommand.ExecuteAsync(null);

        Assert.IsFalse(harness.Shell.IsTimelineMode);
        Assert.AreEqual(Today, harness.Shell.SelectedDate);
    }

    [TestMethod]
    public async Task A_todo_row_in_the_right_panel_leaves_the_timeline()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Earlier, "할 일", "-[] 장부 정리");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);
        TodoItemViewModel todo = harness.Shell.Todo.Items.First();
        await todo.JumpCommand.ExecuteAsync(null);

        Assert.IsFalse(harness.Shell.IsTimelineMode, "A todo row left the timeline open.");
        Assert.AreEqual(Earlier, harness.Shell.SelectedDate);
    }

    [TestMethod]
    public async Task A_note_in_the_day_list_leaves_the_timeline()
    {
        // This one never changes the date, so it does not pass through SelectDateAsync and needs its
        // own way out.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        // StoreNotesOnDateAsync, not two StoreNoteAsync calls: the second would save as another
        // first edit for the same day and the repository rejects that as a conflict.
        await context.StoreNotesOnDateAsync(Today, ("첫째", "본문"), ("둘째", "본문"));
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);
        Assert.IsTrue(harness.Shell.IsTimelineMode);

        // The note that is not already open, so the selection has to actually move. Held by id, not
        // by reference: opening the timeline flushes the workspace, which rebuilds the tab list, so
        // the instance in hand can be replaced by an equal one for the same note.
        Notes.NoteTabViewModel target = harness.Shell.Notes.Tabs
            .First(t => !t.IsProjection && t.Id != harness.Shell.Notes.SelectedTab?.Id);
        NoteId targetId = target.Id;

        await harness.Shell.SelectDayNoteCommand.ExecuteAsync(target);

        Assert.IsFalse(harness.Shell.IsTimelineMode, "Picking a note from the day list left the timeline open.");
        Assert.AreEqual(targetId, harness.Shell.Notes.SelectedTab?.Id);
    }

    [TestMethod]
    public async Task Opening_the_timeline_is_still_possible_after_leaving_it()
    {
        // The exit sits in the shared funnel, and the funnel runs while the timeline is being opened
        // too (it loads the day first). A regression there would make the button do nothing.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "오늘", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);
        CalendarDayCellViewModel cell = harness.Shell.Calendar.Cells.Single(c => c.IsInMonth && c.Date == Today);
        await cell.SelectCommand.ExecuteAsync(null);
        await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);

        Assert.IsTrue(harness.Shell.IsTimelineMode, "The timeline could not be reopened.");
    }
}
