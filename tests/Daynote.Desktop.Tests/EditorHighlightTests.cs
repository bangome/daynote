using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Desktop.Composition;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The editor's highlight layer, and its alignment with the editor over it.
/// </summary>
/// <remarks>
/// The body is a transparent-foreground <c>TextBox</c> over a <c>TextBlock</c> that draws the same
/// string with the shapes the app understood picked out. The illusion holds only while both layers
/// break lines in the same places — the WPF editor shipped this same trick and shipped a bug with
/// it: typing appeared one line above the caret, because its TextBox template gives the inner text
/// view a 2px margin the TextBlock does not have.
/// <para>
/// So the geometry is asserted, not assumed. The headless text shaper is not the real one, which
/// makes the absolute numbers meaningless — but both layers go through the same shaper, so a
/// difference between them is real, and a constant width offset is exactly the fault that bit WPF.
/// </para>
/// </remarks>
[TestClass]
public sealed class EditorHighlightTests
{
    [TestMethod]
    public void The_two_layers_are_the_same_width()
    {
        WithEditor((shell, editor, highlight) =>
        {
            Assert.AreEqual(
                editor.Bounds.Width,
                highlight.Bounds.Width,
                0.5,
                "A width difference moves the wrap points and the caret drifts off its glyph.");
        });
    }

    [TestMethod]
    public void A_wrapping_body_takes_the_same_height_in_both()
    {
        WithEditor((shell, editor, highlight) =>
        {
            // Long enough to wrap several times at the card's width.
            SetBody(shell, editor, highlight, string.Join(' ', Enumerable.Repeat("회의록 초안 항목", 60)));

            Assert.AreEqual(
                editor.Bounds.Height,
                highlight.Bounds.Height,
                1.0,
                "The layers disagree about how many lines the same text takes.");
        });
    }

    [TestMethod]
    public void The_understood_shapes_are_marked_and_the_rest_is_not()
    {
        WithEditor((shell, editor, highlight) =>
        {
            SetBody(shell, editor, highlight, "-[] 장부 정리 (9/7 15:00) #회계 https://example.com 보통 글자");

            Assert.IsGreaterThan(
                0,
                MarkedRuns(highlight, marked: false).Count(),
                "Ordinary text should be left to inherit rather than marked.");

            string[] marked = [.. MarkedRuns(highlight, marked: true).Select(r => r.Text ?? string.Empty)];
            CollectionAssert.AreEqual(
                new[] { "-[]", "(9/7 15:00)", "#회계", "https://example.com" },
                marked,
                $"Marked: {string.Join(" | ", marked)}");
        });
    }

    [TestMethod]
    public void The_layer_holds_the_editor_s_trailing_blank_line()
    {
        WithEditor((shell, editor, highlight) =>
        {
            SetBody(shell, editor, highlight, "한 줄\n");

            Assert.AreEqual(
                editor.Bounds.Height,
                highlight.Bounds.Height,
                1.0,
                "A body ending in a newline leaves the editor one line taller than the block.");
        });
    }

    /// <summary>
    /// The runs the highlighter marked, or the ones it left alone. Asked as "did we set a brush on
    /// it", not "is its brush null": Avalonia hands back the inherited value for a property nobody
    /// set, so an unmarked run still reports a foreground.
    /// </summary>
    private static IEnumerable<Run> MarkedRuns(TextBlock highlight, bool marked) =>
        (highlight.Inlines ?? [])
            .OfType<Run>()
            .Where(run => run.IsSet(TextElement.ForegroundProperty) == marked);

    /// <summary>
    /// Puts a body in the editor through the view model — the path the app uses — and checks it
    /// stuck. Assigning <c>TextBox.Text</c> directly is written back over by the two-way binding, and
    /// an empty editor would make every measurement below agree for the wrong reason.
    /// </summary>
    private static void SetBody(DesktopShellViewModel shell, TextBox editor, TextBlock highlight, string body)
    {
        shell.Notes.EditorText = body;
        editor.UpdateLayout();
        highlight.UpdateLayout();

        Assert.AreEqual(body, editor.Text, "The body never reached the editor.");
        Assert.IsGreaterThan(0, (highlight.Inlines ?? []).Count, "The highlight layer was not rebuilt.");
    }

    /// <summary>Composes the shell, lays the window out, and hands over the two body layers.</summary>
    private static void WithEditor(Action<DesktopShellViewModel, TextBox, TextBlock> body)
    {
        string dataRoot = Path.Combine(Path.GetTempPath(), "daynote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        HeadlessAppFixture.OnUiThread(() =>
        {
            Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", dataRoot);
            Application application = Application.Current!;
            var services = new ServiceCollection();
            services.AddDaynoteDesktop(DaynoteAppOptions.ForCurrentUser(), application, () => null, () => { });
            ServiceProvider provider = services.BuildServiceProvider();
            var shell = provider.GetRequiredService<DesktopShellViewModel>();

            var window = new MainWindow { DataContext = shell };
            try
            {
                window.Show();
                window.Measure(new Size(1240, 780));
                window.Arrange(new Rect(0, 0, 1240, 780));
                window.UpdateLayout();

                var editor = Find<TextBox>(window, "Editor");
                var highlight = Find<TextBlock>(window, "Highlight");
                Assert.IsGreaterThan(0, editor.Bounds.Width, "The editor did not lay out.");

                body(shell, editor, highlight);
            }
            finally
            {
                window.Close();
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });

        try
        {
            Directory.Delete(dataRoot, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle can outlive the test by a moment.
        }
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
}
