namespace Daynote.UiQa.Cli;

/// <summary>The top-level action the harness was asked to perform.</summary>
public enum QaCommand
{
    /// <summary>Print usage. Never launches the product.</summary>
    Help,

    /// <summary>Print the deterministic scenario registry as JSON. Never launches the product.</summary>
    List,

    /// <summary>Read-only, payload-redacted inspection of a Daynote database. Never launches the product.</summary>
    Inspect,

    /// <summary>Run one deterministic scenario. This is the ONLY command that launches anything.</summary>
    Run,
}

/// <summary>
/// Parsed, validated command line for the harness. Parsing is pure: it performs no I/O and starts
/// no process, so <c>--help</c>, <c>--list</c>, and a plain build can never launch the product.
/// </summary>
public sealed class QaArguments
{
    private QaArguments(QaCommand command)
    {
        Command = command;
    }

    public QaCommand Command { get; private init; }

    public string? Scenario { get; private init; }

    public string? EvidenceDirectory { get; private init; }

    public IReadOnlyList<string> Queries { get; private init; } = Array.Empty<string>();

    public string? PackagePath { get; private init; }

    public string? InspectDatabasePath { get; private init; }

    /// <summary>When true the harness leaves the disposable QA data root on disk after the run so a
    /// follow-up preservation check can observe it. Cleanup still only ever targets the QA namespace.</summary>
    public bool KeepData { get; private init; }

    public static QaArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return new QaArguments(QaCommand.Help);
        }

        string? scenario = null;
        string? evidence = null;
        string? queries = null;
        string? packagePath = null;
        string? inspect = null;
        bool keepData = false;
        bool list = false;
        bool help = false;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--help" or "-h":
                    help = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "--keep-data":
                    keepData = true;
                    break;
                case "--scenario":
                    scenario = Next(args, ref index, argument);
                    break;
                case "--evidence":
                    evidence = Next(args, ref index, argument);
                    break;
                case "--queries":
                    queries = Next(args, ref index, argument);
                    break;
                case "--package-path":
                    packagePath = Next(args, ref index, argument);
                    break;
                case "--inspect":
                    inspect = Next(args, ref index, argument);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (help)
        {
            return new QaArguments(QaCommand.Help);
        }

        if (inspect is not null)
        {
            return new QaArguments(QaCommand.Inspect) { InspectDatabasePath = inspect };
        }

        if (list)
        {
            return new QaArguments(QaCommand.List);
        }

        if (scenario is null)
        {
            throw new ArgumentException("A run requires --scenario <name> (or use --list / --help).");
        }

        if (string.IsNullOrWhiteSpace(evidence))
        {
            throw new ArgumentException("--evidence <dir> is required so every run writes its evidence somewhere.");
        }

        string[] parsedQueries = queries is null
            ? Array.Empty<string>()
            : queries.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new QaArguments(QaCommand.Run)
        {
            Scenario = scenario,
            EvidenceDirectory = evidence,
            Queries = parsedQueries,
            PackagePath = packagePath,
            KeepData = keepData,
        };
    }

    private static string Next(IReadOnlyList<string> args, ref int index, string argument)
    {
        if (++index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{argument} requires a value.");
        }

        return args[index];
    }

    public const string Usage = """
        Daynote.UiQa - deterministic UI/OS QA harness for the Daynote desktop app.

          --scenario <name> --evidence <dir> [options]   Run one scenario (launches the real app)
          --list                                         Print the scenario registry as JSON (no launch)
          --inspect <daynote.db>                         Print payload-redacted DB row counts (no launch)
          --help, -h                                     Print this usage (no launch)

        Options for --scenario:
          --queries "a|b|c"    Pipe-separated literal search queries (used by unified-search)
          --package-path <p>   Path to the installed/packaged app for package scenarios
          --keep-data          Leave the disposable QA data root on disk (preservation checks)

        Only --scenario launches a process. --help, --list, --inspect, and a plain build never do.
        Scenarios run the real product against a disposable data root nested under
        %LocalAppData%\Daynote\.uiqa; the harness only ever deletes inside that namespace.
        """;
}
