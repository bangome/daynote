using System.Text.Json.Serialization;

namespace Daynote.UiQa.Scenarios;

/// <summary>A single named, binary pass/fail observable. Never a manual-only assertion: each one is
/// checked against a real artifact (UI Automation state, a DB row count, a process count, a file).</summary>
public sealed record ScenarioObservable(
    string Name,
    bool Passed,
    string Detail);

/// <summary>Outcome of one scenario run: its observables and the overall pass verdict.</summary>
public sealed class ScenarioResult
{
    private readonly List<ScenarioObservable> _observables = new();

    public ScenarioResult(string scenario)
    {
        Scenario = scenario;
    }

    public string Scenario { get; }

    public IReadOnlyList<ScenarioObservable> Observables => _observables;

    /// <summary>Overall pass requires at least one observable and every observable passing.</summary>
    public bool Passed => _observables.Count > 0 && _observables.TrueForAll(static o => o.Passed);

    public void Observe(string name, bool passed, string detail) =>
        _observables.Add(new ScenarioObservable(name, passed, detail));

    /// <summary>Records that a binary observable holds; fails the observable otherwise.</summary>
    public void Expect(string name, bool condition, string detail) =>
        Observe(name, condition, detail);
}

/// <summary>Serializable per-scenario summary written to the evidence directory.</summary>
public sealed record ScenarioSummary(
    string Scenario,
    bool Passed,
    [property: JsonPropertyName("observables")] IReadOnlyList<ScenarioObservable> Observables,
    string EvidenceDirectory,
    bool Deferred,
    string? DeferredReason,
    DateTimeOffset TimestampUtc);
