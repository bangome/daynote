using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Daynote.Core.Notes;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Selected body text stays readable.
/// </summary>
/// <remarks>
/// Reported as "the text is hard to see when I drag over it", and the cause is the two-layer
/// editor: the coloured glyphs belong to a TextBlock underneath, while the TextBox on top draws no
/// text of its own (its Foreground is Transparent) and exists for the caret, input and selection.
/// Its selection rectangle therefore lands *over* the glyphs and nothing redraws them.
/// <para>
/// Asserted on pixels because that is the only place the bug exists: every property involved can be
/// set correctly while the result is still an unreadable block, since the layering is what decides
/// it.
/// </para>
/// <para>
/// Two earlier versions of this test passed with the fix reverted, and both were the same mistake —
/// counting a colour somewhere it also occurs innocently. First, counting the glyph colour across
/// the frame: light's selected-text colour is #FFFFFF, the card, and dark's is #121316, the page.
/// Both are everywhere. Then, counting inside the bounding box of the selection colour: that colour
/// *is* the accent, so the box stretched from the sidebar's add button to the right rail and
/// swallowed the card again.
/// </para>
/// <para>
/// What holds is below: look only inside the editor, and only at the span each row's selection
/// actually covers. A band is painted as one solid run, so a pixel of another colour between its
/// first and last column was drawn on top of it — which is the thing being tested.
/// </para>
/// </remarks>
[TestClass]
public sealed class EditorSelectionTests
{
    private static readonly Color LightSelectionBg = Color.Parse("#FF0067C0");
    private static readonly Color LightSelectionText = Color.Parse("#FFFFFFFF");
    private static readonly Color DarkSelectionBg = Color.Parse("#FF4CC2FF");
    private static readonly Color DarkSelectionText = Color.Parse("#FF121316");

    [TestMethod]
    public void Selected_text_is_drawn_over_the_selection_in_light()
    {
        AssertSelectionIsLegible(dark: false, LightSelectionBg, LightSelectionText);
    }

    [TestMethod]
    public void Selected_text_is_drawn_over_the_selection_in_dark()
    {
        // The dark theme is the one that matters most: its accent is a pale blue, and white on it
        // measures 2.01:1. The palette inverts the other way here (page colour on the accent,
        // 9.26:1), so this also pins that the two themes do not share one foreground.
        AssertSelectionIsLegible(dark: true, DarkSelectionBg, DarkSelectionText);
    }

    private static void AssertSelectionIsLegible(bool dark, Color background, Color glyphs)
    {
        int bandPixels = 0;
        int glyphPixels = 0;

        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.IsDark = dark;
            shell.NewNoteCommand.Execute(null);
            Pump();

            // Long enough to wrap, so the selection is a band rather than a few characters.
            shell.Notes.EditorText = string.Join(
                '\n',
                "선택 영역에서도 글자가 보여야 합니다.",
                "The quick brown fox jumps over the lazy dog.",
                "-[] 할 일 하나 #태그");
            Wait(shell.Notes.FlushAsync(FlushReason.NoteChange));

            TextBox editor = window.FindControl<TextBox>("Editor")!;
            editor.Focus();
            editor.SelectAll();
            Pump(window);

            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.IsNotNull(frame, "Nothing rendered.");

            // The editor's own rectangle, so the accent elsewhere in the shell cannot be mistaken
            // for a selection.
            Point origin = editor.TranslatePoint(default, window)
                ?? throw new AssertFailedException("The editor is not in the window's visual tree.");
            var bounds = new PixelRect(
                (int)origin.X,
                (int)origin.Y,
                (int)editor.Bounds.Width,
                (int)editor.Bounds.Height);

            (bandPixels, glyphPixels) = Count(frame, bounds, background, glyphs);
        });

        Assert.IsGreaterThan(
            500,
            bandPixels,
            "The selection band is not painted inside the editor, so this is not measuring a selection.");

        // The point of the whole fix. Without a selection foreground the glyphs under the band are
        // simply covered, and this count is zero.
        Assert.IsGreaterThan(
            100,
            glyphPixels,
            "No glyphs are drawn over the selection — the selected text is invisible or washed out.");
    }

    /// <summary>
    /// Counts the selection band inside <paramref name="bounds"/>, and the glyph colour within the
    /// horizontal span the band covers on each row.
    /// </summary>
    /// <remarks>
    /// Per row rather than one bounding box: the box of a multi-line selection includes the ragged
    /// space past the end of each line, which is plain background — and the background is the same
    /// colour as the glyphs in both themes. The span between a row's first and last band pixel
    /// contains only the band and whatever was drawn over it.
    /// </remarks>
    private static (int Band, int Glyphs) Count(
        WriteableBitmap bitmap,
        PixelRect bounds,
        Color background,
        Color glyphs)
    {
        using ILockedFramebuffer pixels = bitmap.Lock();
        Assert.IsTrue(
            pixels.Format == PixelFormat.Bgra8888 || pixels.Format == PixelFormat.Rgba8888,
            $"Unexpected pixel format {pixels.Format}; the channel order below assumes 8-bit BGRA or RGBA.");
        bool rgba = pixels.Format == PixelFormat.Rgba8888;

        uint Packed(Color colour) => rgba
            ? colour.R | ((uint)colour.G << 8) | ((uint)colour.B << 16) | ((uint)colour.A << 24)
            : colour.B | ((uint)colour.G << 8) | ((uint)colour.R << 16) | ((uint)colour.A << 24);

        uint wantedBand = Packed(background);
        uint wantedGlyphs = Packed(glyphs);

        int top = Math.Max(0, bounds.Y);
        int bottom = Math.Min(pixels.Size.Height, bounds.Y + bounds.Height);
        int left = Math.Max(0, bounds.X);
        int right = Math.Min(pixels.Size.Width, bounds.X + bounds.Width);

        int bandCount = 0;
        int glyphCount = 0;
        byte[] row = new byte[pixels.RowBytes];

        for (int y = top; y < bottom; y += 1)
        {
            Marshal.Copy(pixels.Address + (y * pixels.RowBytes), row, 0, row.Length);

            int first = -1;
            int last = -1;
            for (int x = left; x < right; x += 1)
            {
                if (BitConverter.ToUInt32(row, x * 4) != wantedBand)
                {
                    continue;
                }

                bandCount += 1;
                if (first < 0)
                {
                    first = x;
                }

                last = x;
            }

            if (first < 0)
            {
                continue;
            }

            for (int x = first; x <= last; x += 1)
            {
                if (BitConverter.ToUInt32(row, x * 4) == wantedGlyphs)
                {
                    glyphCount += 1;
                }
            }
        }

        return (bandCount, glyphCount);
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "The note never saved.");
        work.GetAwaiter().GetResult();
    }

    private static void Pump(Window? window = null)
    {
        for (int i = 0; i < 20; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            window?.UpdateLayout();
            Thread.Sleep(5);
        }
    }
}
