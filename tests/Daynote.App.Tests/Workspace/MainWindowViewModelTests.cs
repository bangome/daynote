using Daynote.App.Shell;
using Daynote.Core.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class MainWindowViewModelTests
{
    private static readonly LocalDate Today = WorkspaceTestContext.Date("2026-07-20");
    private static readonly LocalDate Next = WorkspaceTestContext.Date("2026-07-21");

    [TestMethod]
    public async Task SelectDate_LoadsThatDatesNotes()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "today", "today body");
        await context.StoreNoteAsync(Next, "next", "next body");
        await context.Main.InitializeAsync();

        Assert.AreEqual(Today, context.Main.SelectedDate);

        bool navigated = await context.Main.SelectDateAsync(Next);

        Assert.IsTrue(navigated);
        Assert.AreEqual(Next, context.Main.SelectedDate);
        Assert.AreEqual(Next, context.Notes.SelectedDate);
    }

    [TestMethod]
    public async Task LayoutVisibility_TracksResolvedState()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();

        context.Main.UpdateEffectiveWidth(1300);
        Assert.IsTrue(context.Main.IsWide);
        Assert.IsTrue(context.Main.IsSidebarLayout);

        context.Main.UpdateEffectiveWidth(700);
        Assert.IsTrue(context.Main.IsCompact);
        Assert.IsFalse(context.Main.IsSidebarLayout);
    }
}
