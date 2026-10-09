using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Mobile.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The Lists tab's chip row (phone §03): the counts it shares with the desktop sidebar, the filter,
/// and the sheet that makes, renames and deletes a list.
/// </summary>
[TestClass]
public sealed class AgendaListChipTests
{
    [TestMethod]
    public void The_row_leads_with_everything_and_ends_with_a_way_to_make_one()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            Guid work = AddList(shell, "업무");
            AddTodo(shell, "내 것");
            AddTodo(shell, "업무 하나", work);
            AddTodo(shell, "업무 둘", work);

            Assert.IsTrue(shell.IsAllAgendaListsSelected, "Nothing is filtered until a chip is tapped.");
            Assert.AreEqual(3, shell.Todo.OpenCount, "The 전체 chip reads the panel's own total.");

            // The same rows the desktop sidebar draws, in the same order, from the same counts.
            Assert.AreSequenceEqual(
new[] { "내 할 일", "업무" },
                shell.Todo.Lists.Select(list => list.Name).ToArray());
            Assert.AreSequenceEqual(
new[] { "1", "2" }, shell.Todo.Lists.Select(list => list.CountText).ToArray());
            Assert.IsFalse(shell.Todo.Lists[0].CanDelete, "The built-in list's menu has no delete item.");

            shell.GoToPageCommand.Execute(MobilePage.Lists);
            Dispatcher.UIThread.RunJobs();

            // Rendered, rather than only built: a chip whose binding does not resolve is a silent
            // blank on a phone, and this is the only place that would catch it.
            string[] drawn = [.. view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.DataContext is AgendaListRowViewModel)
                .Select(button => ((AgendaListRowViewModel)button.DataContext!).Name)];
            Assert.AreSequenceEqual(
new[] { "내 할 일", "업무" }, drawn);
        });
    }

    [TestMethod]
    public void A_chip_narrows_the_bands_without_changing_what_is_owed()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Guid work = AddList(shell, "업무");
            AddTodo(shell, "내 것");
            AddTodo(shell, "업무 것", work);

            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(work));

            Assert.AreSequenceEqual(
new[] { "업무 것" }, Rows(shell));
            Assert.AreEqual(2, shell.Todo.OpenCount, "Narrowing the view changed how much there is.");
            Assert.IsFalse(shell.IsAllAgendaListsSelected);
            Assert.IsTrue(shell.Todo.Lists.Single(list => list.Name == "업무").IsSelected);
        });
    }

    [TestMethod]
    public void The_first_chip_is_how_the_filter_comes_off()
    {
        // A chip row has no empty space to tap, so 전체 is the way back - and tapping the lit chip
        // works too, the way a filter comes off everywhere else.
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Guid work = AddList(shell, "업무");
            AddTodo(shell, "내 것");

            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(work));
            Assert.IsEmpty(Rows(shell));

            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(null));

            Assert.IsTrue(shell.IsAllAgendaListsSelected);
            Assert.AreSequenceEqual(
new[] { "내 것" }, Rows(shell));

            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(work));
            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(work));
            Assert.IsTrue(shell.IsAllAgendaListsSelected);
        });
    }

    [TestMethod]
    public void The_day_is_not_filtered_by_a_list()
    {
        // The chips sit over the cross-date view. The day panel answers "what is on this date", and
        // filtering it would be hiding part of a date from itself.
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Guid work = AddList(shell, "업무");
            AddTodo(shell, "내 것");
            AddTodo(shell, "업무 것", work);

            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(work));

            Assert.AreSequenceEqual(
new[] { "내 것", "업무 것" },
                shell.DayTodos.Select(row => row.Item.Text).OrderBy(text => text, StringComparer.Ordinal).ToArray());
        });
    }

    [TestMethod]
    public void A_long_press_opens_the_menu_and_the_built_in_list_has_no_delete()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            AddList(shell, "업무");

            shell.OpenAgendaListMenu(shell.Todo.Lists[0]);

            Assert.IsTrue(shell.IsAgendaListSheetOpen);
            Assert.IsFalse(shell.IsNamingAgendaList, "The menu comes first; the field is the second face.");
            Assert.AreEqual("내 할 일", shell.AgendaListSheetTitle);
            Assert.IsFalse(shell.CanDeleteSheetList);
            Assert.IsTrue(shell.IsSheetListDefault, "Only the built-in list explains what lands in it.");
            Assert.IsFalse(shell.ShowDock, "A sheet covers the tab bar.");

            shell.OpenAgendaListMenu(shell.Todo.Lists[1]);
            Assert.IsTrue(shell.CanDeleteSheetList);
            Assert.IsFalse(shell.IsSheetListDefault);

            Assert.IsTrue(Run(shell.GoBackAsync()), "Back should close the sheet rather than leave the app.");
            Assert.IsFalse(shell.IsAgendaListSheetOpen);
        });
    }

    [TestMethod]
    public void The_one_field_both_makes_a_list_and_renames_one()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            shell.NewAgendaListCommand.Execute(null);
            Assert.IsTrue(shell.IsNamingAgendaList);
            Assert.AreEqual(string.Empty, shell.AgendaListDraftName, "A new list starts empty, not pre-named.");

            shell.AgendaListDraftName = "운동";
            Pump(() => shell.CommitAgendaListNameCommand.ExecuteAsync(null));

            Assert.IsFalse(shell.IsAgendaListSheetOpen);
            AgendaListRowViewModel made = shell.Todo.Lists.Single(list => list.Name == "운동");
            Assert.IsTrue(made.IsSelected, "A list is selected as it is made, so the next thing filed lands in it.");

            shell.OpenAgendaListMenu(made);
            shell.RenameAgendaListFromSheetCommand.Execute(null);
            Assert.AreEqual("운동", shell.AgendaListDraftName, "A rename starts from the name it has.");
            shell.AgendaListDraftName = "아침 운동";
            Pump(() => shell.CommitAgendaListNameCommand.ExecuteAsync(null));

            Assert.IsTrue(shell.Todo.Lists.Any(list => list.Name == "아침 운동"));
            Assert.IsFalse(shell.Todo.Lists.Any(list => list.Name == "운동"));
        });
    }

    [TestMethod]
    public void A_blank_name_is_a_cancel_rather_than_a_list_nobody_can_read()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            int before = shell.Todo.Lists.Count;
            shell.NewAgendaListCommand.Execute(null);
            shell.AgendaListDraftName = "   ";
            Pump(() => shell.CommitAgendaListNameCommand.ExecuteAsync(null));

            Assert.IsFalse(shell.IsAgendaListSheetOpen);
            Assert.AreEqual(before, shell.Todo.Lists.Count);
        });
    }

    [TestMethod]
    public void Deleting_a_list_keeps_its_to_dos_and_drops_the_filter()
    {
        // §3: a container is not a reason to lose a task. They move to the built-in list, which is
        // also why the sheet deletes without asking.
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Guid work = AddList(shell, "업무");
            AddTodo(shell, "업무 것", work);
            Pump(() => shell.SelectAgendaListCommand.ExecuteAsync(work));

            shell.OpenAgendaListMenu(shell.Todo.Lists.Single(list => list.Name == "업무"));
            Pump(() => shell.DeleteAgendaListFromSheetCommand.ExecuteAsync(null));

            Assert.IsFalse(shell.IsAgendaListSheetOpen);
            Assert.IsTrue(shell.IsAllAgendaListsSelected, "The view stayed filtered to a list that is gone.");
            Assert.AreSequenceEqual(
new[] { "업무 것" }, Rows(shell));
            Assert.AreEqual("1", shell.Todo.Lists.Single().CountText);
        });
    }

    /// <summary>Every to-do the bands are showing, in the order they are drawn.</summary>
    private static string[] Rows(MobileShellViewModel shell) =>
        [.. shell.TodoGroups.SelectMany(group => group.Items).Select(row => row.Item.Text)];

    private static Guid AddList(MobileShellViewModel shell, string name)
    {
        IAgendaRepository agenda = Repository();
        var id = Guid.NewGuid();
        Pump(() => agenda.CreateListAsync(id, name).AsTask());
        Pump(shell.RefreshAllAsync);
        return id;
    }

    /// <summary>
    /// A to-do due on the selected day. Written through the repository because the phone's own
    /// capture bar is not built yet (phone §01).
    /// </summary>
    private static void AddTodo(MobileShellViewModel shell, string title, Guid? list = null)
    {
        IAgendaRepository agenda = Repository();
        DateOnly day = LocalDates.ToDateOnly(shell.SelectedDate);
        var item = new AgendaItem(
            Guid.NewGuid(),
            list ?? AgendaList.DefaultId,
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
            AgendaStatus.NeedsAction,
            CompletedUtc: null,
            Priority: 0,
            TimelineVisibility.Auto,
            SourceNoteId: null,
            ExceptionDates: [],
            AgendaAlert.Default,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Pump(() => agenda.SaveAsync(item).AsTask());
        Pump(shell.RefreshAllAsync);
    }

    private static IAgendaRepository Repository() =>
        (IAgendaRepository)TestServices.CurrentProvider!.GetService(typeof(IAgendaRepository))!;

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
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
}
