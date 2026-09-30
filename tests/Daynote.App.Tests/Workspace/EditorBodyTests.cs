using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Daynote.App.Shell.Product;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace Daynote.App.Tests.Workspace;

/// <summary>
/// The note body: the design's line height, and marks that stay legible when the theme flips.
/// </summary>
/// <remarks>
/// Both of these are properties of the two-layer editor. The caret belongs to a transparent text box
/// and the glyphs to a text block underneath, so the line height has to reach both — and WPF gives
/// only the block a <c>LineHeight</c>, which is why it travels on the face instead. The marks are
/// runs in that block, and a run holding a brush object rather than a reference to one keeps its
/// colour through a palette swap.
/// </remarks>
[TestClass]
public sealed class EditorBodyTests
{
    private const double FontSize = 15.5;

    /// <summary>
    /// The design's own numbers, written out rather than read from the shell. Asking DeskFonts what
    /// the line height is and then checking the layout against that answer would pass for any value
    /// it happened to hold, which is the one thing this test exists to catch.
    /// </summary>
    private const double DesignLineHeight = 29.45;

    [STATestMethod]
    public void The_body_is_set_on_the_designs_line_height()
    {
        (ScrollViewer highlight, TextBox body, Window window) = Compose("첫 줄\n둘째 줄\n셋째 줄");
        try
        {
            // The design's 1.9 puts the lines 29.45 apart; the face's own spacing is nearer 1.2,
            // which is the cramped body this replaces. The two are far enough apart that the
            // tolerance below cannot hide one for the other.
            const double perLine = DesignLineHeight;
            Assert.AreEqual(
                perLine,
                body.ExtentHeight / body.LineCount,
                1.0,
                $"The editor lays out {body.ExtentHeight / body.LineCount:F2}px per line, not the design's {perLine:F2}.");

            // And the glyph layer agrees, or the caret stops landing on its own character.
            Assert.AreEqual(
                body.ExtentHeight,
                highlight.ExtentHeight - HighlightBottomPadding(highlight),
                1.0,
                "The two layers disagree about how tall the text is.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void A_mark_takes_the_accent_of_whichever_theme_is_on()
    {
        const string Link = "https://example.com";
        (ScrollViewer highlight, _, Window window) = Compose($"회의록 {Link} 참고");
        try
        {
            Application application = Application.Current!;
            Run mark = Mark((TextBlock)highlight.Content, Link);

            Assert.AreEqual(Accent(application), Ink(mark), "The mark is not the light accent.");

            new WpfProductThemeApplier(application, highContrast: false).Apply(dark: true);

            Assert.AreEqual(
                Accent(application),
                Ink(mark),
                "The mark kept the colour it was written in. Over the other theme's page that is very "
                + "nearly the page itself, and the mark disappears.");
        }
        finally
        {
            window.Close();
            new WpfProductThemeApplier(Application.Current!, highContrast: false).Apply(dark: false);
        }
    }

    /// <summary>The run the highlighter coloured: the link in the sample text.</summary>
    private static Run Mark(TextBlock highlight, string text)
    {
        Run? run = highlight.Inlines.OfType<Run>().FirstOrDefault(r => r.Text == text);
        Assert.IsNotNull(run, $"The highlighter did not mark '{text}'.");
        return run;
    }

    private static Color Accent(Application application) =>
        ((SolidColorBrush)application.TryFindResource("Daynote.Product.Brush.Accent")).Color;

    private static Color Ink(Run run) => ((SolidColorBrush)run.Foreground).Color;

    private static double HighlightBottomPadding(ScrollViewer highlight) =>
        ((TextBlock)highlight.Content).Padding.Bottom;

    private static (ScrollViewer Highlight, TextBox Body, Window Window) Compose(string text)
    {
        Application application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        new WpfProductThemeApplier(application, highContrast: false).Apply(dark: false);
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
            Width = 900,
            Height = 600,
            Left = -4000,
            ShowInTaskbar = false,
            Content = new Grid { Width = 860, Height = 560, Children = { card } },
        };
        window.Show();

        var body = (TextBox)card.FindName("BodyBox");
        var highlight = (ScrollViewer)card.FindName("HighlightScroll");
        BindingOperations.ClearBinding(body, TextBox.TextProperty);
        body.Text = text;
        Pump();
        return (highlight, body, window);
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
