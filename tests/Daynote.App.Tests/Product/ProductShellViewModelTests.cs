using CommunityToolkit.Mvvm.Input;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Integration tests for the Calendar Notes product shell over a real SQLite database. They cover the
/// redesign surfaces the design contract adds: calendar content dots, the cross-date todo panel with
/// toggle-rewrite, favorite/tag editing through the reused note engine, search (note + derived date
/// results) with navigation, day-file add/delete, and theme/collapse persistence.
/// </summary>
[TestClass]
public sealed class ProductShellViewModelTests
{
    private static readonly LocalDate Today = LocalDate.Parse("2026-07-20").Value;

    /// <summary>Polls a fire-and-forget persistence condition to true within a bounded window.</summary>
    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("The persisted condition did not become true within the timeout.");
    }

    [TestMethod]
    public async Task Calendar_ShowsNoteDotFromMonthSummary()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "회의", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();

        await harness.Shell.InitializeAsync();

        CalendarDayCellViewModel cell = harness.Shell.Calendar.Cells.Single(c => c.IsInMonth && c.Date == Today);
        Assert.IsTrue(cell.HasNotes, "The date with a note shows the accent note dot.");
        Assert.AreEqual("1", cell.CountText);
    }

    [TestMethod]
    public async Task Todo_ParsesAcrossDatesAndTogglePersistsBody()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        LocalDate other = LocalDate.Parse("2026-07-18").Value;
        NoteId a = await context.StoreNoteAsync(Today, "오늘", "-[] 오늘 할 일 (7/25 14:00)");
        await context.StoreNoteAsync(other, "지난주", "-[] 예전 할 일\n-[x] 완료");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();

        await harness.Shell.InitializeAsync();

        Assert.AreEqual(3, harness.Shell.Todo.Items.Count, "Todos parse from every date, not just the selected one.");
        Assert.AreEqual(2, harness.Shell.Todo.OpenCount);

        TodoItemViewModel first = harness.Shell.Todo.Items.First(i => i.Text == "오늘 할 일");
        await ((IAsyncRelayCommand)first.ToggleCommand).ExecuteAsync(null);

        DayWorkspace workspace = await context.NoteRepository.GetDayWorkspaceStateAsync(Today);
        Note toggled = workspace.Notes.Notes.Single(n => n.Id == a);
        StringAssert.Contains(toggled.Body, "-[x]", "Toggling rewrites the source note's checkbox line.");
    }

    [TestMethod]
    public async Task Titlebar_CompactsBelowNineHundredDips()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        harness.Shell.UpdateWidth(899);
        Assert.IsTrue(harness.Shell.IsTitlebarCompact, "Below 900 DIP the titlebar simplifies.");

        harness.Shell.UpdateWidth(900);
        Assert.IsFalse(harness.Shell.IsTitlebarCompact, "At and above 900 DIP the full titlebar returns.");
    }

    [TestMethod]
    public async Task Todo_GroupsUncheckedDueTodayOnTop()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(
            Today, "오늘", "-[] 오늘 마감 (7/20 10:00)\n-[] 나중 마감 (7/25)\n-[x] 완료된 오늘 마감 (7/20)");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();

        await harness.Shell.InitializeAsync();

        Assert.IsTrue(harness.Shell.Todo.HasToday);
        Assert.AreEqual(1, harness.Shell.Todo.TodayItems.Count, "Only unchecked todos due today enter the 오늘 group.");
        Assert.AreEqual("오늘 마감", harness.Shell.Todo.TodayItems.Single().Text);
        Assert.IsFalse(harness.Shell.Todo.Items.Any(i => i.Text == "오늘 마감"), "A due-today todo leaves the general list.");
        Assert.AreEqual(2, harness.Shell.Todo.Items.Count, "Future-due and checked todos stay in the general list.");
    }

    [TestMethod]
    public async Task Favorite_ToggleThroughEditorPersists()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        NoteId id = await context.StoreNoteAsync(Today, "즐겨찾기 대상", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await ((IAsyncRelayCommand)harness.Shell.ToggleFavoriteCommand).ExecuteAsync(null);

        DayWorkspace workspace = await context.NoteRepository.GetDayWorkspaceStateAsync(Today);
        Assert.IsTrue(workspace.Notes.Notes.Single(n => n.Id == id).IsFavorite);
        Assert.IsTrue(harness.Notes.SelectedTab!.IsFavorite);
    }

    [TestMethod]
    public async Task Tags_AddAndRemovePersistThroughEditor()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        NoteId id = await context.StoreNoteAsync(Today, "태그 노트", "본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Notes.AddTagAsync(harness.Notes.SelectedTab, "회의");
        DayWorkspace afterAdd = await context.NoteRepository.GetDayWorkspaceStateAsync(Today);
        CollectionAssert.Contains(afterAdd.Notes.Notes.Single(n => n.Id == id).Tags.ToArray(), "회의");

        await harness.Shell.RemoveTagAsync("회의");
        DayWorkspace afterRemove = await context.NoteRepository.GetDayWorkspaceStateAsync(Today);
        Assert.AreEqual(0, afterRemove.Notes.Notes.Single(n => n.Id == id).Tags.Count);
    }

    [TestMethod]
    public async Task Search_ReturnsNoteAndDerivedDateResults()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Today, "검색 대상 노트", "고유단어 본문");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Shell.Search.SearchNowAsync("고유단어");

        Assert.IsTrue(
            harness.Shell.Search.Results.Any(r => r.Kind == Daynote.App.Localization.AppStrings.SearchKindNote),
            "A note result is produced for the matching body.");
        Assert.IsTrue(
            harness.Shell.Search.Results.Any(r => r.Kind == Daynote.App.Localization.AppStrings.SearchKindDate),
            "A date result is derived from the matched note's date.");
    }

    [TestMethod]
    public async Task Search_ActivatingNoteResultNavigatesToDateAndNote()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        LocalDate other = LocalDate.Parse("2026-07-15").Value;
        NoteId id = await context.StoreNoteAsync(other, "다른날 노트", "특이단어");
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        await harness.Shell.Search.SearchNowAsync("특이단어");
        SearchResultRowViewModel note = harness.Shell.Search.Results
            .First(r => r.Kind == Daynote.App.Localization.AppStrings.SearchKindNote);
        await ((IAsyncRelayCommand)note.ActivateCommand).ExecuteAsync(null);

        Assert.AreEqual(other, harness.Shell.SelectedDate);
        Assert.AreEqual(id, harness.Notes.SelectedTab!.Id);
    }

    [TestMethod]
    public async Task Files_AddThroughPickerThenDelete()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(temp, "attachment body");
        harness.Picker.Paths.Add(temp);
        try
        {
            await ((IAsyncRelayCommand)harness.Shell.Files.AddFilesCommand).ExecuteAsync(null);
            Assert.AreEqual(1, harness.Shell.Files.Items.Count);

            FileItemViewModel item = harness.Shell.Files.Items[0];
            await ((IAsyncRelayCommand)item.DeleteCommand).ExecuteAsync(null);
            Assert.AreEqual(0, harness.Shell.Files.Items.Count);
            Assert.IsTrue(harness.Shell.Files.IsEmpty);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [TestMethod]
    public async Task Files_AddFromStreamStoresPrependsAndUniquifiesNames()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        using var first = new MemoryStream("one"u8.ToArray());
        using var second = new MemoryStream("two"u8.ToArray());
        Daynote.Core.Files.DayFile? a = await harness.Shell.Files.AddFromStreamAsync("메모.txt", first);
        Daynote.Core.Files.DayFile? b = await harness.Shell.Files.AddFromStreamAsync("메모.txt", second);

        Assert.AreEqual("메모.txt", a!.DisplayName);
        Assert.AreEqual("메모 (2).txt", b!.DisplayName, "A duplicate display name is uniquified.");
        Assert.AreEqual(2, harness.Shell.Files.Items.Count);
        Assert.AreEqual("메모 (2).txt", harness.Shell.Files.Items[0].Name, "New cards are prepended (newest first).");
        Assert.IsFalse(harness.Shell.Files.IsEmpty);

        // Persisted, not just in-memory: a fresh shell over the same database lists both.
        await using WorkspaceTestContext.ProductShellHarness reloaded = context.BuildProductShell();
        await reloaded.Shell.InitializeAsync();
        Assert.AreEqual(2, reloaded.Shell.Files.Items.Count);
    }

    [TestMethod]
    public async Task Files_RevealFileOpensTheTabExpandsThePanelAndHighlightsTheNewestMatch()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        using var first = new MemoryStream("one"u8.ToArray());
        using var second = new MemoryStream("two"u8.ToArray());
        await harness.Shell.Files.AddFromStreamAsync("a.txt", first);
        await harness.Shell.Files.AddFromStreamAsync("b.txt", second);
        harness.Shell.RightCollapsed = true;
        harness.Shell.ActiveTab = RightTab.Todo;

        harness.Shell.RevealFile("a.txt");

        Assert.IsFalse(harness.Shell.RightCollapsed, "A link click expands a collapsed right panel.");
        Assert.AreEqual(RightTab.Files, harness.Shell.ActiveTab);
        Assert.IsTrue(harness.Shell.Files.Items.Single(i => i.Name == "a.txt").IsHighlighted);
        Assert.IsFalse(harness.Shell.Files.Items.Single(i => i.Name == "b.txt").IsHighlighted);

        // A dangling name still opens the tab and clears every highlight.
        harness.Shell.RevealFile("없는파일.png");
        Assert.AreEqual(RightTab.Files, harness.Shell.ActiveTab);
        Assert.IsTrue(harness.Shell.Files.Items.All(i => !i.IsHighlighted));
    }

    [TestMethod]
    public async Task ThemeAndCollapse_PersistAcrossShellReload()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using (WorkspaceTestContext.ProductShellHarness first = context.BuildProductShell())
        {
            await first.Shell.InitializeAsync();
            first.Shell.IsDark = true;
            first.Shell.LeftCollapsed = true;
            first.Shell.RightCollapsed = true;

            // Persistence is fire-and-forget from the property setters; await it landing before reloading.
            await WaitForAsync(async () => await first.Settings.GetAsync("product.theme") == "dark");
            await WaitForAsync(() => first.Settings.GetBoolAsync("product.left-collapsed", false).AsTask());
            await WaitForAsync(() => first.Settings.GetBoolAsync("product.right-collapsed", false).AsTask());
        }

        await using WorkspaceTestContext.ProductShellHarness reloaded = context.BuildProductShell();
        await reloaded.Shell.InitializeAsync();

        Assert.IsTrue(reloaded.Shell.IsDark, "Theme persisted through the settings store.");
        Assert.IsTrue(reloaded.Shell.LeftCollapsed);
        Assert.IsTrue(reloaded.Shell.RightCollapsed);
        Assert.AreEqual(true, reloaded.Theme.LastDark, "The reloaded shell applied the persisted dark theme.");
    }

    [TestMethod]
    public async Task NewNote_MaterializesAndClearsEmptyState()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();
        Assert.IsTrue(harness.Shell.IsDayEmpty, "A fresh date starts with the empty note list.");

        await ((IAsyncRelayCommand)harness.Shell.NewNoteCommand).ExecuteAsync(null);

        Assert.IsFalse(harness.Shell.IsDayEmpty);
        Assert.IsTrue(harness.Shell.HasOpenNote, "The created note opens in the editor.");
    }
}
