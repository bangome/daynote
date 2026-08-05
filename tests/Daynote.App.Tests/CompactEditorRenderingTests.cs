using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class CompactEditorRenderingTests
{
    private static readonly string[] PrimaryActions =
        ["Open settings", "Add note", "Bold", "Italic", "Bulleted list", "Numbered list", "Inline code"];
    private static readonly string[] ToolbarActions =
        ["Bold", "Italic", "Bulleted list", "Numbered list", "Inline code"];

    [STATestMethod]
    public void CompactCjkAtScaleTwo_RendersGlyphInkInsideEditorWithoutHidingActions()
    {
        var application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        var selection = new ShowcaseSelection(
            ShowcaseManifest.FindPage("compact.app-shell.default"),
            ShowcasePalette.Standard,
            ShowcaseMotion.Reduced,
            ShowcaseStress.Cjk,
            ShowcaseFrame.Settled);

        var width = ShowcaseResources.Get<double>("Daynote.Size.Window.MinWidth");
        var height = ShowcaseResources.Get<double>("Daynote.Size.Window.MinHeight");
        var scale = 2;
        var host = ComposeDetached(selection, width, height, scale);
        var decoded = RenderAndDecode(host, checked((int)(width * scale)), checked((int)(height * scale)));
        var editor = Descendants(host).OfType<TextBox>()
            .Single(text => AutomationProperties.GetName(text).StartsWith("Markdown editor", StringComparison.Ordinal));
        var contentHost = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
        var textView = Descendants(contentHost).OfType<FrameworkElement>()
            .Single(element => element.GetType().Name == "TextBoxView");

        var inkBounds = FindInkBounds(decoded, PhysicalBounds(textView, host));
        var minimumGlyphHeight = editor.FontSize * scale * 0.6;
        Assert.IsGreaterThanOrEqualTo(
            minimumGlyphHeight,
            inkBounds.Height,
            $"Committed CJK glyph ink was clipped to {inkBounds.Height:F2} physical pixels inside the editor body.");
        Assert.IsGreaterThanOrEqualTo(
            contentHost.ExtentHeight,
            contentHost.ViewportHeight,
            "The MarkdownEditor viewport must fit at least one natural committed-text line.");

        var toolbar = Descendants(host).OfType<ToolBar>()
            .Single(control => AutomationProperties.GetName(control) == "Editor toolbar");
        var editorBounds = PhysicalBounds(editor, host);
        var toolbarBounds = PhysicalBounds(toolbar, host);
        Assert.IsLessThanOrEqualTo(
            toolbarBounds.Top + 0.5,
            editorBounds.Bottom,
            "The editor body must end before the fixed toolbar begins.");
        Assert.IsTrue(editorBounds.Contains(PhysicalBounds(textView, host)), "Glyph ink must stay inside the editor body.");
        var shellStatus = Descendants(host).OfType<TextBlock>()
            .Single(text => AutomationProperties.GetName(text) == "AppShell status region");
        Assert.IsLessThanOrEqualTo(
            PhysicalBounds(shellStatus, host).Top + 0.5,
            toolbarBounds.Bottom,
            "The fixed toolbar must end before the shell status begins.");

        var primaryTarget = ShowcaseResources.Get<double>("Daynote.Size.Target.Primary");
        foreach (var name in PrimaryActions)
        {
            var action = Descendants(host).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == name);
            var bounds = PhysicalBounds(action, host);
            Assert.IsTrue(action.IsEnabled && action.Visibility == Visibility.Visible, $"{name} must remain enabled and visible.");
            Assert.IsGreaterThanOrEqualTo(primaryTarget, action.ActualWidth, $"{name} width");
            Assert.IsGreaterThanOrEqualTo(primaryTarget, action.ActualHeight, $"{name} height");
            Assert.IsTrue(PhysicalBounds(host, host).Contains(bounds), $"{name} must remain inside the capture.");
            if (ToolbarActions.Contains(name, StringComparer.Ordinal))
                Assert.IsTrue(toolbarBounds.Contains(bounds), $"{name} must remain inside the fixed toolbar.");
        }
    }

    private static Grid ComposeDetached(ShowcaseSelection selection, double width, double height, int scale)
    {
        var surface = new ShowcaseComposer().Compose(selection);
        surface.Width = width;
        surface.Height = height;
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        surface.VerticalAlignment = VerticalAlignment.Top;
        surface.LayoutTransform = new ScaleTransform(scale, scale);
        var host = new Grid
        {
            Width = width * scale,
            Height = height * scale,
            Background = ShowcaseResources.Get<Brush>("Daynote.Brush.Canvas"),
            ClipToBounds = true
        };
        host.Children.Add(surface);
        var physicalSize = new Size(host.Width, host.Height);
        host.Measure(physicalSize);
        host.Arrange(new Rect(physicalSize));
        ApplyTemplates(host);
        host.UpdateLayout();
        return host;
    }

    private static BitmapSource RenderAndDecode(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new FormatConvertedBitmap(
            new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0],
            PixelFormats.Bgra32,
            null,
            0);
    }

    private static Rect FindInkBounds(BitmapSource bitmap, Rect region)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var foreground = ShowcaseResources.Get<SolidColorBrush>("Daynote.Brush.Text.Primary").Color;
        var canvas = ShowcaseResources.Get<SolidColorBrush>("Daynote.Brush.Surface.Primary").Color;
        var left = Math.Max(0, (int)Math.Floor(region.Left));
        var top = Math.Max(0, (int)Math.Floor(region.Top));
        var right = Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(region.Right));
        var bottom = Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(region.Bottom));
        var minX = right;
        var minY = bottom;
        var maxX = left - 1;
        var maxY = top - 1;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = (y * stride) + (x * 4);
                var red = pixels[offset + 2];
                var green = pixels[offset + 1];
                var blue = pixels[offset];
                var foregroundDistance = Math.Abs(red - foreground.R) + Math.Abs(green - foreground.G) + Math.Abs(blue - foreground.B);
                var canvasDistance = Math.Abs(red - canvas.R) + Math.Abs(green - canvas.G) + Math.Abs(blue - canvas.B);
                if (foregroundDistance >= canvasDistance)
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < minX ? Rect.Empty : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect PhysicalBounds(FrameworkElement element, FrameworkElement host)
    {
        var topLeft = element.TranslatePoint(new Point(), host);
        var bottomRight = element.TranslatePoint(new Point(element.ActualWidth, element.ActualHeight), host);
        return new Rect(topLeft, bottomRight);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }

    private static void ApplyTemplates(DependencyObject root)
    {
        if (root is Control control)
            control.ApplyTemplate();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            ApplyTemplates(VisualTreeHelper.GetChild(root, index));
    }
}
