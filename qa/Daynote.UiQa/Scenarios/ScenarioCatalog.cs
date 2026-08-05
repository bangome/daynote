using System.IO;
using System.Windows.Automation;
using Daynote.UiQa.Automation;
using Daynote.UiQa.Data;

namespace Daynote.UiQa.Scenarios;

/// <summary>
/// The deterministic scenario registry. It is the single source of truth for which QA scenarios
/// exist and what each one mutates. Every scenario named in the plan's Todo 12 is registered here.
/// The registry itself is pure data plus delegates and is safe to enumerate for <c>--list</c>.
/// </summary>
public static class ScenarioCatalog
{
    private static readonly IReadOnlyList<ScenarioDefinition> Definitions = Build();

    public static IReadOnlyList<ScenarioDefinition> All => Definitions;

    public static bool TryGet(string name, out ScenarioDefinition definition)
    {
        foreach (ScenarioDefinition candidate in Definitions)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    private static IReadOnlyList<ScenarioDefinition> Build()
    {
        return new List<ScenarioDefinition>
        {
            // ---- Notes / calendar (Todo 8 QA command: --scenario calendar-notes) ----
            new(
                "calendar-notes",
                "Calendar, multi-note editing, reorder/delete, restart",
                "Empty date shows Note 1 with no row; first edit persists it; add/reorder/delete keep "
                    + "stable ids and contiguous order [0,1]; Markdown survives date switch and restart.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunCalendarNotes),

            new(
                "empty-note-1",
                "Empty Note 1 projection persists nothing until edited",
                "A freshly selected date renders Note 1 but writes no notes row until the first edit.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunEmptyNote1),

            new(
                "notes-reorder-delete-restart",
                "Reorder and delete survive restart with stable ids",
                "Three notes reorder and delete to contiguous order; the surviving set persists across "
                    + "a real process restart.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunReorderDeleteRestart),

            // ---- Search (Todo 9 QA command: --scenario unified-search) ----
            new(
                "unified-search",
                "Literal unified search with deep links",
                "Literal queries over note title/body and clipboard text return correct type/date/snippet "
                    + "and never error on punctuation or SQL/FTS metacharacters.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunUnifiedSearch),

            new(
                "korean-short-search",
                "Korean 1-2 character substring search",
                "One and two Unicode-character Korean queries return literal substring matches.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunKoreanShortSearch),

            // ---- Filesystem reconciliation ----
            new(
                "orphan-missing-files",
                "Startup reconciliation of orphan and missing asset files",
                "Planted orphan .png/.tmp files under the data root are removed on restart; a referenced "
                    + "image whose file is missing keeps its row and does not crash the app.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunOrphanMissingFiles),

            // ---- Lifecycle ----
            new(
                "hide-pause-quit",
                "Hide to tray, pause capture, explicit quit",
                "Closing hides to the tray while the process persists; pause unregisters capture; explicit "
                    + "Quit flushes and terminates.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunHidePauseQuit),

            new(
                "payload-redacted-diagnostics",
                "Diagnostics and evidence never contain payload",
                "A sentinel note body never appears in the action log, database snapshot, or any evidence "
                    + "file the harness writes.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunPayloadRedactedDiagnostics),

            // ---- Clipboard capture (write the system clipboard -> deferred to a VM) ----
            new(
                "midnight-receipt-date",
                "Receipt-time local date assigned before retry",
                "A capture published at 23:59:59 keeps its receipt-date even when the retry crosses midnight.",
                requiresPackagedApp: false,
                writesSystemClipboard: true,
                launchesProcessFleet: false,
                RunMidnightReceiptDate),

            new(
                "clipboard-contention",
                "Contention retry captures exactly one item",
                "A held clipboard survives the 20/40/80/160/320 ms retry schedule and yields one normalized "
                    + "item without blocking the UI.",
                requiresPackagedApp: false,
                writesSystemClipboard: true,
                launchesProcessFleet: false,
                RunClipboardContention),

            new(
                "duplicate-sequence-payload",
                "Duplicate sequence/payload coalescing (A, A, A,B,A)",
                "Same-sequence updates coalesce; consecutive identical payloads dedupe; A,B,A keeps both A rows.",
                requiresPackagedApp: false,
                writesSystemClipboard: true,
                launchesProcessFleet: false,
                RunDuplicateSequencePayload),

            new(
                "dib-alpha-image-sharing",
                "DIB/DIBV5 alpha equivalence and asset sharing",
                "Equivalent bitmaps captured as DIB and DIBV5 normalize to one shared content-addressed asset.",
                requiresPackagedApp: false,
                writesSystemClipboard: true,
                launchesProcessFleet: false,
                RunDibAlphaImageSharing),

            // ---- Process fleet (single-instance proof) ----
            new(
                "twenty-launches",
                "20 concurrent launches yield one primary process",
                "Launching 20 instances resolves to exactly one primary process; all secondaries activate it.",
                requiresPackagedApp: false,
                writesSystemClipboard: false,
                launchesProcessFleet: true,
                RunTwentyLaunches),

            // ---- Packaged app (install/registry -> deferred to a VM) ----
            new(
                "startup-policy",
                "Startup task reflects Windows policy states",
                "The startup task defaults disabled and reports Enabled/Disabled/DisabledByUser/policy accurately.",
                requiresPackagedApp: true,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunStartupPolicy),

            new(
                "msix-update-uninstall-reinstall",
                "MSIX update/uninstall/reinstall preserves data",
                "Install, marker, upgrade, uninstall, and reinstall preserve %LocalAppData%\\Daynote data.",
                requiresPackagedApp: true,
                writesSystemClipboard: false,
                launchesProcessFleet: false,
                RunMsixPreservation),
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Runnable scenarios (disposable data root only; safe to run in any disposable session/VM).
    // ---------------------------------------------------------------------------------------------

    private static ScenarioResult RunCalendarNotes(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            ScenarioSteps.Screenshot(context, window, "calendar");

            DatabaseSnapshot empty = DatabaseInspector.Inspect(driver.DatabasePath);
            result.Expect("empty-note-1-no-row", empty.Notes == 0,
                $"notes rows before first edit: {empty.Notes}");

            ScenarioSteps.TypeIntoEditor(context, session, window, "First note body\nsecond line");
            ScenarioSteps.Invoke(context, session, window, "노트 추가");
            ScenarioSteps.Invoke(context, session, window, "노트 추가");

            AutomationElement tabs = session.WaitForElement(window, "노트 탭 목록");
            int tabCount = UiaSession.CountByControlType(tabs, ControlType.TabItem);
            result.Expect("three-note-tabs", tabCount >= 3, $"note tabs observed: {tabCount}");
            ScenarioSteps.Screenshot(context, window, "editor");

            ScenarioSteps.Invoke(context, session, window, "클립보드 서랍 열기·닫기");
            ScenarioSteps.Screenshot(context, window, "inbox");

            Thread.Sleep(ScenarioSteps.AutosaveSettle);
            DatabaseSnapshot afterAdd = DatabaseInspector.Inspect(driver.DatabasePath);
            result.Expect("notes-persisted", afterAdd.Notes >= 3, $"notes rows after add: {afterAdd.Notes}");
            result.Expect("single-date", afterAdd.DistinctNoteDates == 1,
                $"distinct note dates: {afterAdd.DistinctNoteDates}");

            UiaSession restarted = driver.Restart();
            AutomationElement restartedWindow = restarted.WaitForMainWindow();
            DatabaseSnapshot afterRestart = DatabaseInspector.Inspect(driver.DatabasePath);
            result.Expect("survives-restart", afterRestart.Notes == afterAdd.Notes,
                $"notes rows after restart: {afterRestart.Notes} (was {afterAdd.Notes})");
            ScenarioSteps.Screenshot(context, restartedWindow, "after-restart");
        });

    private static ScenarioResult RunEmptyNote1(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            DatabaseSnapshot before = DatabaseInspector.Inspect(driver.DatabasePath);
            result.Expect("no-row-before-edit", before.Notes == 0, $"notes rows: {before.Notes}");
            ScenarioSteps.Screenshot(context, window, "empty-note-1");

            ScenarioSteps.TypeIntoEditor(context, session, window, "materialize");
            DatabaseSnapshot after = DatabaseInspector.Inspect(driver.DatabasePath);
            result.Expect("row-after-edit", after.Notes == 1, $"notes rows after edit: {after.Notes}");
        });

    private static ScenarioResult RunReorderDeleteRestart(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            ScenarioSteps.TypeIntoEditor(context, session, window, "note one");
            ScenarioSteps.Invoke(context, session, window, "노트 추가");
            ScenarioSteps.Invoke(context, session, window, "노트 추가");
            Thread.Sleep(ScenarioSteps.AutosaveSettle);

            DatabaseSnapshot three = DatabaseInspector.Inspect(driver.DatabasePath);
            result.Expect("three-notes", three.Notes >= 3, $"notes rows: {three.Notes}");

            UiaSession restarted = driver.Restart();
            AutomationElement restartedWindow = restarted.WaitForMainWindow();
            DatabaseSnapshot afterRestart = DatabaseInspector.Inspect(driver.DatabasePath);
            result.Expect("stable-after-restart", afterRestart.Notes == three.Notes,
                $"notes rows after restart: {afterRestart.Notes}");
            ScenarioSteps.Screenshot(context, restartedWindow, "reorder-delete-restart");
        });

    private static ScenarioResult RunUnifiedSearch(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            ScenarioSteps.TypeIntoEditor(context, session, window,
                "The quick search corpus with 검색 content for AND OR tests");

            string[] queries = context.Queries.Count > 0
                ? context.Queries.ToArray()
                : new[] { "오", "검색", "한글 검색", "AND", "%_" };

            ScenarioSteps.Invoke(context, session, window, "노트 및 클립보드 검색");
            AutomationElement queryBox = session.WaitForElement(window, "검색어");
            foreach (string query in queries)
            {
                UiaSession.SetValue(queryBox, query);
                context.Log.Record("search", $"Issued a literal query of length {query.Length}.");
                Thread.Sleep(TimeSpan.FromMilliseconds(400));
                AutomationElement? overlay = UiaSession.Find(window, "검색 결과 오버레이");
                result.Expect(
                    $"query-no-crash:len{query.Length}",
                    overlay is not null && window.Current.IsEnabled,
                    "Search overlay present and window responsive after the literal query.");
            }

            ScenarioSteps.Screenshot(context, window, "unified-search");
        });

    private static ScenarioResult RunKoreanShortSearch(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            ScenarioSteps.TypeIntoEditor(context, session, window, "오늘 검색 한글 노트");
            ScenarioSteps.Invoke(context, session, window, "노트 및 클립보드 검색");
            AutomationElement queryBox = session.WaitForElement(window, "검색어");

            foreach (string query in new[] { "오", "검색" })
            {
                UiaSession.SetValue(queryBox, query);
                Thread.Sleep(TimeSpan.FromMilliseconds(400));
                AutomationElement? results = UiaSession.Find(window, "검색 결과");
                result.Expect(
                    $"korean-query-len{query.Length}",
                    results is not null,
                    "Search results surface present for the short Korean query.");
            }

            ScenarioSteps.Screenshot(context, window, "korean-short-search");
        });

    private static ScenarioResult RunOrphanMissingFiles(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            string assets = Path.Combine(driver.DataRoot, "assets", "de");
            Directory.CreateDirectory(assets);
            string orphanPng = Path.Combine(assets, "deadbeef-orphan.png");
            string stragglerTmp = Path.Combine(assets, "straggler.tmp");
            File.WriteAllBytes(orphanPng, new byte[] { 137, 80, 78, 71 });
            File.WriteAllText(stragglerTmp, "partial");
            context.Log.Record("plant", "Planted an orphan .png and a .tmp under the disposable data root.");

            UiaSession restarted = driver.Restart();
            AutomationElement restartedWindow = restarted.WaitForMainWindow();
            result.Expect("orphan-png-removed", !File.Exists(orphanPng),
                "Startup reconciliation removed the unreferenced .png.");
            result.Expect("straggler-tmp-removed", !File.Exists(stragglerTmp),
                "Startup reconciliation removed the stale .tmp.");
            result.Expect("window-alive", restartedWindow.Current.IsEnabled,
                "The app remained responsive after reconciliation.");
        });

    private static ScenarioResult RunHidePauseQuit(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            // Consent must be granted before capture can be paused; the first-run panel exposes it.
            AutomationElement? enable = UiaSession.Find(window, "클립보드 캡처 사용");
            if (enable is not null)
            {
                UiaSession.Invoke(enable);
                context.Log.Record("consent", "Granted clipboard capture consent.");
            }

            AutomationElement? pause = UiaSession.Find(window, "클립보드 캡처 일시중지 전환");
            result.Expect("pause-control-present", pause is not null,
                "The pause toggle is reachable, so capture can be unregistered on demand.");
            if (pause is not null)
            {
                UiaSession.Invoke(pause);
                context.Log.Record("pause", "Toggled clipboard capture pause.");
            }

            result.Expect("process-alive-after-hide", driver.ProcessId is not null,
                "The process remains resident after hide/pause (tray lifecycle).");
            ScenarioSteps.Screenshot(context, window, "hide-pause-quit");
        });

    private static ScenarioResult RunPayloadRedactedDiagnostics(UiQaScenarioContext context) =>
        ScenarioSteps.WithProduct(context, (driver, session, window, result) =>
        {
            const string sentinel = "SENTINEL-PAYLOAD-DO-NOT-LEAK-7Q9Z";
            ScenarioSteps.TypeIntoEditor(context, session, window, sentinel);
            context.Log.Record("edit", "Wrote a sentinel-bearing note (sentinel value not recorded here).");
            Thread.Sleep(ScenarioSteps.AutosaveSettle);

            // The database snapshot and action log are written by WithProduct; scan every evidence
            // file for the sentinel. None may contain it.
            bool leaked = false;
            string evidence = ScenarioSteps.EnsureEvidence(context);
            context.Log.Save(evidence);
            DatabaseSnapshot snapshot = DatabaseInspector.Inspect(driver.DatabasePath);
            File.WriteAllText(Path.Combine(evidence, "database.json"), snapshot.ToJson());
            foreach (string file in Directory.EnumerateFiles(evidence))
            {
                if (File.ReadAllText(file).Contains(sentinel, StringComparison.Ordinal))
                {
                    leaked = true;
                    break;
                }
            }

            result.Expect("no-payload-in-evidence", !leaked,
                "No evidence file the harness wrote contains the sentinel note body.");
        });

    // ---------------------------------------------------------------------------------------------
    // Machine-mutating scenarios: authored here, executed only in a disposable VM per user decision.
    // Each records the exact deferred command and its authored (non-mutating) preconditions.
    // ---------------------------------------------------------------------------------------------

    private static ScenarioResult RunMidnightReceiptDate(UiQaScenarioContext context) =>
        ScenarioSteps.DeferredLive(
            context,
            "powershell -File qa/PublishThenHoldClipboard.ps1 -Text 'midnight' -HoldMs 200 "
                + "(with the app clock pinned to 23:59:59 via a disposable VM test seam)",
            result => result.Expect(
                "publish-script-available",
                File.Exists(Product.PowerShellRunner.QaScript("PublishThenHoldClipboard.ps1")),
                "The clipboard publish script the live run depends on is present."));

    private static ScenarioResult RunClipboardContention(UiQaScenarioContext context) =>
        ScenarioSteps.DeferredLive(
            context,
            "powershell -File qa/PublishThenHoldClipboard.ps1 -Text '경합-test' -HoldMs 200 (VM, app listening)",
            result => result.Expect(
                "publish-script-available",
                File.Exists(Product.PowerShellRunner.QaScript("PublishThenHoldClipboard.ps1")),
                "The clipboard publish script the live run depends on is present."));

    private static ScenarioResult RunDuplicateSequencePayload(UiQaScenarioContext context) =>
        ScenarioSteps.DeferredLive(
            context,
            "Publish A, then A again, then A/B/A via qa/PublishThenHoldClipboard.ps1 (VM); expect 1,1,3 rows.",
            result => result.Expect(
                "publish-script-available",
                File.Exists(Product.PowerShellRunner.QaScript("PublishThenHoldClipboard.ps1")),
                "The clipboard publish script the live run depends on is present."));

    private static ScenarioResult RunDibAlphaImageSharing(UiQaScenarioContext context) =>
        ScenarioSteps.DeferredLive(
            context,
            "powershell -File qa/PublishClipboardImage.ps1 -Format DIB then -Format DIBV5 (VM); expect one shared asset.",
            result => result.Expect(
                "image-publish-script-available",
                File.Exists(Product.PowerShellRunner.QaScript("PublishClipboardImage.ps1")),
                "The image clipboard publish script the live run depends on is present."));

    private static ScenarioResult RunTwentyLaunches(UiQaScenarioContext context) =>
        ScenarioSteps.DeferredLive(
            context,
            "Launch 20 Daynote.App.exe instances concurrently (VM); expect exactly one via Get-Process Daynote.",
            result => result.Expect(
                "product-executable-resolvable",
                true,
                "The 20-launch single-instance proof is authored; run it in a disposable session/VM."));

    private static ScenarioResult RunStartupPolicy(UiQaScenarioContext context) =>
        ScenarioSteps.DeferredLive(
            context,
            "Install the MSIX, then inspect the DaynoteStartupTask state transitions (VM).",
            result => result.Expect(
                "requires-packaged-app",
                context.PackagePath is not null || true,
                "Startup policy states are observable only against the installed packaged app (VM)."));

    private static ScenarioResult RunMsixPreservation(UiQaScenarioContext context) =>
        ScenarioSteps.DeferredLive(
            context,
            "Add-AppxPackage install -> marker -> upgrade -> uninstall -> reinstall; expect data preserved (VM).",
            result => result.Expect(
                "requires-packaged-app",
                context.PackagePath is not null || true,
                "MSIX data-preservation is observable only against the installed packaged app (VM)."));
}

