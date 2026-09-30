using System.ComponentModel;
using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Exactly one view fills the middle of the window, and it says which one it is.
/// </summary>
/// <remarks>
/// The editor, the timeline and the four lists share one cell and each shows itself on its own flag,
/// so two flags on at once draws two views over each other. The heading above the lists is derived
/// from <c>ActiveTab</c>, so a list that opens while another is already up has to raise it.
/// Both were wrong when the design-B layout landed and both are invisible to a binding-error sweep:
/// one is a stale string, the other is two views that each composed correctly.
/// </remarks>
[TestClass]
public sealed class DeskNavigationTests
{
    [TestMethod]
    public async Task Switching_between_lists_renames_the_heading()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            await harness.Shell.InitializeAsync();
            await harness.Shell.ShowListCommand.ExecuteAsync(RightTab.Todo);
            string first = harness.Shell.ListTitle;

            var raised = new List<string>();
            harness.Shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

            // The list view is already up: only ActiveTab changes from here.
            await harness.Shell.ShowListCommand.ExecuteAsync(RightTab.Files);

            Assert.AreNotEqual(first, harness.Shell.ListTitle, "The heading still names the list that was open before.");
            CollectionAssert.Contains(raised, nameof(ProductShellViewModel.ListTitle), "Nothing told the window the heading changed.");
            CollectionAssert.Contains(raised, nameof(ProductShellViewModel.ListSubtitle));
        }
    }

    [TestMethod]
    public async Task Every_list_names_itself()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            await harness.Shell.InitializeAsync();

            var titles = new List<string>();
            foreach (RightTab tab in new[] { RightTab.Todo, RightTab.Favorites, RightTab.Tags, RightTab.Files })
            {
                await harness.Shell.ShowListCommand.ExecuteAsync(tab);
                titles.Add(harness.Shell.ListTitle);
            }

            CollectionAssert.AllItemsAreUnique(titles, $"Two lists share a heading: {string.Join(", ", titles)}");
        }
    }

    [TestMethod]
    public async Task The_timeline_replaces_the_list_rather_than_covering_it()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            await harness.Shell.InitializeAsync();
            await harness.Shell.ShowListCommand.ExecuteAsync(RightTab.Tags);
            Assert.IsTrue(harness.Shell.IsListMode);

            await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);

            Assert.IsTrue(harness.Shell.IsTimelineMode, "The timeline did not open.");
            AssertOneView(harness.Shell);
        }
    }

    [TestMethod]
    public async Task One_view_fills_the_middle_along_every_path_between_them()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            await harness.Shell.InitializeAsync();
            AssertOneView(harness.Shell);

            await harness.Shell.ShowListCommand.ExecuteAsync(RightTab.Favorites);
            AssertOneView(harness.Shell);

            await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);
            AssertOneView(harness.Shell);

            // Toggling the timeline off leaves the editor, not the list that was up before it.
            await harness.Shell.ToggleTimelineCommand.ExecuteAsync(null);
            AssertOneView(harness.Shell);
            Assert.IsTrue(harness.Shell.IsEditorMode);

            await harness.Shell.ShowListCommand.ExecuteAsync(RightTab.Files);
            AssertOneView(harness.Shell);

            harness.Shell.ShowEditorCommand.Execute(null);
            AssertOneView(harness.Shell);
            Assert.IsTrue(harness.Shell.IsEditorMode);
        }
    }

    private static void AssertOneView(ProductShellViewModel shell)
    {
        int showing = (shell.IsEditorMode ? 1 : 0) + (shell.IsTimelineMode ? 1 : 0) + (shell.IsListMode ? 1 : 0);
        Assert.AreEqual(
            1,
            showing,
            $"editor={shell.IsEditorMode} timeline={shell.IsTimelineMode} list={shell.IsListMode}; " +
            "the three share one cell, so anything but exactly one draws them over each other.");
    }
}
