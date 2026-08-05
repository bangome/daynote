using Daynote.App.Search;
using Daynote.App.Shell;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class WorkspaceSearchNavigationTests
{
    private static readonly LocalDate Day = WorkspaceTestContext.Date("2026-07-20");
    private static readonly LocalDate Other = WorkspaceTestContext.Date("2026-07-18");

    [TestMethod]
    public async Task NoteResult_SelectsExactDateAndNoteAfterReorder()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        IReadOnlyList<NoteId> ids = await context.StoreNotesOnDateAsync(
            Day, ("Alpha", "alpha body"), ("Bravo", "bravo body"), ("Charlie", "charlie body"));
        await context.Main.InitializeAsync();

        await context.Main.Notes.ReorderAsync([ids[2], ids[0], ids[1]]);

        SearchActivationOutcome outcome = await context.Main.Navigator.ActivateAsync(NoteResult(ids[1], Day, "Bravo"));

        Assert.IsTrue(outcome.Navigated);
        Assert.AreEqual(Day, context.Main.SelectedDate);
        Assert.AreEqual(ids[1], context.Main.Notes.SelectedTab!.Id);
    }

    [TestMethod]
    public async Task NoteResult_ResolvesByStableIdAfterRestart()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        IReadOnlyList<NoteId> ids = await context.StoreNotesOnDateAsync(
            Day, ("Alpha", "alpha body"), ("Bravo", "bravo body"), ("Charlie", "charlie body"));
        await context.Main.InitializeAsync();
        await context.Main.Notes.ReorderAsync([ids[2], ids[0], ids[1]]);

        await using WorkspaceTestContext.FreshShell restarted = context.NewShell();
        await restarted.Main.InitializeAsync();

        SearchActivationOutcome outcome = await restarted.Main.Navigator.ActivateAsync(NoteResult(ids[0], Day, "Alpha"));

        Assert.IsTrue(outcome.Navigated);
        Assert.AreEqual(Day, restarted.Main.SelectedDate);
        Assert.AreEqual(ids[0], restarted.Main.Notes.SelectedTab!.Id);
    }

    [TestMethod]
    public async Task NoteResult_SelectsExactDateWhenNavigatingFromAnotherDate()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        NoteId target = (await context.StoreNotesOnDateAsync(Other, ("Historical", "history body")))[0];
        await context.Main.InitializeAsync();
        Assert.AreEqual(Day, context.Main.SelectedDate);

        SearchActivationOutcome outcome = await context.Main.Navigator.ActivateAsync(NoteResult(target, Other, "Historical"));

        Assert.IsTrue(outcome.Navigated);
        Assert.AreEqual(Other, context.Main.SelectedDate);
        Assert.AreEqual(target, context.Main.Notes.SelectedTab!.Id);
    }

    [TestMethod]
    public async Task StaleNoteResult_ShowsMessageRefreshesAndDoesNotMisnavigate()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        IReadOnlyList<NoteId> ids = await context.StoreNotesOnDateAsync(
            Day, ("Ghost", "ghost body text"), ("Keeper", "ghost keeper text"));
        await context.Main.InitializeAsync();
        await context.Main.Notes.SelectNoteByIdAsync(ids[1]);

        context.Main.Search.Query = "ghost";
        await context.Main.Search.Pending;
        Assert.AreEqual(2, context.Main.Search.Results.Count);

        // The source is deleted out of band after the search returned it.
        await context.NoteRepository.DeleteNoteAsync(Day, ids[0]);

        await context.Main.Search.ActivateAsync(NoteResult(ids[0], Day, "Ghost"));

        Assert.IsNotNull(context.Main.Search.StaleMessage);
        Assert.IsTrue(context.Main.Search.IsOpen, "The overlay stays open so the refreshed results are visible.");
        Assert.IsFalse(context.Main.Search.Results.Any(r => r.SourceId == ids[0].Value), "The stale result is gone after refresh.");
        Assert.IsTrue(context.Main.Search.Results.Any(r => r.SourceId == ids[1].Value));
        Assert.AreEqual(ids[1], context.Main.Notes.SelectedTab!.Id, "Selection must not jump to the deleted note.");
    }

    private static SearchResultViewModel NoteResult(NoteId id, LocalDate date, string title) =>
        new(new SearchResult(SearchSourceType.Note, id.Value, date, title, title, 0.0));
}
