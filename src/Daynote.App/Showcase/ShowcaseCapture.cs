using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfSize = System.Windows.Size;

namespace Daynote.App.Showcase;

public sealed record ShowcaseElementState(string AutomationName, string ControlType, bool Enabled, string ForcedState);

public sealed record ShowcaseCaptureMetadata(
    string PageId,
    string Png,
    string BuildIdentity,
    DateTimeOffset BuildModifiedUtc,
    DateTimeOffset SourceModifiedUtc,
    double WidthDip,
    double HeightDip,
    int Scale,
    int PixelWidth,
    int PixelHeight,
    ShowcasePalette Palette,
    ShowcaseMotion Motion,
    ShowcaseFrame Frame,
    ShowcaseStress Stress,
    string State,
    string FocusOwner,
    string ActualFocusedAutomationName,
    string ScrollOwner,
    string ActualScrollOwnerAutomationName,
    string InputPath,
    IReadOnlyList<ShowcaseElementState> UiaState);

public sealed record ShowcaseRunManifest(
    ShowcaseManifestDocument Manifest,
    DateTimeOffset CapturedUtc,
    IReadOnlyList<ShowcaseCaptureMetadata> Captures);

internal static class ShowcaseCapture
{
    public static ShowcaseCaptureMetadata Render(
        ShowcaseWindow window,
        FrameworkElement surface,
        ShowcaseSelection selection,
        ShowcaseOptions options,
        ShowcaseManifestDocument manifest,
        string outputDirectory,
        double width,
        double height)
    {
        Directory.CreateDirectory(outputDirectory);
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        surface.Measure(new WpfSize(width, height));
        surface.Arrange(new Rect(new WpfSize(width, height)));
        surface.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var pixelWidth = checked((int)Math.Round(width * options.Scale, MidpointRounding.AwayFromZero));
        var pixelHeight = checked((int)Math.Round(height * options.Scale, MidpointRounding.AwayFromZero));
        var dpi = 96d * options.Scale;
        var renderSurface = surface;
        var renderDpi = dpi;
        if (options.Scale > 1)
        {
            var scaledSurface = new ShowcaseComposer().Compose(selection);
            scaledSurface.Width = width;
            scaledSurface.Height = height;
            scaledSurface.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            scaledSurface.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            scaledSurface.LayoutTransform = new ScaleTransform(options.Scale, options.Scale);

            var captureHost = new System.Windows.Controls.Grid
            {
                Width = pixelWidth,
                Height = pixelHeight,
                Background = ShowcaseResources.Get<System.Windows.Media.Brush>("Daynote.Brush.Canvas"),
                ClipToBounds = true
            };
            captureHost.Children.Add(scaledSurface);
            captureHost.Measure(new WpfSize(pixelWidth, pixelHeight));
            captureHost.Arrange(new Rect(new WpfSize(pixelWidth, pixelHeight)));
            captureHost.UpdateLayout();
            renderSurface = captureHost;
            renderDpi = 96d;
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, renderDpi, renderDpi, PixelFormats.Pbgra32);
        bitmap.Render(renderSurface);
        bitmap.Freeze();

        var stem = FileStem(selection, options.Scale);
        var pngPath = Path.Combine(outputDirectory, $"{stem}.png");
        using (var stream = File.Create(pngPath))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        var actualFocus = Keyboard.FocusedElement as DependencyObject;
        var actualFocusName = actualFocus is null ? "none" : AutomationProperties.GetName(actualFocus);
        var metadata = new ShowcaseCaptureMetadata(
            selection.Page.Id,
            Path.GetFileName(pngPath),
            manifest.BuildIdentity,
            manifest.BuildModifiedUtc,
            manifest.SourceModifiedUtc,
            width,
            height,
            options.Scale,
            pixelWidth,
            pixelHeight,
            selection.Palette,
            selection.Motion,
            selection.Frame,
            selection.Stress,
            selection.Page.State,
            selection.Page.FocusOwner,
            string.IsNullOrWhiteSpace(actualFocusName) ? actualFocus?.GetType().Name ?? "none" : actualFocusName,
            selection.Page.ScrollOwner,
            ActualScrollOwner(window),
            InputPath(selection),
            ReadUiaState(window));
        File.WriteAllText(
            Path.Combine(outputDirectory, $"{stem}.json"),
            JsonSerializer.Serialize(metadata, ShowcaseManifest.JsonOptions));
        window.Close();
        return metadata;
    }

    public static void WriteRunManifest(
        string outputDirectory,
        ShowcaseManifestDocument manifest,
        IReadOnlyList<ShowcaseCaptureMetadata> captures)
    {
        Directory.CreateDirectory(outputDirectory);
        var document = new ShowcaseRunManifest(manifest, DateTimeOffset.UtcNow, captures);
        File.WriteAllText(
            Path.Combine(outputDirectory, "showcase-manifest.json"),
            JsonSerializer.Serialize(document, ShowcaseManifest.JsonOptions));
    }

    private static IReadOnlyList<ShowcaseElementState> ReadUiaState(DependencyObject root) =>
        ShowcaseWindow.Descendants(root)
            .OfType<FrameworkElement>()
            .Select(element => new
            {
                Element = element,
                Name = AutomationProperties.GetName(element)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ShowcaseElementState(
                item.Name,
                item.Element.GetType().Name,
                item.Element.IsEnabled,
                ShowcaseForcedState.GetState(item.Element)))
            .ToArray();

    private static string ActualScrollOwner(DependencyObject root)
    {
        var scrollViewer = ShowcaseWindow.Descendants(root)
            .OfType<System.Windows.Controls.ScrollViewer>()
            .FirstOrDefault();
        if (scrollViewer is null)
            return "none";

        for (DependencyObject? current = scrollViewer; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var name = AutomationProperties.GetName(current);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return scrollViewer.GetType().Name;
    }

    private static string InputPath(ShowcaseSelection selection) => selection.Page.State == "focus"
        ? "attached-state-override; programmatic focus requested; no pointer or keyboard input executed"
        : "attached-state-override; no pointer or keyboard input executed";

    private static string FileStem(ShowcaseSelection selection, int scale) =>
        $"{selection.Page.Id}.{selection.Palette.ToString().ToLowerInvariant()}." +
        $"{selection.Motion.ToString().ToLowerInvariant()}.{selection.Frame.ToString().ToLowerInvariant()}." +
        $"{selection.Stress.ToString().ToLowerInvariant()}.scale-{scale}";

}
