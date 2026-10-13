using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.Core.Notes;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The day's note tabs, when there are more than fit: no scroll bar, a wheel turned up or down moves
/// them sideways, and a round step shows at each end that has tabs past it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NoteTabStripTests
{
    [TestMethod]
    public void An_overflowing_strip_scrolls_by_wheel_and_steps_without_a_bar()
    {
        TestServices.WithInitialisedShell(1440, 900, (window, shell) =>
        {
            for (int i = 1; i <= 16; i += 1)
            {
                Wait(shell.NewNoteCommand.ExecuteAsync(null));
                Pump(window);
                shell.Notes.EditorText = $"본문 {i}";
                Wait(shell.Notes.FlushAsync(FlushReason.NoteChange));
                Wait(shell.Notes.RenameAsync(shell.Notes.SelectedTab, $"꽤 긴 노트 제목 {i}"));
            }

            Wait(shell.Notes.SelectNoteAsync(shell.Notes.Tabs.First(tab => !tab.IsProjection)));
            Pump(window);

            ScrollViewer strip = Named<ScrollViewer>(window, "NoteTabsScroll");
            Panel left = Named<Panel>(window, "NoteTabsLeft");
            Panel right = Named<Panel>(window, "NoteTabsRight");
            strip.Offset = default;
            Pump(window);

            Assert.IsTrue(strip.Extent.Width > strip.Viewport.Width, "Sixteen tabs fit; the test proves nothing.");
            Assert.IsFalse(window.GetVisualDescendants().OfType<ScrollBar>()
                .Any(bar => bar.IsEffectivelyVisible && bar.FindAncestorOfType<ScrollViewer>() == strip),
                "The tab strip shows a scroll bar.");
            Assert.IsFalse(left.IsVisible, "A step shows at the start with nothing before it.");
            Assert.IsTrue(right.IsVisible, "No step shows at the end with tabs past it.");
            Shoot(window, "note-tabs-start");

            Point centre = strip.TranslatePoint(new Point(strip.Bounds.Width / 2, strip.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(centre, new Vector(0, -1));
            Pump(window);
            Assert.IsTrue(strip.Offset.X > 0, "A wheel turned down does not move the tabs.");
            Assert.IsTrue(left.IsVisible, "No step shows at the start once tabs are before it.");

            double afterWheel = strip.Offset.X;
            window.MouseWheel(centre, new Vector(0, 1));
            Pump(window);
            Assert.IsTrue(strip.Offset.X < afterWheel, "A wheel turned up does not move the tabs back.");

            Click(right.GetVisualDescendants().OfType<Button>().Single());
            Assert.IsTrue(strip.Offset.X > strip.Viewport.Width * 0.5, "The end step does not page.");
            Shoot(window, "note-tabs-middle");

            for (int i = 0; i < 20 && right.IsVisible; i += 1)
            {
                Click(right.GetVisualDescendants().OfType<Button>().Single());
            }

            Assert.IsFalse(right.IsVisible, "The end step stays at the end.");
            Assert.AreEqual(strip.Extent.Width - strip.Viewport.Width, strip.Offset.X, 0.5);
        });

        void Click(Button button)
        {
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump(TopLevel.GetTopLevel(button) as Window);
        }
    }

    private static T Named<T>(Window window, string name)
        where T : Control
    {
        Pump(window);
        return window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    }

    private static void Shoot(Window window, string name)
    {
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        string directory = Environment.GetEnvironmentVariable("DAYNOTE_FRAMES_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "frames", "note-tabs");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
        work.GetAwaiter().GetResult();
    }

    private static void Pump(Window? window)
    {
        for (int i = 0; i < 20; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            window?.UpdateLayout();
            Thread.Sleep(5);
        }
    }
}
