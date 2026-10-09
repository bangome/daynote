using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Renaming a note from the editor header, and the helpers that write the body's syntax.
/// </summary>
/// <remarks>
/// Both are phone-only: the desktop renames with a double-click and types <c>-[]</c> on a keyboard
/// that has brackets. The helpers edit the text box directly rather than going through the view
/// model, so a test that only drove commands would miss the part that can actually go wrong — where
/// the caret lands and which line was touched.
/// </remarks>
[TestClass]
public sealed class EditorHelperTests
{
    [TestMethod]
    public void Renaming_from_the_header_reaches_the_note()
    {
        WithEditor((_, shell) =>
        {
            string original = shell.Notes.SelectedTab!.Title;

            shell.BeginRenameTitleCommand.Execute(null);
            Assert.IsTrue(shell.IsRenamingTitle, "Tapping the title did not open it for editing.");
            Assert.AreEqual(original, shell.TitleDraft, "The draft did not start from the current name.");

            shell.TitleDraft = "  회의 기록  ";
            Pump(() => shell.CommitRenameTitleCommand.ExecuteAsync(null));

            Assert.IsFalse(shell.IsRenamingTitle);
            Assert.AreEqual("회의 기록", shell.Notes.SelectedTab!.Title, "The trimmed name did not stick.");
        });
    }

    [TestMethod]
    public void An_empty_name_leaves_the_note_alone()
    {
        WithEditor((_, shell) =>
        {
            string original = shell.Notes.SelectedTab!.Title;
            shell.BeginRenameTitleCommand.Execute(null);
            shell.TitleDraft = "   ";
            Pump(() => shell.CommitRenameTitleCommand.ExecuteAsync(null));

            Assert.AreEqual(original, shell.Notes.SelectedTab!.Title, "An empty box renamed the note.");
        });
    }

    [TestMethod]
    public void The_date_helper_appends_the_notes_own_day_and_replaces_its_own_suffix()
    {
        WithEditor((view, shell) =>
        {
            TextBox body = Body(view);
            body.Text = "보고서 보내기";
            body.CaretIndex = 0;

            Tap(view, "InsertDate");
            string expected = $"({shell.SelectedDate.Month}/{shell.SelectedDate.Day})";
            // Plain text. It used to be read back as a to-do's due date; nothing parses it now.
            Assert.AreEqual($"보고서 보내기 {expected}", body.Text, "The date was not appended.");

            // A line that already ends in one gets a replacement, not a second suffix.
            Tap(view, "MobileInsertTime");
            Assert.AreEqual(
                1,
                System.Text.RegularExpressions.Regex.Matches(body.Text!, @"\(\d{1,2}/\d{1,2}").Count,
                $"A second due suffix was added: {body.Text}");
            StringAssert.Matches(
                body.Text!,
                new System.Text.RegularExpressions.Regex(@"\(\d{1,2}/\d{1,2} \d{2}:\d{2}\)$"),
                "The time form did not replace the date-only one.");
        });
    }

    private static TextBox Body(EditorPage page) =>
        page.GetLogicalDescendants().OfType<TextBox>().First(t => t.Name == "Body");

    /// <summary>Presses the helper whose content is the given catalog key's label.</summary>
    private static void Tap(EditorPage page, string key)
    {
        string label = ViewModels.MobileStrings.Instance[key];
        Button button = page.GetLogicalDescendants()
            .OfType<Button>()
            .First(b => b.Classes.Contains("tool")
                && b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == label));

        // The helpers are Click handlers on a non-focusable button; raising the event is what a tap
        // does, and it keeps the caret and the keyboard where they were.
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>An initialised shell with one note open in the editor.</summary>
    private static void WithEditor(Action<EditorPage, ViewModels.MobileShellViewModel> body)
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            Pump(() => shell.NewNoteCommand.ExecuteAsync(null));
            view.UpdateLayout();

            EditorPage page = view.GetLogicalDescendants().OfType<EditorPage>().Single();
            Assert.IsTrue(page.IsVisible, "The editor did not open.");
            body(page, shell);
        });
    }

    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The command did not complete within 20 seconds.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
}
