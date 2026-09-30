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

            Assert.IsTrue(shell.IsMonthPickerOpen, "Choosing a month closed the sheet before a day was picked.");
            Assert.AreEqual(3, shell.PickerMonths.Single(m => m.IsCurrent).Number, "The chosen month is not the marked one.");
            Assert.AreEqual(before.Year - 1, shell.Calendar.CursorYear);
            Assert.AreEqual(3, shell.Calendar.CursorMonth);
            Assert.AreEqual(before, shell.SelectedDate, "The picker moved the selected day.");
            Assert.IsNotEmpty(shell.Calendar.Cells, "The grid did not reload for the chosen month.");
        });
    }

    [TestMethod]
    public void Tapping_a_day_in_the_sheet_selects_it_and_closes_the_sheet()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            shell.OpenMonthPickerCommand.Execute(null);
            Pump(() => shell.PickMonthCommand.ExecuteAsync(shell.Calendar.CursorMonth == 1 ? 2 : 1));
            var cell = shell.Calendar.Cells.First(c => c.IsInMonth && c.Date.Day == 15);

            Pump(() => cell.SelectCommand.ExecuteAsync(null));

            Assert.IsFalse(shell.IsMonthPickerOpen, "Picking a day left the sheet open.");
            Assert.AreEqual(cell.Date, shell.SelectedDate, "The day tapped is not the day selected.");
            Assert.IsTrue(shell.Week.Any(d => d.IsSelected && d.Date == cell.Date), "The week strip did not follow.");
        });
    }

    [TestMethod]
    public void Closing_the_sheet_without_a_day_puts_the_header_back_on_the_selected_month()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            shell.OpenMonthPickerCommand.Execute(null);
            shell.PickerPreviousYearCommand.Execute(null);
            Pump(() => shell.PickMonthCommand.ExecuteAsync(3));

            shell.CloseMonthPickerCommand.Execute(null);
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (shell.Calendar.CursorYear != shell.SelectedDate.Year && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Assert.AreEqual(shell.SelectedDate.Year, shell.Calendar.CursorYear, "The header kept the browsed year.");
            Assert.AreEqual(shell.SelectedDate.Month, shell.Calendar.CursorMonth, "The header kept the browsed month.");
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
                // The one on screen: the attachment sheets have scrims of their own, hidden now.
                .Single(b => b.Classes.Contains("scrim") && b.IsEffectivelyVisible);

            // The bound command, not a synthesised Click: a Button runs its Command from its own
            // OnClick, so raising the event from outside proves nothing about the binding. What is
            // worth checking is that the scrim resolved to a real command and that it closes.
            Assert.IsNotNull(scrim.Command, "The scrim has no command bound to it.");
            scrim.Command.Execute(scrim.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(shell.IsMonthPickerOpen, "Tapping beside the sheet did not close it.");
            Assert.IsTrue(shell.ShowDock, "The tab bar did not come back after the sheet closed.");
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
