using System.IO;
using System.Text.Json;
using Daynote.UiQa.Cli;
using Daynote.UiQa.Data;
using Daynote.UiQa.Scenarios;

namespace Daynote.UiQa;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            QaArguments arguments = QaArguments.Parse(args);
            return arguments.Command switch
            {
                QaCommand.Help => Help(),
                QaCommand.List => List(),
                QaCommand.Inspect => Inspect(arguments.InspectDatabasePath!),
                QaCommand.Run => Run(arguments),
                _ => Help(),
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(QaArguments.Usage);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine(QaArguments.Usage);
        return 0;
    }

    private static int List()
    {
        var listing = ScenarioCatalog.All.Select(static definition => definition.ToListing()).ToList();
        Console.WriteLine(JsonSerializer.Serialize(listing, JsonOptions));
        return 0;
    }

    private static int Inspect(string databasePath)
    {
        DatabaseSnapshot snapshot = DatabaseInspector.Inspect(databasePath);
        Console.WriteLine(snapshot.ToJson());
        // Non-zero when the database is present but its search index has drifted or FKs are broken.
        if (!snapshot.Exists)
        {
            return 0;
        }

        return snapshot.ForeignKeyViolations == 0 && snapshot.SearchIndexSynchronized ? 0 : 3;
    }

    private static int Run(QaArguments arguments)
    {
        if (!ScenarioCatalog.TryGet(arguments.Scenario!, out ScenarioDefinition definition))
        {
            Console.Error.WriteLine($"Unknown scenario '{arguments.Scenario}'. Use --list to see the registry.");
            return 2;
        }

        string evidence = Path.GetFullPath(arguments.EvidenceDirectory!);
        Directory.CreateDirectory(evidence);

        var context = new UiQaScenarioContext(definition, arguments, evidence);
        ScenarioResult result = definition.Run(context);
        context.Log.Save(evidence);

        var summary = new ScenarioSummary(
            definition.Name,
            result.Passed,
            result.Observables,
            evidence,
            definition.DeferredOnAuthoringMachine,
            definition.DeferredOnAuthoringMachine
                ? "Live execution runs only in a disposable VM per the 2026-07-20 user decision."
                : null,
            DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(evidence, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));

        foreach (ScenarioObservable observable in result.Observables)
        {
            Console.WriteLine($"[{(observable.Passed ? "PASS" : "FAIL")}] {observable.Name}: {observable.Detail}");
        }

        Console.WriteLine(result.Passed ? "SCENARIO PASS" : "SCENARIO FAIL");
        return result.Passed ? 0 : 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
