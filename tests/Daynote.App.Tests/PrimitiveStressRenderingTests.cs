using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WpfThumb = System.Windows.Controls.Primitives.Thumb;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace Daynote.App.Tests;

[TestClass]
public sealed class PrimitiveStressRenderingTests
{
    private const string LongTitle =
        "A deliberately long note title wraps across two lines while every adjacent action stays visible";

    [STATestMethod]
    [DataRow("compact.app-shell.default", 760d, 600d)]
    [DataRow("regular.app-shell.default", 1000d, 720d)]
    [DataRow("wide.app-shell.default", 1586d, 992d)]
    public void LongNoteTitle_UsesTwoBoundedLinesAndKeepsNamedActionsFixed(
        string pageId,
        double width,
        double height)
    {
        var surface = Compose(pageId, ShowcaseStress.Long, width, height);
        var title = Descendants(surface).OfType<TextBlock>().Single(text =>
            text.Text == LongTitle && AutomationProperties.GetName(text) == LongTitle);
        var titleBounds = Bounds(title, surface);
        var close = NamedButton(surface, $"Close {LongTitle}");
        var add = NamedButton(surface, "Add note");
        var primaryTarget = ShowcaseResources.Get<double>("Daynote.Size.Target.Primary");

        Assert.AreEqual(TextWrapping.Wrap, title.TextWrapping);
        Assert.AreEqual(TextTrimming.None, title.TextTrimming);
        Assert.AreEqual(LongTitle, AutomationProperties.GetName(title));
        Assert.IsGreaterThan(title.LineHeight, title.ActualHeight);
        Assert.IsLessThanOrEqualTo((2 * title.LineHeight) + 0.5, title.ActualHeight);
        Assert.IsLessThanOrEqualTo(title.MaxHeight + 0.5, title.ActualHeight);
        foreach (var action in new[] { close, add })
        {
            var actionBounds = Bounds(action, surface);
            Assert.IsGreaterThanOrEqualTo(primaryTarget, action.ActualWidth, AutomationProperties.GetName(action));
            Assert.IsGreaterThanOrEqualTo(primaryTarget, action.ActualHeight, AutomationProperties.GetName(action));
            Assert.IsLessThanOrEqualTo(actionBounds.Left + 0.5, titleBounds.Right,
                $"{AutomationProperties.GetName(action)} displaced or overlapped the bounded title.");
        }
        Assert.IsLessThan(Bounds(add, surface).Left, Bounds(close, surface).Left,
            "Close must remain before Add in the fixed action cluster.");
    }

    [STATestMethod]
    [DataRow("compact.status-banner.error", 760d, 600d)]
    [DataRow("regular.status-banner.error", 1000d, 720d)]
    [DataRow("wide.status-banner.error", 1586d, 992d)]
    public void LongErrorStatus_WrapsWithoutEllipsisOrRetryDisplacement(
        string pageId,
        double width,
        double height)
    {
        var surface = Compose(pageId, ShowcaseStress.Long, width, height);
        var message = Descendants(surface).OfType<TextBlock>()
            .Single(text => AutomationProperties.GetName(text) == "Error status message");
        var retry = NamedButton(surface, "Retry status action");
        var messageBounds = Bounds(message, surface);
        var retryBounds = Bounds(retry, surface);

        Assert.AreEqual(TextWrapping.Wrap, message.TextWrapping);
        Assert.AreEqual(TextTrimming.None, message.TextTrimming);
        Assert.IsGreaterThan(message.LineHeight, message.ActualHeight);
        Assert.IsLessThanOrEqualTo(retryBounds.Left + 0.5, messageBounds.Right);
        Assert.IsTrue(Bounds(surface, surface).Contains(retryBounds));
        Assert.AreEqual(Visibility.Visible, retry.Visibility);
    }

    [STATestMethod]
    [DataRow("compact.calendar-day.default", 760d, 600d)]
    [DataRow("regular.calendar-day.default", 1000d, 720d)]
    [DataRow("wide.calendar-day.default", 1200d, 720d)]
    public void StandaloneCalendarDay_MeasuresOneCenteredPrimaryCell(
        string pageId,
        double width,
        double height)
    {
        var surface = Compose(pageId, ShowcaseStress.Default, width, height);
        var day = Descendants(surface).OfType<WpfToggleButton>().Single();
        var target = ShowcaseResources.Get<double>("Daynote.Size.Target.Primary");
        var bounds = Bounds(day, surface);

        Assert.AreEqual(target, day.ActualWidth, 0.01);
        Assert.AreEqual(target, day.ActualHeight, 0.01);
        Assert.AreEqual(HorizontalAlignment.Center, day.HorizontalAlignment);
        Assert.IsGreaterThan((width - target) / 3, bounds.Left, "The cell must be centered, not stretched from the left edge.");
    }

    [STATestMethod]
    [DataRow("compact.pane-splitter.default", 760d, 600d)]
    [DataRow("regular.pane-splitter.default", 1000d, 720d)]
    [DataRow("wide.pane-splitter.default", 1200d, 720d)]
    public void PaneSplitter_UsesHairlineVerticalDividerInsidePrimaryHitTargetWidth(
        string pageId,
        double width,
        double height)
    {
        var surface = Compose(pageId, ShowcaseStress.Default, width, height);
        var splitter = Descendants(surface).OfType<WpfThumb>().Single();
        var target = ShowcaseResources.Get<double>("Daynote.Size.Splitter.HitTarget");
        var thin = ShowcaseResources.Get<double>("Daynote.Border.Thin");
        var divider = (Border)splitter.Template.FindName("Divider", splitter);

        // Revision 2026-07-21: the divider is a full-height 1-DIP hairline (no floating thumb),
        // centered inside the unchanged 44-DIP transparent hit-target width.
        Assert.AreEqual(target, splitter.ActualWidth, 0.01);
        Assert.AreEqual(thin, divider.ActualWidth, 0.01);
        Assert.AreEqual(splitter.ActualHeight, divider.ActualHeight, 0.01);
        Assert.AreEqual(System.Windows.Input.Cursors.SizeWE, splitter.Cursor);
        Assert.AreEqual("Left and Right adjust; Home and End select bounds; Escape restores width.",
            AutomationProperties.GetHelpText(splitter));
    }

    [STATestMethod]
    [DataRow("compact.app-shell.default", 760d, 600d)]
    [DataRow("regular.app-shell.default", 1000d, 720d)]
    [DataRow("wide.app-shell.default", 1586d, 992d)]
    public void CjkFixture_UsesCompletePhraseLinesAndDecodesAtScaleTwo(
        string pageId,
        double width,
        double height)
    {
        var surface = Compose(pageId, ShowcaseStress.Cjk, width, height);
        var editor = Descendants(surface).OfType<TextBox>()
            .Single(text => AutomationProperties.GetName(text).StartsWith("Markdown editor", StringComparison.Ordinal));
        var previews = Descendants(surface).OfType<TextBlock>()
            .Where(text => AutomationProperties.GetName(text) == "Clipboard preview")
            .ToArray();

        CollectionAssert.DoesNotContain(EditorLines(editor), "다.");
        foreach (var preview in previews)
        {
            Assert.IsFalse(preview.Text.Split('\n').Any(line => line.Trim() is "다." or "니다."));
            foreach (var line in preview.Text.Split('\n'))
                Assert.IsLessThanOrEqualTo(preview.ActualWidth + 0.5, Measure(line, preview));
        }

        var decoded = RenderPng(surface, width, height, scale: 2);
        Assert.AreEqual((int)width * 2, decoded.PixelWidth);
        Assert.AreEqual((int)height * 2, decoded.PixelHeight);
        Assert.AreEqual(255, AlphaAt(decoded, decoded.PixelWidth - 1, decoded.PixelHeight - 1));
    }

    private static FrameworkElement Compose(string pageId, ShowcaseStress stress, double width, double height)
    {
        var application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        var selection = new ShowcaseSelection(
            ShowcaseManifest.FindPage(pageId), ShowcasePalette.Standard,
            ShowcaseMotion.Reduced, stress, ShowcaseFrame.Settled);
        var surface = new ShowcaseComposer().Compose(selection);
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        ApplyTemplates(surface);
        surface.UpdateLayout();
        return surface;
    }

    private static BitmapSource RenderPng(FrameworkElement source, double width, double height, int scale)
    {
        source.LayoutTransform = new ScaleTransform(scale, scale);
        var host = new Grid
        {
            Width = width * scale,
            Height = height * scale,
            Background = ShowcaseResources.Get<Brush>("Daynote.Brush.Canvas"),
            ClipToBounds = true
        };
        host.Children.Add(source);
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        ApplyTemplates(host);
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static string[] EditorLines(TextBox editor) =>
        Enumerable.Range(0, editor.LineCount).Select(index => editor.GetLineText(index).Trim()).ToArray();

    private static double Measure(string text, TextBlock sample) => new FormattedText(
        text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
        new Typeface(sample.FontFamily, sample.FontStyle, sample.FontWeight, sample.FontStretch),
        sample.FontSize, sample.Foreground, VisualTreeHelper.GetDpi(sample).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private static byte AlphaAt(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3];
    }

    private static Button NamedButton(DependencyObject root, string name) => Descendants(root).OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == name);

    private static Rect Bounds(FrameworkElement element, FrameworkElement root) =>
        new(element.TranslatePoint(new Point(), root), element.RenderSize);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }

    private static void ApplyTemplates(DependencyObject root)
    {
        if (root is Control control)
            control.ApplyTemplate();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            ApplyTemplates(VisualTreeHelper.GetChild(root, index));
    }
}
