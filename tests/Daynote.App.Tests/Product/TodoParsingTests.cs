using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>Pure tests for the todo grammar/sort/toggle port of the design's parseTodos/toggleTodo.</summary>
[TestClass]
public sealed class TodoParsingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 9, 0, 0, TimeSpan.Zero);

    private static NoteSummary Note(string body, string title = "노트 1") =>
        new(Guid.NewGuid(), LocalDate.Parse("2026-07-21").Value, title, body, 0, false);

    [TestMethod]
    public void Parse_DetectsCheckedAndUnchecked()
    {
        IReadOnlyList<TodoLine> todos = TodoParsing.Parse([Note("-[] open\n-[x] done")], Now);

        Assert.AreEqual(2, todos.Count);
        Assert.IsFalse(todos.Single(t => t.Text == "open").Checked);
        Assert.IsTrue(todos.Single(t => t.Text == "done").Checked);
    }

    [TestMethod]
    public void Parse_ExtractsDueWithTime()
    {
        TodoLine todo = TodoParsing.Parse([Note("-[] ship (7/25 14:00)")], Now).Single();

        Assert.AreEqual("ship", todo.Text);
        Assert.AreEqual("7/25 14:00", todo.DueLabel);
        Assert.IsNotNull(todo.Due);
        Assert.AreEqual(new DateTimeOffset(2026, 7, 25, 14, 0, 0, TimeSpan.Zero), todo.Due!.Value);
    }

    [TestMethod]
    public void Parse_ExtractsDueDateOnlyDefaultsToEndOfDay()
    {
        TodoLine todo = TodoParsing.Parse([Note("-[] plan (8/1)")], Now).Single();

        Assert.AreEqual("8/1", todo.DueLabel);
        Assert.AreEqual(new DateTimeOffset(2026, 8, 1, 23, 59, 0, TimeSpan.Zero), todo.Due!.Value);
    }

    [TestMethod]
    public void Parse_MarksUncheckedPastDueAsOverdue()
    {
        TodoLine overdue = TodoParsing.Parse([Note("-[] late (7/20 08:00)")], Now).Single();
        TodoLine doneLate = TodoParsing.Parse([Note("-[x] late (7/20 08:00)")], Now).Single();

        Assert.IsTrue(overdue.Overdue);
        Assert.IsFalse(doneLate.Overdue, "A checked past-due item is not overdue.");
    }

    [TestMethod]
    public void Parse_EmptyTaskTextFallsBackToPlaceholder()
    {
        TodoLine todo = TodoParsing.Parse([Note("-[] ")], Now).Single();

        Assert.AreEqual(Daynote.App.Localization.AppStrings.TodoEmptyText, todo.Text);
    }

    [TestMethod]
    public void Parse_SortsUncheckedBeforeCheckedThenByDue()
    {
        var note = Note("-[x] done\n-[] later (7/30)\n-[] sooner (7/22)\n-[] undated");

        IReadOnlyList<TodoLine> todos = TodoParsing.Parse([note], Now);

        CollectionAssert.AreEqual(
            new[] { "sooner", "later", "undated", "done" },
            todos.Select(t => t.Text).ToArray());
    }

    [TestMethod]
    public void Parse_IgnoresNonCheckboxLines()
    {
        IReadOnlyList<TodoLine> todos = TodoParsing.Parse([Note("plain text\n- bullet\n-[] real")], Now);

        Assert.AreEqual(1, todos.Count);
        Assert.AreEqual("real", todos[0].Text);
    }

    [TestMethod]
    public void ToggleLine_FlipsMarkerBothWays()
    {
        const string body = "intro\n-[] task\ntail";

        string toChecked = TodoParsing.ToggleLine(body, 1);
        Assert.AreEqual("intro\n-[x] task\ntail", toChecked);

        string backToOpen = TodoParsing.ToggleLine(toChecked, 1);
        Assert.AreEqual("intro\n-[ ] task\ntail", backToOpen);
    }

    [TestMethod]
    public void ToggleLine_LeavesNonCheckboxAndOutOfRangeUnchanged()
    {
        Assert.AreEqual("just text", TodoParsing.ToggleLine("just text", 0));
        Assert.AreEqual("-[] a", TodoParsing.ToggleLine("-[] a", 5));
    }
}
