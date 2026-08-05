using Daynote.UiQa.Scenarios;

namespace Daynote.UiQa.Tests;

[TestClass]
public sealed class ScenarioCatalogTests
{
    // Every scenario the plan's Todo 12 requires the harness to cover.
    private static readonly string[] RequiredScenarios =
    {
        "calendar-notes",
        "empty-note-1",
        "notes-reorder-delete-restart",
        "unified-search",
        "korean-short-search",
        "orphan-missing-files",
        "hide-pause-quit",
        "payload-redacted-diagnostics",
        "midnight-receipt-date",
        "clipboard-contention",
        "duplicate-sequence-payload",
        "dib-alpha-image-sharing",
        "twenty-launches",
        "startup-policy",
        "msix-update-uninstall-reinstall",
    };

    [TestMethod]
    public void Registry_covers_every_required_scenario()
    {
        var names = ScenarioCatalog.All.Select(static d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string required in RequiredScenarios)
        {
            Assert.IsTrue(names.Contains(required), $"Missing required scenario: {required}");
        }
    }

    [TestMethod]
    public void Registry_has_no_duplicate_names()
    {
        var names = ScenarioCatalog.All.Select(static d => d.Name).ToList();
        Assert.AreEqual(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Scenario names must be unique.");
    }

    [TestMethod]
    public void Every_scenario_has_title_description_and_delegate()
    {
        foreach (ScenarioDefinition definition in ScenarioCatalog.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(definition.Title), $"{definition.Name} has no title.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(definition.Description), $"{definition.Name} has no description.");
            Assert.IsNotNull(definition.Run, $"{definition.Name} has no run delegate.");
        }
    }

    [TestMethod]
    public void Machine_mutating_scenarios_are_flagged_deferred()
    {
        foreach (ScenarioDefinition definition in ScenarioCatalog.All)
        {
            bool mutatesHost = definition.WritesSystemClipboard
                || definition.LaunchesProcessFleet
                || definition.RequiresPackagedApp;
            Assert.AreEqual(mutatesHost, definition.DeferredOnAuthoringMachine,
                $"{definition.Name} deferred flag must match whether it mutates the host.");
        }
    }

    [TestMethod]
    public void Plan_named_qa_scenarios_are_present_and_run_the_live_app()
    {
        Assert.IsTrue(ScenarioCatalog.TryGet("calendar-notes", out ScenarioDefinition calendar));
        Assert.IsFalse(calendar.DeferredOnAuthoringMachine,
            "calendar-notes drives the live app against a disposable data root only; it is not host-mutating.");

        Assert.IsTrue(ScenarioCatalog.TryGet("unified-search", out ScenarioDefinition search));
        Assert.IsFalse(search.DeferredOnAuthoringMachine,
            "unified-search drives the live app against a disposable data root only; it is not host-mutating.");
    }

    [TestMethod]
    public void TryGet_is_case_insensitive_and_reports_unknown()
    {
        Assert.IsTrue(ScenarioCatalog.TryGet("CALENDAR-NOTES", out _));
        Assert.IsFalse(ScenarioCatalog.TryGet("does-not-exist", out _));
    }
}
