using Daynote.UiQa.Cli;
using Daynote.UiQa.Evidence;

namespace Daynote.UiQa.Scenarios;

/// <summary>Everything a scenario needs to run: where to write evidence, the parsed inputs, and a
/// shared action log. Constructed only for an actual run, never for --list/--help.</summary>
public sealed class UiQaScenarioContext
{
    public UiQaScenarioContext(ScenarioDefinition definition, QaArguments arguments, string evidenceDirectory)
    {
        Definition = definition;
        Arguments = arguments;
        EvidenceDirectory = evidenceDirectory;
        Log = new ActionLog();
    }

    public ScenarioDefinition Definition { get; }

    public QaArguments Arguments { get; }

    public string EvidenceDirectory { get; }

    public ActionLog Log { get; }

    public IReadOnlyList<string> Queries => Arguments.Queries;

    public string? PackagePath => Arguments.PackagePath;

    public bool KeepData => Arguments.KeepData;
}
