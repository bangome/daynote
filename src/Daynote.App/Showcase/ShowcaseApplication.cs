using System.IO;
using System.Text.Json;
using System.Windows;

namespace Daynote.App.Showcase;

internal static class ShowcaseApplication
{
    public static int Run(ShowcaseOptions options)
    {
        var sourceRoot = FindSourceRoot();
        var manifest = ShowcaseManifest.CreateDocument(sourceRoot);
        if (options.List)
        {
            Console.WriteLine(JsonSerializer.Serialize(manifest, ShowcaseManifest.JsonOptions));
            return 0;
        }

        if (manifest.SourceModifiedUtc > manifest.BuildModifiedUtc)
            throw new InvalidOperationException(
                $"Showcase source is newer than build {manifest.BuildIdentity}; rebuild before capture or --hold.");

        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ShowcaseResources.Load(application, options.Palette == ShowcasePalette.HighContrast);
        if (options.InteractionSequence is not null)
        {
            var documentPath = ShowcaseInteractionSequenceRunner.Run(options, manifest);
            application.Shutdown();
            Console.WriteLine($"Captured correlated interaction sequence to {documentPath}");
            return 0;
        }
        var pages = options.CaptureAll
            ? ShowcaseManifest.Pages
            : new[] { ShowcaseManifest.FindPage(options.Page) };
        var captures = new List<ShowcaseCaptureMetadata>();

        if (options.Output is not null)
        {
            foreach (var page in pages)
            {
                var selection = Selection(page, options);
                var content = new ShowcaseComposer().Compose(selection);
                var (width, height) = Dimensions(page, options);
                var window = new ShowcaseWindow(content, page, width, height, false);
                captures.Add(ShowcaseCapture.Render(
                    window, content, selection, options, manifest, options.Output, width, height));
            }
            ShowcaseCapture.WriteRunManifest(options.Output, manifest, captures);
            Console.WriteLine($"Captured {captures.Count} page(s) to {Path.GetFullPath(options.Output)}");
        }

        if (options.Hold)
        {
            if (options.CaptureAll)
                throw new ArgumentException("--hold requires one --page; it cannot hold every --capture-all page.");
            var page = pages[0];
            var selection = Selection(page, options);
            var content = new ShowcaseComposer().Compose(selection);
            var (width, height) = Dimensions(page, options);
            var window = new ShowcaseWindow(content, page, width, height, true);
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            application.Run(window);
            return 0;
        }

        if (options.Output is null)
            throw new ArgumentException("--showcase requires --output, --hold, or --list.");
        application.Shutdown();
        return 0;
    }

    private static ShowcaseSelection Selection(ShowcasePage page, ShowcaseOptions options) =>
        new(page, options.Palette, options.Motion, options.Stress, options.Frame);

    private static (double Width, double Height) Dimensions(ShowcasePage page, ShowcaseOptions options)
    {
        var width = options.Width ?? page.Layout switch
        {
            ShowcaseLayout.Compact => ShowcaseResources.Get<double>("Daynote.Size.Window.MinWidth"),
            ShowcaseLayout.Regular => ShowcaseResources.Get<double>("Daynote.Layout.RegularMin"),
            _ => ShowcaseResources.Get<double>("Daynote.Layout.WideMin")
        };
        var height = options.Height ?? ShowcaseResources.Get<double>("Daynote.Size.Window.MinHeight");
        return (width, height);
    }

    private static string FindSourceRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Daynote.App");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
