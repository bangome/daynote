using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Renaming a note from the editor header.
/// </summary>
/// <remarks>
/// Phone-only: the desktop renames with a double-click. The date and time stamps that used to be
/// tested here are gone; a to-do's day and time are entered in the to-do sheet (TodoSheetTests).
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
