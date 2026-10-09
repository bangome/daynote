using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.Core.Notes;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Daynote.Motion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// M3's held tick at its real timing: the 600 ms wait is a gate the test opens, and the ticks are
/// tapped on the day screen with the pointer, as a thumb would.
/// </summary>
/// <remarks>
/// The defect these guard: a second tick inside the first one's wait lost its checkbox - and its
/// command - when the first one's write rebuilt the list, so it was never written; and a tap on the
/// rebuilt row could write a second override on a repeating to-do.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class PendingTickTests
{
    [TestCleanup]
    public void Restore() => MotionEnvironment.Instant = true;

    [TestMethod]
    public void Two_ticks_inside_the_wait_are_both_written()
    {
        WithDay((view, shell, gates) =>
        {
            Tap(view, Check(view, "회의실 예약 확인"));
            Tap(view, Check(view, "릴리즈 노트 작성"));
            Assert.AreEqual(2, shell.Ticks.Count, "Both ticks should be held.");
            Assert.IsFalse(Todo(shell, "회의실 예약 확인").Checked, "Nothing is written during the wait.");

            // The first wait ends: its write rebuilds the list under the second.
            Open(gates, 0);
            Settle(view);
            Assert.IsTrue(Todo(shell, "회의실 예약 확인").Checked);
            Assert.IsTrue(IsShownTicked(Check(view, "릴리즈 노트 작성")), "The rebuilt row lost its held tick.");

            Open(gates, 1);
            Settle(view);
            Assert.IsTrue(Todo(shell, "릴리즈 노트 작성").Checked, "The second tick was lost with its checkbox.");
            Assert.AreEqual(0, shell.Ticks.Count);
        });
    }

    [TestMethod]
    public void A_tap_on_a_rebuilt_row_takes_the_held_tick_back_instead_of_writing_twice()
    {
        WithDay((view, shell, gates) =>
        {
            Tap(view, Check(view, "회의실 예약 확인"));
            ScreenshotTests.Pump(shell.RefreshAllAsync);
            Settle(view);
            Assert.IsTrue(IsShownTicked(Check(view, "회의실 예약 확인")), "A rebuild dropped the held tick.");

            Tap(view, Check(view, "회의실 예약 확인"));
            Assert.AreEqual(0, shell.Ticks.Count, "The second tap should cancel, not queue another toggle.");

            Open(gates, 0);
            Settle(view);
            Assert.IsFalse(Todo(shell, "회의실 예약 확인").Checked, "A cancelled tick was written.");
        });
    }

    [TestMethod]
    public void A_flush_writes_the_held_tick_at_once()
    {
        WithDay((view, shell, _) =>
        {
            Tap(view, Check(view, "회의실 예약 확인"));
            ScreenshotTests.Pump(() => shell.FlushAsync(FlushReason.Quit));
            Settle(view);
            Assert.AreEqual(0, shell.Ticks.Count);
            Assert.IsTrue(Todo(shell, "회의실 예약 확인").Checked, "Quitting lost the held tick.");
        });
    }

    private static void WithDay(Action<MainView, MobileShellViewModel, List<TaskCompletionSource>> body) =>
        TestServices.WithInitialisedShell(402, 874, (view, shell) =>
        {
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            shell.GoToPageCommand.Execute(MobilePage.Day);
            Settle(view);

            var gates = new List<TaskCompletionSource>();
            MotionEnvironment.Instant = false;
            shell.Ticks.Delay = (_, token) =>
            {
                var gate = new TaskCompletionSource();
                token.Register(() => gate.TrySetCanceled(token));
                gates.Add(gate);
                return gate.Task;
            };

            body(view, shell, gates);
        });

    private static void Open(List<TaskCompletionSource> gates, int index)
    {
        gates[index].TrySetResult();
        for (int i = 0; i < 40; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static Daynote.App.Shell.Product.TodoItemViewModel Todo(MobileShellViewModel shell, string title) =>
        shell.DayTodos.Single(row => row.Item.Text == title).Item;

    private static TodoCheck Check(Control view, string title) =>
        view.GetVisualDescendants().OfType<Views.DayPage>().Single()
            .GetVisualDescendants().OfType<TodoCheck>()
            .Single(check => (check.DataContext as TodoRowViewModel)?.Item.Text == title);

    private static bool IsShownTicked(TodoCheck check) => check.Classes.Contains(":checked");

    private static void Tap(Control view, Control target)
    {
        var window = (Window)TopLevel.GetTopLevel(view)!;
        Settle(view);
        Point centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        window.MouseMove(new Point(1, 1));
        Settle(view);
    }

    private static void Settle(Control view)
    {
        for (int i = 0; i < 5; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
        }
    }
}
