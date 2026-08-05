using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Interop;

namespace Daynote.App.Showcase;

internal static partial class ShowcaseInteractionSequenceRunner
{
    private const string Schema = "daynote.showcase-interaction-sequence/v1";

    public static string Run(ShowcaseOptions options, ShowcaseManifestDocument manifest)
    {
        var definition = ShowcaseInteractionCatalog.Find(options.InteractionSequence!);
        var output = Path.GetFullPath(options.Output!);
        Directory.CreateDirectory(output);
        var page = ShowcaseManifest.FindPage(definition.PageId);
        var selection = new ShowcaseSelection(
            page, options.Palette, ShowcaseMotion.Reduced, ShowcaseStress.Default, ShowcaseFrame.Settled);
        var surface = new ShowcaseComposer().Compose(selection);
        var width = options.Width ?? ShowcaseResources.Get<double>("Daynote.Layout.WideMin");
        var height = options.Height ?? ShowcaseResources.Get<double>("Daynote.Size.Window.MinHeight");
        var window = new ShowcaseWindow(surface, page, width, height, false)
        {
            ShowActivated = true,
            ShowInTaskbar = false
        };

        try
        {
            window.Show();
            window.Activate();
            Pump(window.Dispatcher, TimeSpan.FromMilliseconds(1));
            Layout(surface, width, height);
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("The interaction sequence requires a materialized WPF HWND.");

            var initiatorAutomationName = InitiatorName(definition, options.InteractionModality!.Value);
            var normal = RunTransition(
                surface, window, definition, options.InteractionModality!.Value,
                ShowcaseMotion.Normal, output, width, height, options.Scale, hwnd);
            var reduced = RunTransition(
                surface, window, definition, options.InteractionModality.Value,
                ShowcaseMotion.Reduced, output, width, height, options.Scale, hwnd);
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var document = new ShowcaseInteractionSequenceDocument(
                Schema,
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                manifest.BuildIdentity,
                manifest.BuildModifiedUtc,
                manifest.SourceModifiedUtc,
                executablePath,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executablePath))),
                Environment.ProcessId,
                Environment.CurrentManagedThreadId,
                Hwnd(hwnd),
                definition.FamilyId,
                definition.PageId,
                options.InteractionModality.Value,
                definition.SemanticAction,
                initiatorAutomationName,
                definition.KeyboardInitiatorAutomationName,
                definition.InitiatorControlType,
                definition.MotionTargetAutomationName,
                definition.ScrollOwner,
                definition.ScrollOwnerAutomationName,
                AutomationProperties.GetName(surface),
                AncestorNames(FindExact(surface, initiatorAutomationName, definition.InitiatorControlType)),
                [normal, reduced]);
            var documentPath = Path.Combine(output, "interaction-sequence.json");
            File.WriteAllText(documentPath, JsonSerializer.Serialize(document, ShowcaseManifest.JsonOptions));
            return documentPath;
        }
        finally
        {
            window.Close();
        }
    }
}
