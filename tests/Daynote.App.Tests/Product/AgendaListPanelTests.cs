using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The lists beside 할 일: their counts, the filter, and making, renaming and deleting one
/// (design §04 4e).
/// </summary>
[TestClass]
public sealed class AgendaListPanelTests
{
    private static readonly LocalDate Today = LocalDate.Parse("2026-07-20").Value;

    [TestMethod]
    public async Task The_built_in_list_is_first_and_cannot_be_deleted()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        AgendaListRowViewModel first = harness.Shell.Todo.Lists.First();

        Assert.AreEqual("내 할 일", first.Name, "The built-in list reads from the catalogue, not the row.");
        // §04 leaves the item out rather than greying it: there is nowhere for its contents to go.
        Assert.IsFalse(first.CanDelete);
    }

    [TestMethod]
    public async Task A_lists_count_is_what_is_owed_in_it()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        AgendaList work = await context.AgendaRepository.CreateListAsync(Guid.NewGuid(), "업무");
        await context.StoreTodoAsync(Today, "내 것");
        await context.StoreTodoAsync(Today, "업무 하나", list: work.Id);
        await context.StoreTodoAsync(Today, "업무 둘", list: work.Id);
        await context.StoreTodoAsync(Today, "끝난 업무", list: work.Id, done: true);
        await harness.Shell.Todo.RefreshAsync();

        Assert.AreEqual("1", harness.Shell.Todo.Lists.Single(l => l.Name == "내 할 일").CountText);
        Assert.AreEqual("2", harness.Shell.Todo.Lists.Single(l => l.Name == "업무").CountText);
        Assert.AreEqual(3, harness.Shell.Todo.OpenCount);
    }

    [TestMethod]
    public async Task Selecting_a_list_narrows_the_view_without_changing_the_heading()
    {
        // "할 일 17" is how much there is. Narrowing the view does not make seventeen things into
        // seven, and a heading that moved with the filter would be a different claim every tap.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        AgendaList work = await context.AgendaRepository.CreateListAsync(Guid.NewGuid(), "업무");
        await context.StoreTodoAsync(Today, "내 것");
        await context.StoreTodoAsync(Today, "업무 것", list: work.Id);
        await harness.Shell.Todo.RefreshAsync();

        await harness.Shell.Todo.SelectListAsync(work.Id);

        Assert.AreEqual("업무 것", Rows(harness).Single().Text);
        Assert.AreEqual(2, harness.Shell.Todo.OpenCount);
        Assert.IsTrue(harness.Shell.Todo.Lists.Single(l => l.Name == "업무").IsSelected);
    }

    [TestMethod]
    public async Task Selecting_the_list_you_are_already_in_takes_the_filter_off()
    {
        // The only way off the filter on the phone's chip row, and the way it works everywhere
        // else besides.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        AgendaList work = await context.AgendaRepository.CreateListAsync(Guid.NewGuid(), "업무");
        await context.StoreTodoAsync(Today, "내 것");
        await harness.Shell.Todo.RefreshAsync();

        await harness.Shell.Todo.SelectListAsync(work.Id);
        Assert.IsEmpty(Rows(harness));

        await harness.Shell.Todo.SelectListAsync(work.Id);

        Assert.IsNull(harness.Shell.Todo.SelectedListId);
        Assert.AreEqual("내 것", Rows(harness).Single().Text);
    }

    [TestMethod]
    public async Task A_new_list_is_selected_so_the_next_thing_filed_lands_in_it()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        AgendaList made = await harness.Shell.Todo.CreateListAsync("새 리스트");

        Assert.AreEqual(made.Id, harness.Shell.Todo.SelectedListId);
        Assert.IsTrue(harness.Shell.Todo.Lists.Single(l => l.Id == made.Id).IsSelected);
    }

    [TestMethod]
    public async Task Deleting_a_list_keeps_its_to_dos_and_drops_the_filter()
    {
        // §3: a container is not a reason to lose a task. They move to the built-in list, which
        // is also why deleting one is not worth a confirmation.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        AgendaList work = await context.AgendaRepository.CreateListAsync(Guid.NewGuid(), "업무");
        await context.StoreTodoAsync(Today, "업무 것", list: work.Id);
        await harness.Shell.Todo.RefreshAsync();
        await harness.Shell.Todo.SelectListAsync(work.Id);

        await harness.Shell.Todo.DeleteListAsync(work.Id);

        Assert.IsNull(harness.Shell.Todo.SelectedListId, "The view stayed filtered to a list that is gone.");
        Assert.AreEqual("업무 것", Rows(harness).Single().Text);
        Assert.AreEqual("1", harness.Shell.Todo.Lists.Single(l => l.Name == "내 할 일").CountText);
    }

    [TestMethod]
    public async Task A_list_deleted_on_another_device_takes_the_filter_with_it()
    {
        // Not a hypothetical: the list is a synced row, and a pull can remove the one this
        // screen is filtered to. Leaving the filter on would show an empty view with no way back.
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await using WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await harness.Shell.InitializeAsync();

        AgendaList work = await context.AgendaRepository.CreateListAsync(Guid.NewGuid(), "업무");
        await harness.Shell.Todo.RefreshAsync();
        await harness.Shell.Todo.SelectListAsync(work.Id);

        await context.AgendaRepository.DeleteListAsync(work.Id);
        await harness.Shell.Todo.RefreshAsync();

        Assert.IsNull(harness.Shell.Todo.SelectedListId);
    }

    private static IReadOnlyList<TodoItemViewModel> Rows(WorkspaceTestContext.ProductShellHarness harness) =>
        [.. harness.Shell.Todo.TodayItems.Concat(harness.Shell.Todo.Items)];
}
