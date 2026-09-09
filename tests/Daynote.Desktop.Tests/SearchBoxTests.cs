using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Typing in the title bar's search box opens the results dropdown under it.
/// </summary>
/// <remarks>
/// Reported from use: nothing appears. The earlier test set <c>Search.Query</c> directly, which
/// exercised the view model and skipped the one thing a user does — put characters in the TextBox —
/// so a broken <c>Text</c> binding would have passed it. These drive real key input instead, and
/// check where the panel lands: a dropdown that opens somewhere other than under the box it belongs
/// to is not much better than one that never opens.
/// </remarks>
[TestClass]
public sealed class SearchBoxTests
{
    [TestMethod]
    public void Typing_in_the_box_reaches_the_query()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            var box = window.FindControl<TextBox>("SearchBox")!;
            box.Focus();
            window.KeyTextInput("회의");
            Pump();

            Assert.AreEqual("회의", box.Text, "The TextBox did not take the typed text.");
            Assert.AreEqual("회의", shell.Search.Query, "The typed text never reached Search.Query; the Text binding is one-way.");
            Assert.IsTrue(shell.Search.IsOpen, "A non-empty query must open the dropdown.");
        });
    }

    [TestMethod]
    public void The_dropdown_appears_under_the_search_box()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            var box = window.FindControl<TextBox>("SearchBox")!;
            box.Focus();
            window.KeyTextInput("회의");
            Pump();
            window.UpdateLayout();

            Control dropdown = Dropdown(window);
            Assert.IsTrue(dropdown.IsVisible, "The dropdown is not visible.");

            Rect boxBounds = ScreenBounds(window, box);
            Rect panel = ScreenBounds(window, dropdown);

            Assert.IsGreaterThan(0, panel.Width, "The dropdown has no width.");
            Assert.IsGreaterThan(boxBounds.Bottom - 1, panel.Top, "The dropdown does not hang below the search box.");

            // Its left edge lines up with the box's, unless a narrow window clamped it to the edge.
            // The point is that it follows the box rather than the body's editor column, which is
            // where it used to be — under the note title, far to the right of the box's left edge.
            bool aligned = Math.Abs(panel.Left - boxBounds.Left) < 1;
            bool clamped = Math.Abs(panel.Left - 8) < 1;
            Assert.IsTrue(aligned || clamped, $"The dropdown {panel} does not follow the search box {boxBounds}.");
            Assert.IsLessThan(window.Bounds.Height, panel.Top, "The dropdown starts below the window.");
        });
    }

    private static Control Dropdown(Window window) =>
        ((MainWindow)window).FindControl<Border>("SearchDropdown")!;

    private static Rect ScreenBounds(Window window, Control control) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static void Pump()
    {
        for (int i = 0; i < 20; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
