using Daynote.App.Notes;
using Daynote.App.Tests.Workspace;
using Daynote.Core.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Tagging a note that has not been saved yet.
/// </summary>
/// <remarks>
/// A day opens with one projected tab: a note the user can type into that has no row behind it. That
/// is the state a note stays in for as long as it is being written, and <c>AddTagAsync</c> used to
/// refuse outright for it — so a tag typed while writing a new note was dropped with no message. The
/// Avalonia shell hid the tag entry instead, which is how it was noticed.
/// <para>
/// Tagging is deliberate enough to be reason to create the note, which is what a rename already did.
/// </para>
/// </remarks>
[TestClass]
public sealed class NewNoteTaggingTests
{
    private static readonly LocalDate Today = LocalDate.Parse("2026-07-20").Value;

    [TestMethod]
    public async Task A_tag_on_an_unsaved_note_creates_it_and_sticks()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        NoteTabViewModel projection = harness.Shell.Notes.Tabs.Single();
        Assert.IsTrue(projection.IsProjection, "A fresh day should open on a projected note.");

        harness.Shell.Notes.EditorText = "새 노트 본문";
        Assert.IsTrue(await harness.Shell.Notes.AddTagAsync(projection, "회의"));

        NoteTabViewModel saved = harness.Shell.Notes.Tabs.Single();
        Assert.IsFalse(saved.IsProjection, "Tagging should have brought the note into being.");
        CollectionAssert.AreEqual(new[] { "회의" }, saved.Tags.ToArray());

        // And it is really in the database, not just in the tab.
        await using WorkspaceTestContext.ProductShellHarness reopened = context.BuildProductShell();
        await reopened.Shell.InitializeAsync();
        CollectionAssert.AreEqual(
            new[] { "회의" },
            reopened.Shell.Notes.Tabs.Single().Tags.ToArray(),
            "The tag did not survive a reload.");
    }

    [TestMethod]
    public async Task The_body_being_written_is_kept_when_the_tag_creates_the_note()
    {
        // Materialising flushes the pending text, so the note must not come out empty.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        harness.Shell.Notes.EditorText = "회의록 초안";
        await harness.Shell.Notes.AddTagAsync(harness.Shell.Notes.Tabs.Single(), "회의");

        await using WorkspaceTestContext.ProductShellHarness reopened = context.BuildProductShell();
        await reopened.Shell.InitializeAsync();
        Assert.AreEqual("회의록 초안", reopened.Shell.Notes.EditorText);
    }

    [TestMethod]
    public async Task An_empty_tag_still_creates_nothing()
    {
        // The guard order matters: a blank entry must not bring a note into existence.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        Assert.IsFalse(await harness.Shell.Notes.AddTagAsync(harness.Shell.Notes.Tabs.Single(), "   "));
        Assert.IsTrue(harness.Shell.Notes.Tabs.Single().IsProjection, "A blank tag created a note.");
    }

    [TestMethod]
    public async Task The_tag_panel_shows_a_new_tag_straight_away()
    {
        // The panel refresh used to hang off the Saved status, and ReplaceTagsAsync flushes *before*
        // it writes the tags — so the refresh read the state from a moment earlier and the new tag
        // only appeared once something else refreshed the panel.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "회의", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        Assert.IsTrue(harness.Shell.TagPanel.IsEmpty, "Nothing is tagged yet.");

        harness.Shell.TagInput = "주간";
        await harness.Shell.CommitTagCommand.ExecuteAsync(null);

        Assert.IsFalse(harness.Shell.TagPanel.IsEmpty, "The 태그 tab did not pick up the new tag.");
        Assert.AreEqual("#주간", harness.Shell.TagPanel.Tags.Single().Tag);
    }

    [TestMethod]
    public async Task Removing_a_tag_takes_it_out_of_the_panel_straight_away()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "회의", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        harness.Shell.TagInput = "주간";
        await harness.Shell.CommitTagCommand.ExecuteAsync(null);

        // Asserted before the removal as well as after: "empty" is also what a panel that never
        // refreshed at all looks like, so without this the test passes for the wrong reason.
        Assert.IsFalse(harness.Shell.TagPanel.IsEmpty, "The tag was never listed to begin with.");

        await harness.Shell.RemoveTagCommand.ExecuteAsync("주간");
        Assert.IsTrue(harness.Shell.TagPanel.IsEmpty, "The 태그 tab still lists the removed tag.");
    }

    [TestMethod]
    public async Task Tagging_a_saved_note_still_works()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "회의", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        NoteTabViewModel tab = harness.Shell.Notes.Tabs.Single(t => !t.IsProjection);
        Assert.IsTrue(await harness.Shell.Notes.AddTagAsync(tab, "주간"));
        CollectionAssert.Contains(harness.Shell.Notes.Tabs.Single(t => !t.IsProjection).Tags.ToArray(), "주간");
    }
}
