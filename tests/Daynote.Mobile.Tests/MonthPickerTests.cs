using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Daynote.Mobile.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The year-and-month picker behind the calendar header.
/// </summary>
/// <remarks>
/// It moves what the calendar shows and leaves the selected day alone, the same way the arrows
/// either side of the header do. That distinction is the whole reason for the test: a picker that
/// also changed the date would silently move the note list out from under the user.
/// </remarks>
[TestClass]
public sealed class MonthPickerTests
{
    [TestMethod]
    public void It_opens_on_the_month_the_calendar_is_showing()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            shell.OpenMonthPickerCommand.Execute(null);
            view.UpdateLayout();

            Assert.IsTrue(shell.IsMonthPickerOpen);
            Assert.AreEqual(shell.Calendar.CursorYear, shell.PickerYear);
            Assert.HasCount(12, shell.PickerMonths);
            Assert.AreEqual(
                shell.Calendar.CursorMonth,
                shell.PickerMonths.Single(m => m.IsCurrent).Number,
                "The month on screen is not the one marked in the picker.");
        });
    }

    [TestMethod]
    public void Choosing_a_month_moves_the_view_and_not_the_selected_day()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            var before = shell.SelectedDate;
            shell.OpenMonthPickerCommand.Execute(null);
            shell.PickerPreviousYearCommand.Execute(null);

            Assert.IsFalse(
                shell.PickerMonths.Any(m => m.IsCurrent),
                "Another year still marked a current month.");

            Pump(() => shell.PickMonthCommand.ExecuteAsync(3));
            view.UpdateLayout();

            Assert.IsFalse(shell.IsMonthPickerOpen, "Choosing a month left the sheet open.");
            Assert.AreEqual(before.Year - 1, shell.Calendar.CursorYear);
            Assert.AreEqual(3, shell.Calendar.CursorMonth);
            Assert.AreEqual(before, shell.SelectedDate, "The picker moved the selected day.");
            Assert.IsNotEmpty(shell.Calendar.Cells, "The grid did not reload for the chosen month.");
        });
    }

    [TestMethod]
    public void The_scrim_closes_it()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            shell.OpenMonthPickerCommand.Execute(null);
            view.UpdateLayout();

            Button scrim = view.GetLogicalDescendants()
                .OfType<Button>()
                .Single(b => b.Classes.Contains("scrim"));

            // The bound command, not a synthesised Click: a Button runs its Command from its own
            // OnClick, so raising the event from outside proves nothing about the binding. What is
            // worth checking is that the scrim resolved to a real command and that it closes.
            Assert.IsNotNull(scrim.Command, "The scrim has no command bound to it.");
            scrim.Command.Execute(scrim.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(shell.IsMonthPickerOpen, "Tapping beside the sheet did not close it.");
        });
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
