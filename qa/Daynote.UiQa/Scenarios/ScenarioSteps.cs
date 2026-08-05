using System.Windows.Automation;
using Daynote.UiQa.Automation;
using Daynote.UiQa.Data;
using Daynote.UiQa.Product;

namespace Daynote.UiQa.Scenarios;

/// <summary>
/// Reusable building blocks shared by the deterministic scenarios. Each helper drives the real
/// product surface through UI Automation and observes real artifacts (the automation tree, the
/// SQLite database, the filesystem) so scenarios never rely on manual-only assertions.
/// </summary>
internal static class ScenarioSteps
{
    // Autosave debounce is 500 ms; allow generous slack for the flush to reach SQLite.
    internal static readonly TimeSpan AutosaveSettle = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Standard live-product flow: allocate a disposable data root, launch the real app, run the
    /// body, always capture the database snapshot, and always dispose the process. The disposable
    /// data root is removed unless <c>--keep-data</c> was passed (preservation checks).
    /// </summary>
    internal static ScenarioResult WithProduct(
        UiQaScenarioContext context,
        Action<ProductAppDriver, UiaSession, AutomationElement, ScenarioResult> body)
    {
        var result = new ScenarioResult(context.Definition.Name);
        string runId = $"{context.Definition.Name}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        ProductAppDriver driver = ProductAppDriver.Create(runId);
        context.Log.Record("data-root", $"Disposable QA data root allocated under the .uiqa namespace.");
        try
        {
            UiaSession session = driver.Launch();
            context.Log.Record("launch", "Launched the real Daynote product process.");
            AutomationElement window = session.WaitForMainWindow();
            body(driver, session, window, result);

            DatabaseSnapshot snapshot = DatabaseInspector.Inspect(driver.DatabasePath);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(EnsureEvidence(context), "database.json"),
                snapshot.ToJson());
            result.Expect(
                "no-foreign-key-violations",
                snapshot.ForeignKeyViolations == 0,
                $"foreign_key_check rows: {snapshot.ForeignKeyViolations}");
        }
        finally
        {
            driver.Dispose();
            if (!context.KeepData)
            {
                DaynoteQaPaths.RemoveRunRoot(driver.DataRoot);
                context.Log.Record("cleanup", "Removed the disposable QA data root (inside .uiqa only).");
            }
            else
            {
                context.Log.Record("cleanup", "Kept the disposable QA data root for a preservation check.");
            }
        }

        return result;
    }

    internal static string EnsureEvidence(UiQaScenarioContext context)
    {
        System.IO.Directory.CreateDirectory(context.EvidenceDirectory);
        return context.EvidenceDirectory;
    }

    internal static void Screenshot(
        UiQaScenarioContext context,
        AutomationElement window,
        string name)
    {
        string path = ScreenCapture.CaptureWindow(window, EnsureEvidence(context), name);
        context.Log.Record("screenshot", $"Captured {System.IO.Path.GetFileName(path)}.");
    }

    /// <summary>Types text into the Markdown editor via the Value pattern, then waits for autosave.</summary>
    internal static void TypeIntoEditor(
        UiQaScenarioContext context,
        UiaSession session,
        AutomationElement window,
        string text)
    {
        AutomationElement editor = session.WaitForElement(window, "Markdown editor");
        UiaSession.SetValue(editor, text);
        context.Log.Record("edit", $"Wrote {text.Length} characters into the Markdown editor.");
        Thread.Sleep(AutosaveSettle);
    }

    internal static void Invoke(
        UiQaScenarioContext context,
        UiaSession session,
        AutomationElement window,
        string automationName)
    {
        AutomationElement element = session.WaitForElement(window, automationName);
        UiaSession.Invoke(element);
        context.Log.Record("invoke", $"Invoked '{automationName}'.");
    }

    /// <summary>Marks the remaining live work as deferred with a real reason, and records the exact
    /// deferred command the operator runs in a VM. Keeps at least one recorded observable so the
    /// scenario never silently passes on nothing.</summary>
    internal static ScenarioResult DeferredLive(
        UiQaScenarioContext context,
        string deferredCommand,
        Action<ScenarioResult> authoredChecks)
    {
        var result = new ScenarioResult(context.Definition.Name);
        authoredChecks(result);
        context.Log.Record("deferred", $"Live execution deferred to a VM. Command: {deferredCommand}");
        return result;
    }
}
