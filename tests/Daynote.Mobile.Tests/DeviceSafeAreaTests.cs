using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.Core.Domain;
using Daynote.Mobile.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Every screen, laid out on the handsets whose notches and bottom strips differ the most, keeps
/// what can be tapped out of the notch, the status bar, the home indicator and the navigation bar.
/// </summary>
/// <remarks>
/// <para>
/// The safe areas are the platforms' own figures in points: a Dynamic Island iPhone reports 62
/// above and 34 below, an iPhone SE only its 20-point status bar, a Pixel with gesture navigation
/// 52 and 24, and with the three-button bar 52 and 48. Turned sideways the island moves to a side
/// (62 left and right, 21 below), and Android's button bar goes to the right-hand edge.
/// </para>
/// <para>
/// A button counts only as far as it is on screen: clipped by the scroll view it sits in, a row
/// scrolled under the tab bar is not a row in the home indicator. Nothing is allowed across the
/// line, the floating tab bar included: off a device nothing clips there, but a phone does, and the
/// design's 4-point dip into the strip came out as a bar with its round bottom cut flat.
/// Frames land beside the other screen renders, under <c>artifacts/mobile-screens/devices</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class DeviceSafeAreaTests
{
    private static readonly string OutputDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "mobile-screens", "devices");

    [TestMethod]
    [DataRow("iphone-16-pro", 402, 874, 0, 62, 0, 34)]
    [DataRow("iphone-se", 375, 667, 0, 20, 0, 0)]
    [DataRow("pixel-gesture", 412, 915, 0, 52, 0, 24)]
    [DataRow("pixel-3-button", 412, 915, 0, 52, 0, 48)]
    [DataRow("iphone-16-pro-landscape", 874, 402, 62, 0, 62, 21)]
    [DataRow("pixel-3-button-landscape", 915, 412, 0, 24, 48, 0)]
    public void Nothing_tappable_sits_under_the_notch_or_the_bottom_strip(
        string device, double width, double height, double left, double top, double right, double bottom)
    {
        var safe = new Thickness(left, top, right, bottom);
        string directory = Path.Combine(OutputDirectory, device);
        Directory.CreateDirectory(directory);
        var failures = new List<string>();

        TestServices.WithInitialisedShell(width, height, (view, shell) =>
        {
            view.PreviewSafeArea = safe;
            LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));
            ScreenshotTests.Seed(shell, today);

            void Check(string screen)
            {
                Settle(view);
                Save(view, Path.Combine(directory, $"{screen}.png"));
                failures.AddRange(Offenders(view, safe).Select(offender => $"{device}/{screen}: {offender}"));
            }

            ScreenshotTests.SeedFiles(shell);
            shell.GoToPageCommand.Execute(MobilePage.Day);
            Check("day");

            // The files section sits under the to-dos, below the fold: scrolled to, its rows count.
            ScrollDayToEnd(view);
            Check("day-files");
            ScrollDayToStart(view);

            ScreenshotTests.Pump(() => shell.Notes.SelectNoteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
            shell.IsEditorOpen = true;
            Check("editor");
            shell.IsEditorOpen = false;

            shell.OpenAttachSheetCommand.Execute(null);
            Check("attach-sheet");
            shell.CloseAttachSheetCommand.Execute(null);

            shell.DayFiles[0].ShowMenuCommand.Execute(null);
            Check("file-menu");
            shell.RequestDeleteFileCommand.Execute(null);
            Check("file-confirm");
            shell.CloseFileMenuCommand.Execute(null);

            ScreenshotTests.Pump(() => shell.DayFiles.First(row => row.Item.IsImage).OpenCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsImageViewerOpen, $"{device}: the picture did not open in the viewer.");
            Check("viewer");
            shell.CloseImageViewerCommand.Execute(null);

            shell.GoToPageCommand.Execute(MobilePage.Lists);
            Check("lists");

            shell.GoToPageCommand.Execute(MobilePage.Search);
            Check("search");

            shell.GoToPageCommand.Execute(MobilePage.Settings);
            Check("settings");

            shell.OpenAccountCommand.Execute(null);
            Check("account");
            shell.CloseAccountCommand.Execute(null);

            shell.GoToPageCommand.Execute(MobilePage.Day);
            shell.OpenMonthPickerCommand.Execute(null);
            Check("sheet");
            shell.CloseMonthPickerCommand.Execute(null);
        });

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    private static ScrollViewer DayScroller(Control view) =>
        view.GetVisualDescendants().OfType<Views.DayPage>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();

    private static void ScrollDayToEnd(Control view)
    {
        Settle(view);
        DayScroller(view).ScrollToEnd();
    }

    private static void ScrollDayToStart(Control view) => DayScroller(view).ScrollToHome();

    /// <summary>Visible buttons whose on-screen part crosses into an inset.</summary>
    private static IEnumerable<string> Offenders(Control view, Thickness safe)
    {
        if (TopLevel.GetTopLevel(view) is not { } root)
        {
            yield break;
        }

        Size size = root.ClientSize;
        var dock = view.FindControl<Grid>("Dock");

        // The tab bar as a whole, not only its buttons: a device clips whatever crosses into the
        // bottom strip, and the bar's round lower edge was cut flat on an iPhone while every button
        // above it still passed.
        if (dock is { IsEffectivelyVisible: true } && dock.TranslatePoint(default, root) is { } dockAt &&
            dockAt.Y + dock.Bounds.Height > size.Height - safe.Bottom + 0.5)
        {
            yield return $"the tab bar reaches {dockAt.Y + dock.Bounds.Height:0.#}, past the safe area's bottom {size.Height - safe.Bottom:0.#}";
        }

        foreach (Button button in view.GetVisualDescendants().OfType<Button>())
        {
            if (!button.IsEffectivelyVisible || button.Bounds.Width <= 0 || button.Bounds.Height <= 0 ||
                OnScreen(button, root) is not { } rect)
            {
                continue;
            }

            bool inDock = dock is not null && button.GetVisualAncestors().Contains(dock);
            double bottomLimit = size.Height - safe.Bottom;

            if (rect.Top < safe.Top - 0.5 || rect.Left < safe.Left - 0.5 ||
                rect.Right > size.Width - safe.Right + 0.5 || rect.Bottom > bottomLimit + 0.5)
            {
                string label = button.Name ?? (button.Content as string) ?? AutomationName(button) ?? button.GetType().Name;
                yield return $"'{label}' at {rect} outside the safe area {safe} of {size}";
            }
        }
    }

    /// <summary>The part of a control inside every scroll view it sits in, in window coordinates; null when none is.</summary>
    private static Rect? OnScreen(Visual control, Visual root)
    {
        if (control.TranslatePoint(default, root) is not { } origin)
        {
            return null;
        }

        var rect = new Rect(origin, control.Bounds.Size);
        foreach (ScrollViewer scroller in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (scroller.TranslatePoint(default, root) is not { } at)
            {
                continue;
            }

            rect = rect.Intersect(new Rect(at, scroller.Bounds.Size));
        }

        return rect.Width > 0.5 && rect.Height > 0.5 ? rect : null;
    }

    private static string? AutomationName(Control control) =>
        Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name ? name : null;

    private static void Settle(Control view)
    {
        for (int i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
        }
    }

    private static void Save(Control view, string path)
    {
        using WriteableBitmap? frame = (TopLevel.GetTopLevel(view) as Window)?.CaptureRenderedFrame();
        frame?.Save(path, new PngBitmapEncoderOptions());
    }
}
