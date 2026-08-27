using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using Daynote.App.Shell.Product;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

/// <summary>
/// The editor card draws its text twice: a transparent <c>TextBox</c> owns the caret and the editing,
/// and a <c>TextBlock</c> under it owns the visible glyphs. They are separate scrollers, so if their
/// offsets ever diverge the caret is drawn on one line while typing lands on another. These tests pin
/// the sync, including the case that used to break it.
/// </summary>
[TestClass]
public sealed class EditorHighlightScrollTests
{
    private const double Width = 520;
    private const double Height = 320;

    [STATestMethod]
    public void The_glyph_layer_follows_the_editor_when_it_scrolls()
    {
        (ScrollViewer highlight, TextBox body, Window window) = Compose();
        try
        {
            body.ScrollToEnd();
            Pump();

            Assert.AreNotEqual(
                0d,
                body.VerticalOffset,
                $"must scroll: body.Actual={body.ActualHeight:F1} extent={body.ExtentHeight:F1} " +
                $"viewport={body.ViewportHeight:F1} lines={body.LineCount} highlight.ext={highlight.ExtentHeight:F1}");
            AssertAligned(body, highlight);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void The_glyph_layer_is_aligned_again_after_it_is_rebuilt()
    {
        (ScrollViewer highlight, TextBox body, Window window) = Compose();
        try
        {
            body.ScrollToEnd();
            Pump();

            // The highlight layer is rebuilt on every keystroke. This walks the shape that can strand
            // it: mirror an offset while the layer measures short (ScrollToVerticalOffset clamps
            // silently), then restore its content.
            //
            // NOTE: this does NOT fail when the reissue-on-extent-change guard is removed - the
            // editor's own ScrollChanged re-aligns it in this synthetic sequence. It is kept as an
            // alignment invariant, not as a regression test for that guard.
            var block = (TextBlock)highlight.Content;
            string text = body.Text;
            block.Inlines.Clear();
            Pump();   // let the emptied layer measure short, which is what makes the clamp possible

            highlight.ScrollToVerticalOffset(body.VerticalOffset);
            Assert.AreEqual(0d, highlight.VerticalOffset, "an empty layer cannot scroll; the request is clamped");

            block.Inlines.Add(new Run(text));
            Pump();

            AssertAligned(body, highlight);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertAligned(TextBox body, ScrollViewer highlight)
    {
        Assert.AreEqual(
            body.VerticalOffset,
            highlight.VerticalOffset,
            0.5,
            $"the glyph layer is {body.VerticalOffset - highlight.VerticalOffset:F2}px from the caret layer");
    }

    /// <summary>Builds the real editor card and gives back the two layers that have to stay aligned.</summary>
    private static (ScrollViewer Highlight, TextBox Body, Window Window) Compose()
    {
        Application application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        application.Resources["Daynote.Convert.BoolToVisibility"] = new BooleanToVisibilityConverter();
        application.Resources["Daynote.Convert.InverseBool"] = new Daynote.App.Shell.InverseBooleanConverter();
        application.Resources["Daynote.Convert.InverseBoolToVisibility"] = new Daynote.App.Shell.InverseBoolToVisibilityConverter();
        application.Resources["Daynote.Convert.EqualsToVisibility"] = new Daynote.App.Shell.EqualsToVisibilityConverter();
        application.Resources["Daynote.Convert.EqualsToBool"] = new Daynote.App.Shell.EqualsToBooleanConverter();
        application.Resources["Daynote.Convert.NullToVisibility"] = new Daynote.App.Shell.NullToVisibilityConverter();
        application.Resources["Daynote.Convert.NullToCollapsed"] = new Daynote.App.Shell.NullToVisibilityConverter { Invert = true };

        var card = new EditorCardView();
        var window = new Window
        {
            Width = Width + 40,
            Height = Height + 80,
            Left = -4000,
            ShowInTaskbar = false,
            Content = new Grid { Width = Width, Height = Height, Children = { card } },
        };
        window.Show();

        var body = (TextBox)card.FindName("BodyBox");
        var highlight = (ScrollViewer)card.FindName("HighlightScroll");
        Assert.IsNotNull(body);
        Assert.IsNotNull(highlight);

        // BodyBox.Text is bound to the shell's buffer; these tests are about the two layers, not
        // the binding, so drive the control directly. Its own handlers stay wired.
        BindingOperations.ClearBinding(body, TextBox.TextProperty);
        body.Text = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"{i:00} 한글 줄입니다"));
        Pump();
        return (highlight, body, window);
    }

    private static void Pump()
    {
        for (int i = 0; i < 8; i += 1)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { }));
        }
    }
}
