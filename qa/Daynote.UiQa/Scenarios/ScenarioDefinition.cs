using System.Text.Json.Serialization;

namespace Daynote.UiQa.Scenarios;

/// <summary>
/// One registered deterministic scenario. Metadata is inert data (safe to print with <c>--list</c>);
/// the <see cref="Run"/> delegate is invoked only for an actual <c>--scenario</c> run.
/// </summary>
public sealed class ScenarioDefinition
{
    public ScenarioDefinition(
        string name,
        string title,
        string description,
        bool requiresPackagedApp,
        bool writesSystemClipboard,
        bool launchesProcessFleet,
        Func<UiQaScenarioContext, ScenarioResult> run)
    {
        Name = name;
        Title = title;
        Description = description;
        RequiresPackagedApp = requiresPackagedApp;
        WritesSystemClipboard = writesSystemClipboard;
        LaunchesProcessFleet = launchesProcessFleet;
        Run = run;
    }

    public string Name { get; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>True when the scenario can only run against an installed/packaged app (MSIX, startup policy).</summary>
    public bool RequiresPackagedApp { get; }

    /// <summary>True when the scenario writes the real system clipboard (contention/dedup/image).</summary>
    public bool WritesSystemClipboard { get; }

    /// <summary>True when the scenario starts many product processes (single-instance proof).</summary>
    public bool LaunchesProcessFleet { get; }

    /// <summary>
    /// True when running this scenario mutates the host beyond a disposable data root — installing a
    /// package, writing the system clipboard, or launching a process fleet. Per the 2026-07-20 user
    /// decision these run only in a disposable VM; the harness still contains their real logic.
    /// </summary>
    [JsonIgnore]
    public bool DeferredOnAuthoringMachine =>
        RequiresPackagedApp || WritesSystemClipboard || LaunchesProcessFleet;

    [JsonIgnore]
    public Func<UiQaScenarioContext, ScenarioResult> Run { get; }

    /// <summary>Projection used by <c>--list</c>; excludes the delegate.</summary>
    public ScenarioListing ToListing() => new(
        Name,
        Title,
        Description,
        RequiresPackagedApp,
        WritesSystemClipboard,
        LaunchesProcessFleet,
        DeferredOnAuthoringMachine);
}

public sealed record ScenarioListing(
    string Name,
    string Title,
    string Description,
    bool RequiresPackagedApp,
    bool WritesSystemClipboard,
    bool LaunchesProcessFleet,
    bool DeferredOnAuthoringMachine);
