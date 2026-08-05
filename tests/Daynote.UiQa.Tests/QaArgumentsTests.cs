using Daynote.UiQa.Cli;

namespace Daynote.UiQa.Tests;

[TestClass]
public sealed class QaArgumentsTests
{
    [TestMethod]
    public void No_args_is_help_and_never_runs()
    {
        QaArguments parsed = QaArguments.Parse(Array.Empty<string>());
        Assert.AreEqual(QaCommand.Help, parsed.Command);
    }

    [TestMethod]
    public void Help_and_list_do_not_require_evidence()
    {
        Assert.AreEqual(QaCommand.Help, QaArguments.Parse(new[] { "--help" }).Command);
        Assert.AreEqual(QaCommand.List, QaArguments.Parse(new[] { "--list" }).Command);
    }

    [TestMethod]
    public void Inspect_is_read_only_command_without_evidence()
    {
        QaArguments parsed = QaArguments.Parse(new[] { "--inspect", @"C:\tmp\daynote.db" });
        Assert.AreEqual(QaCommand.Inspect, parsed.Command);
        Assert.AreEqual(@"C:\tmp\daynote.db", parsed.InspectDatabasePath);
    }

    [TestMethod]
    public void Run_requires_scenario_and_evidence()
    {
        Assert.ThrowsExactly<ArgumentException>(() => QaArguments.Parse(new[] { "--scenario", "calendar-notes" }));
        Assert.ThrowsExactly<ArgumentException>(() => QaArguments.Parse(new[] { "--evidence", "out" }));
    }

    [TestMethod]
    public void Run_parses_scenario_evidence_and_pipe_separated_queries()
    {
        QaArguments parsed = QaArguments.Parse(new[]
        {
            "--scenario", "unified-search",
            "--evidence", "out",
            "--queries", "오|검색|%_",
        });

        Assert.AreEqual(QaCommand.Run, parsed.Command);
        Assert.AreEqual("unified-search", parsed.Scenario);
        Assert.AreEqual("out", parsed.EvidenceDirectory);
        CollectionAssert.AreEqual(new[] { "오", "검색", "%_" }, parsed.Queries.ToArray());
    }

    [TestMethod]
    public void Unknown_argument_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => QaArguments.Parse(new[] { "--nope" }));
    }

    [TestMethod]
    public void Missing_value_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => QaArguments.Parse(new[] { "--scenario" }));
    }
}
