using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Onboarding;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The shell and the sticky note render to real pixels under the headless platform, and the frames
/// are written out where a person can look at them.
/// </summary>
/// <remarks>
/// This is the replacement for the WPF showcase pipeline (docs/WINDOWS_ON_AVALONIA.md §7): not a
/// catalogue of fixtures with a contract table, just the running windows, drawn by Skia with no
/// window server, saved as PNG next to the test results. The assertions are deliberately coarse —
/// the frame exists, is the size asked for, is not a blank sheet, and the two theme variants differ —
/// because a pixel-exact oracle is the thing that made the showcase expensive to keep true. The
/// PNGs are the evidence; the assertions only guarantee the evidence is real.
/// <para>
/// Frames land in <c>frames/</c> under the test binary. The composition tests prove the bindings;
/// this proves the paint.
/// </para>
/// </remarks>
[TestClass]
public sealed class RenderedFrameTests
{
    private static readonly string FramesDirectory = Path.Combine(AppContext.BaseDirectory, "frames");

    [TestMethod]
    public void The_shell_renders_in_both_variants_and_they_differ()
    {
        FrameSummary light = default;
        FrameSummary dark = default;

        TestServices.WithInitialisedShell((window, shell) =>
        {
            // Through the view model, as the theme button does, so everything that depends on the
            // variant (the applier, the wordmark) follows — setting RequestedThemeVariant directly
            // repaints the palette and leaves the light wordmark on the dark ground.
            shell.IsDark = false;
            light = Capture(window, "shell-light");

            shell.IsDark = true;
            dark = Capture(window, "shell-dark");
            shell.IsDark = false;
        });

        AssertIsAPicture(light, 1256, 788);
        AssertIsAPicture(dark, 1256, 788);
        Assert.AreNotEqual(light.Hash, dark.Hash, "Light and dark rendered identical frames; the variant is not reaching the paint.");

        // The page colours the palette defines, sampled where nothing else is drawn: the frame's
        // corners. Light Bg1 is #F4F4F5, dark is #121316 (Daynote.Product.*.axaml). A corner that is
        // not the page means either the palette is not applied or the window is drawing chrome.
        Assert.AreEqual(Color.Parse("#FFF4F4F5"), light.BottomLeft, "Light frame's bottom-left corner is not the page colour.");
        Assert.AreEqual(Color.Parse("#FF121316"), dark.BottomLeft, "Dark frame's bottom-left corner is not the page colour.");
    }

    [TestMethod]
    public void An_empty_day_lists_no_note_row()
    {
        // Found in the first rendered frame: a fresh day carries one projection tab — the editor's
        // blank "노트 1" — and the sidebar drew it as a row while the header said "노트 0개" and the
        // empty-state text sat right under it. The WPF shell hides projection rows; so does this one now.
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Assert.IsTrue(shell.Notes.Tabs.Count > 0, "The fresh day has no tabs at all; the scenario is not the one described.");
            Assert.IsTrue(shell.Notes.Tabs.All(static tab => tab.IsProjection), "The fresh day already has a real note.");
            Assert.IsTrue(shell.IsDayEmpty, "The header does not call the day empty.");

            List<Button> visibleRows = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(static button => button.Classes.Contains("noterow") && button.IsVisible)
                .ToList();

            Assert.AreEqual(0, visibleRows.Count, "A projection is listed as a note row on an empty day.");
        });
    }

    [TestMethod]
    public void The_sticky_note_renders_its_own_chrome_once()
    {
        FrameSummary frame = default;

        HeadlessAppFixture.OnUiThread(() =>
        {
            var sticky = new StickyNoteWindow { Width = 320, Height = 280 };
            try
            {
                sticky.Show();
                frame = Capture(sticky, "sticky-note");
            }
            finally
            {
                sticky.Close();
            }
        });

        AssertIsAPicture(frame, 320, 280);

        // The defect this guards: the sticky once showed two title bars — its own strip and the
        // theme's. The app's strip is the same yellow as the note and carries only the pin and close
        // glyphs, so the top rows contain more than one colour (the glyphs) and the band right below
        // is flat note colour with nothing drawn on it. A second bar would put glyphs, a border or a
        // different fill into that band.
        Assert.IsTrue(
            frame.Rows.Take(30).Any(static colours => colours > 1),
            "No glyphs in the top 30 rows: the sticky's own strip is missing.");
        Assert.IsTrue(
            frame.Rows.Skip(34).Take(30).All(static colours => colours == 1),
            "Rows 34-63 are not flat note colour: something is painted where a second title bar would be.");
    }

    [TestMethod]
    public void The_tutorial_spotlight_cuts_a_visible_hole()
    {
        // The port's evidence: the calendar step should show the calendar undimmed inside a dimmed
        // shell. A frame where the hole is not actually transparent looks like a flat scrim.
        FrameSummary frame = default;

        TestServices.WithInitialisedShell((window, shell) =>
        {
            TutorialOverlay overlay = window.GetVisualDescendants().OfType<TutorialOverlay>().Single();
            var model = (TutorialViewModel)overlay.DataContext!;
            model.Open();
            model.Index = model.Steps
                .Select(static (step, index) => (step, index))
                .First(static pair => pair.step.TargetName == TutorialTargets.Calendar)
                .index;

            overlay.IsVisible = true;
            window.UpdateLayout();
            for (int i = 0; i < 10; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            frame = Capture(window, "tutorial-spotlight");
        });

        AssertIsAPicture(frame, 1256, 788);

        // The bottom-left corner is outside the calendar, so it is dimmed rather than the page colour
        // the un-dimmed shell shows there (#F4F4F5 — see the shell test above).
        Assert.AreNotEqual(
            Color.Parse("#FFF4F4F5"),
            frame.BottomLeft,
            "The corner is the undimmed page colour, so the scrim is not covering the shell.");
    }

    [TestMethod]
    public void The_search_dropdown_opens_over_the_shell()
    {
        // Reported as "the autocomplete does not expand". The panel is bound to Search.IsOpen and
        // floats over the body with a ZIndex, so the two ways it can fail are the flag never turning
        // on and the panel being covered; a frame shows both.
        FrameSummary frame = default;
        int rows = 0;
        bool open = false;

        TestServices.WithInitialisedShell((window, shell) =>
        {
            // A real, persisted note: the search reads the database, not the editor buffer.
            shell.NewNoteCommand.Execute(null);
            Pump();
            shell.Notes.EditorText = "회의록 초안과 다음 주 일정";
            // Pumped rather than blocked: the flush posts its continuation to this dispatcher, so
            // waiting on it here would deadlock — the same trap as the probe in §5.
            Task flush = shell.Notes.FlushAsync(Daynote.Core.Notes.FlushReason.NoteChange);
            for (int i = 0; i < 200 && !flush.IsCompleted; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }

            Assert.IsTrue(flush.IsCompleted, "The note never saved, so there is nothing to search.");

            shell.Search.Query = "회의";

            for (int i = 0; i < 60 && shell.Search.Results.Count == 0; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(25);
            }

            open = shell.Search.IsOpen;
            rows = shell.Search.Results.Count;
            window.UpdateLayout();
            frame = Capture(window, "search-dropdown");
        });

        Assert.IsTrue(open, "Typing a query did not open the dropdown.");
        Assert.IsGreaterThan(0, rows, "The query matched nothing, so the dropdown had nothing to show.");
        AssertIsAPicture(frame, 1256, 788);
    }

    /// <summary>Lets the async commands' continuations run on this dispatcher.</summary>
    private static void Pump()
    {
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static FrameSummary Capture(Window window, string name)
    {
        // Layout and paint happen on the render timer, which a headless run has to tick by hand.
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: no frame was rendered. Is headless drawing still stubbed?");

        Directory.CreateDirectory(FramesDirectory);
        string path = Path.Combine(FramesDirectory, name + ".png");
        bitmap.Save(path, new PngBitmapEncoderOptions());

        using ILockedFramebuffer pixels = bitmap.Lock();
        Assert.IsTrue(
            pixels.Format == PixelFormat.Bgra8888 || pixels.Format == PixelFormat.Rgba8888,
            $"Unexpected pixel format {pixels.Format}; the channel order below assumes 8-bit BGRA or RGBA.");
        bool rgba = pixels.Format == PixelFormat.Rgba8888;

        int width = pixels.Size.Width;
        int height = pixels.Size.Height;
        var distinct = new HashSet<uint>();
        var hash = new HashCode();
        var rows = new int[height];
        byte[] row = new byte[pixels.RowBytes];

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(pixels.Address + (y * pixels.RowBytes), row, 0, row.Length);
            var inRow = new HashSet<uint>();
            for (int x = 0; x < width; x++)
            {
                uint packed = BitConverter.ToUInt32(row, x * 4);
                distinct.Add(packed);
                inRow.Add(packed);
                hash.Add(packed);
            }

            rows[y] = inRow.Count;
        }

        return new FrameSummary(
            path,
            width,
            height,
            distinct.Count,
            hash.ToHashCode(),
            Pixel(pixels, 2, height - 3, rgba),
            rows);
    }

    private static Color Pixel(ILockedFramebuffer pixels, int x, int y, bool rgba)
    {
        byte[] c = new byte[4];
        Marshal.Copy(pixels.Address + (y * pixels.RowBytes) + (x * 4), c, 0, 4);
        return rgba
            ? Color.FromArgb(c[3], c[0], c[1], c[2])
            : Color.FromArgb(c[3], c[2], c[1], c[0]);
    }

    private static void AssertIsAPicture(FrameSummary frame, int width, int height)
    {
        Assert.AreEqual(width, frame.Width, $"{frame.Path}: wrong width.");
        Assert.AreEqual(height, frame.Height, $"{frame.Path}: wrong height.");
        Assert.IsGreaterThan(
            16,
            frame.DistinctColours,
            $"{frame.Path}: only {frame.DistinctColours} distinct colours — a blank or flat frame, not the UI.");
        Assert.IsTrue(File.Exists(frame.Path), $"{frame.Path} was not written.");
    }

    /// <param name="Rows">Distinct colours per row, top to bottom; 1 means a flat row.</param>
    private readonly record struct FrameSummary(
        string Path,
        int Width,
        int Height,
        int DistinctColours,
        int Hash,
        Color BottomLeft,
        int[] Rows);
}
