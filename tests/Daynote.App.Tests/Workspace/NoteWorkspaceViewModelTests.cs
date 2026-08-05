using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class NoteWorkspaceViewModelTests
{
    private static readonly LocalDate Day = WorkspaceTestContext.Date("2026-07-20");

    [TestMethod]
    public async Task EmptyDate_ShowsUnpersistedNote1Projection()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.Notes.LoadAsync(Day);

        Assert.AreEqual(1, context.Notes.Tabs.Count);
        Assert.IsTrue(context.Notes.Tabs[0].IsProjection);
        Assert.AreEqual("노트 1", context.Notes.Tabs[0].Title);
        Assert.IsTrue(context.Notes.ProjectionOnly);

        DayWorkspace persisted = await context.NoteRepository.GetDayWorkspaceStateAsync(Day);
        Assert.IsTrue(persisted.Notes.IsProjectionOnly, "The projection must not create a row until first edit.");
    }

    [TestMethod]
    public async Task FirstEdit_MaterializesAndPersistsTheProjection()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.Notes.LoadAsync(Day);

        context.Notes.EditorText = "First entry";
        FlushResult flush = await context.Notes.FlushAsync(FlushReason.NoteChange);

        Assert.IsTrue(flush.CanProceed);
        DayWorkspace persisted = await context.NoteRepository.GetDayWorkspaceStateAsync(Day);
        Assert.IsFalse(persisted.Notes.IsProjectionOnly);
        Assert.AreEqual("First entry", persisted.Notes.Notes[0].Body);
    }

    [TestMethod]
    public async Task AddDeleteMiddle_KeepsStableIdsAndContiguousOrders()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.Notes.LoadAsync(Day);

        await context.Notes.AddNoteAsync(); // empty day → Note 1
        await context.Notes.AddNoteAsync(); // Note 2
        await context.Notes.AddNoteAsync(); // Note 3
        Assert.AreEqual(3, context.Notes.Tabs.Count);
        NoteId[] before = context.Notes.Tabs.Select(t => t.Id).ToArray();

        await context.Notes.DeleteNoteAsync(context.Notes.Tabs[1]);

        Assert.AreEqual(2, context.Notes.Tabs.Count);
        Assert.AreEqual(0, context.Notes.Tabs[0].SortOrder);
        Assert.AreEqual(1, context.Notes.Tabs[1].SortOrder);
        CollectionAssert.AreEqual(
            new[] { before[0], before[2] },
            context.Notes.Tabs.Select(t => t.Id).ToArray());
    }

    [TestMethod]
    public async Task Reorder_PreservesIdentitiesInNewOrder()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.Notes.LoadAsync(Day);
        await context.Notes.AddNoteAsync(); // empty day → Note 1
        await context.Notes.AddNoteAsync(); // Note 2
        await context.Notes.AddNoteAsync(); // Note 3
        NoteId[] original = context.Notes.Tabs.Select(t => t.Id).ToArray();

        IReadOnlyList<NoteId> reversed = original.Reverse().ToArray();
        bool ok = await context.Notes.ReorderAsync(reversed);

        Assert.IsTrue(ok);
        CollectionAssert.AreEqual(reversed.ToArray(), context.Notes.Tabs.Select(t => t.Id).ToArray());
        Assert.AreEqual(0, context.Notes.Tabs[0].SortOrder);
        Assert.AreEqual(2, context.Notes.Tabs[2].SortOrder);
    }

    [TestMethod]
    public async Task SaveFailure_CancelsNavigationRetainsDirtyTextThenRetrySucceeds()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.Main.InitializeAsync();
        context.Notes.EditorText = "unsaved draft";

        context.NoteRepository.FailSaves = true;
        bool navigated = await context.Main.SelectDateAsync(WorkspaceTestContext.Date("2026-07-21"));

        Assert.IsFalse(navigated, "Navigation must be canceled when the flush fails.");
        Assert.AreEqual(Day, context.Main.SelectedDate);
        Assert.AreEqual("unsaved draft", context.Notes.EditorText);
        Assert.IsTrue(context.Notes.HasSaveError);

        context.NoteRepository.FailSaves = false;
        await context.Notes.RetryCommand.ExecuteAsync(null);

        Assert.IsFalse(context.Notes.HasSaveError);
        DayWorkspace persisted = await context.NoteRepository.GetDayWorkspaceStateAsync(Day);
        Assert.AreEqual("unsaved draft", persisted.Notes.Notes[0].Body);
    }

    [TestMethod]
    public async Task ApplyFormat_WrapsSelectionInMarkdown()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.Notes.LoadAsync(Day);
        context.Notes.EditorText = "bold";

        MarkdownEdit edit = context.Notes.ApplyFormat(MarkdownCommand.Bold, 0, 4);

        Assert.AreEqual("**bold**", context.Notes.EditorText);
        Assert.AreEqual(2, edit.SelectionStart);
        Assert.AreEqual(4, edit.SelectionLength);
    }
}
