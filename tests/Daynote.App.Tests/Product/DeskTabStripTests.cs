using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Application = System.Windows.Application;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The day's notes as tabs: the strip scrolls with the wheel and says which end has more.
/// </summary>
/// <remarks>
/// A horizontal scrollbar under a row of tabs is most of the row, so there is none; the two edge
/// gradients carry the same information, and the wheel does the scrolling a bar would have offered.
/// None of that is visible to a binding sweep or to a layout pass, which is why it is driven here.
/// </remarks>
[TestClass]
public sealed class DeskTabStripTests
{
    [STATestMethod]
    public void The_wheel_scrolls_the_strip_and_the_fades_follow()
    {
        (ScrollViewer strip, Rectangle left, Rectangle right, Window window, _) = Compose(notes: 14);
        try
        {
            Assert.IsTrue(strip.ScrollableWidth > 0, "Fourteen tabs do not overflow the strip; the rest cannot be tested.");
            Assert.AreEqual(ScrollBarVisibility.Hidden, strip.HorizontalScrollBarVisibility, "The strip draws a scrollbar.");

            Assert.AreEqual(0d, left.Opacity, "Nothing is scrolled past yet, so the left fade should be off.");
            Assert.AreEqual(1d, right.Opacity, "There are tabs off the right edge and no fade says so.");

            Wheel(strip, -600);
            Pump();

            Assert.IsTrue(strip.HorizontalOffset > 0, "The wheel did not move the strip sideways.");
            Assert.AreEqual(1d, left.Opacity, "Tabs are off the left edge now and no fade says so.");

            strip.ScrollToRightEnd();
            Pump();

            Assert.AreEqual(0d, right.Opacity, "The strip is at its end; the right fade should be off.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void A_strip_that_fits_shows_no_fades()
    {
        (ScrollViewer strip, Rectangle left, Rectangle right, Window window, _) = Compose(notes: 2);
        try
        {
            Assert.AreEqual(0d, strip.ScrollableWidth, "Two tabs should fit without scrolling.");
            Assert.AreEqual(0d, left.Opacity);
            Assert.AreEqual(0d, right.Opacity, "A strip with nothing behind its edges should be plain.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Selecting_a_note_off_the_edge_brings_its_tab_back()
    {
        (ScrollViewer strip, _, _, Window window, ProductShellViewModel shell) = Compose(notes: 14);
        try
        {
            strip.ScrollToRightEnd();
            Pump();
            double atEnd = strip.HorizontalOffset;
            Assert.IsTrue(atEnd > 0);

            // The day list, a search result and a tag jump all select a note without touching the
            // strip; the first tab is far off the left edge by now.
            shell.SelectDayNoteCommand.Execute(shell.Notes.Tabs.First(tab => !tab.IsProjection));
            Pump();

            Assert.IsTrue(
                strip.HorizontalOffset < atEnd,
                "Selecting the first note left the strip where it was, so its tab is still off screen.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Wheel(UIElement target, int delta) =>
        target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        });

    private static (ScrollViewer Strip, Rectangle Left, Rectangle Right, Window Window, ProductShellViewModel Shell)
        Compose(int notes)
    {
        Application application = ProductWindowCompositionTests.EnsureApplicationResources(dark: false);
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        Run(harness.Shell.InitializeAsync());
        for (int i = 0; i < notes; i += 1)
        {
            Run(harness.Shell.NewNoteCommand.ExecuteAsync(null));
        }

        var window = new ProductWindow(harness.Shell)
        {
            Width = 1100,
            Height = 700,
            Left = -4000,
            ShowInTaskbar = false,
        };
        window.Show();
        Pump();

        var card = (EditorCardView)window.FindName("TutEditor");
        var strip = (ScrollViewer)card.FindName("TabScroll");
        var left = (Rectangle)card.FindName("TabFadeLeft");
        var right = (Rectangle)card.FindName("TabFadeRight");
        Assert.IsNotNull(strip);

        // The window owns the context and the harness for the rest of the test; both are torn down
        // with it, and neither outlives the process.
        window.Closed += (_, _) =>
        {
            Run(harness.DisposeAsync().AsTask());
            Run(context.DisposeAsync().AsTask());
            application.Resources.MergedDictionaries.Clear();
        };

        return (strip, left, right, window, harness.Shell);
    }

    /// <summary>
    /// Waits for a task while the dispatcher keeps running.
    /// </summary>
    /// <remarks>
    /// Blocking on it instead deadlocks: the shell awaits with ConfigureAwait(true), so each
    /// continuation is posted back to this thread's dispatcher, and a thread parked in GetResult
    /// never runs it. An earlier test in the run has already shown a window, which is what installs
    /// the dispatcher synchronization context this depends on.
    /// </remarks>
    private static void Run(Task task)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        _ = task.ContinueWith(
            _ => frame.Continue = false,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void Pump()
    {
        for (int i = 0; i < 8; i += 1)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }
}
