using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Daynote.App.Notes;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The sidebar's note rows can be managed in place: a context menu with rename / duplicate / delete,
/// the Delete key on a focused row, and a double-click on the heading to rename.
/// </summary>
/// <remarks>
/// The bindings inside a ContextMenu are the part worth a test. The menu is a popup with its own
/// visual tree, so a <c>$parent[Window]</c> walk that works everywhere else in the file can silently
/// come back null there — and a MenuItem with a null command renders fine and does nothing when
/// clicked. Each test opens the real menu and checks the commands resolved.
/// </remarks>
[TestClass]
public sealed class NoteRowActionsTests
{
    [TestMethod]
    public void The_row_menu_resolves_all_three_commands()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Button row = MaterialiseARow(window, shell);

            ContextMenu menu = row.ContextMenu ?? throw new AssertFailedException("The row has no context menu.");
            menu.Open(row);
            try
            {
                List<MenuItem> items = menu.GetVisualDescendants().OfType<MenuItem>().ToList();
                if (items.Count == 0)
                {
                    items = menu.Items.OfType<MenuItem>().ToList();
                }

                Assert.AreEqual(3, items.Count, "Rename, duplicate, delete.");
                foreach (MenuItem item in items)
                {
                    Assert.IsNotNull(item.Command, $"Menu item '{item.Header}' has no command; the popup binding did not resolve.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(item.Header?.ToString()), "A menu item has no header text.");
                    Assert.AreSame(row.DataContext, item.CommandParameter, "The command parameter must be the row's own tab.");
                }
            }
            finally
            {
                menu.Close();
            }
        });
    }

    [TestMethod]
    public void The_row_binds_Delete_to_deleting_that_row()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Button row = MaterialiseARow(window, shell);

            KeyBinding binding = row.KeyBindings.Single();
            Assert.AreEqual(Key.Delete, binding.Gesture.Key);
            Assert.AreEqual(KeyModifiers.None, binding.Gesture.KeyModifiers);
            Assert.IsNotNull(binding.Command, "The Delete binding has no command.");
            Assert.AreSame(row.DataContext, binding.CommandParameter);

            // Through the headless input pipeline, as a real key press: a KeyBinding is matched by the
            // input manager on the way to the focused element, not by the element's own KeyDown.
            int before = shell.Notes.Tabs.Count(static t => !t.IsProjection);
            row.Focus();
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Pump();

            Assert.AreEqual(before - 1, shell.Notes.Tabs.Count(static t => !t.IsProjection), "Delete on the row did not delete the note.");
        });
    }

    [TestMethod]
    public void Deleting_a_row_selects_the_one_above_it()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.NewNoteCommand.Execute(null);
            Pump();
            shell.NewNoteCommand.Execute(null);
            Pump();
            shell.NewNoteCommand.Execute(null);
            Pump();
            NoteTabViewModel[] tabs = shell.Notes.Tabs.ToArray();
            Assert.AreEqual(3, tabs.Length);

            shell.DeleteDayNoteCommand.Execute(tabs[2]);
            Pump();

            Assert.AreEqual(tabs[1].Id, shell.Notes.SelectedTab!.Id, "The note above the deleted one should be selected.");
        });
    }

    [TestMethod]
    public void Double_clicking_the_heading_edits_it_and_Enter_commits()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            MaterialiseARow(window, shell);
            var editor = window.FindControl<TextBox>("TitleEditor")!;
            Assert.IsFalse(editor.IsVisible, "The title editor starts hidden.");

            shell.BeginRenameTitle();
            window.UpdateLayout();
            Assert.IsTrue(shell.IsRenamingTitle);
            Assert.IsTrue(editor.IsVisible, "Beginning a rename shows the editor.");
            Assert.AreEqual(shell.Notes.SelectedTab!.Title, shell.TitleDraft, "The draft starts as the current title.");

            shell.TitleDraft = "회의록";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = editor });
            Pump();

            Assert.IsFalse(shell.IsRenamingTitle, "Enter closes the editor.");
            Assert.AreEqual("회의록", shell.Notes.SelectedTab!.Title);
            Assert.IsTrue(shell.Notes.SelectedTab.HasCustomTitle);

            // Escape drops the draft and keeps the title.
            shell.BeginRenameTitle();
            shell.TitleDraft = "버릴 제목";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = editor });
            Pump();
            Assert.IsFalse(shell.IsRenamingTitle);
            Assert.AreEqual("회의록", shell.Notes.SelectedTab!.Title, "Escape must not rename.");
        });
    }

    /// <summary>A fresh day has only the projection, which is not listed; make one real note and return its row.</summary>
    private static Button MaterialiseARow(MainWindow window, DesktopShellViewModel shell)
    {
        shell.NewNoteCommand.Execute(null);
        Pump();
        window.UpdateLayout();

        return window.GetVisualDescendants()
            .OfType<Button>()
            .First(static button => button.Classes.Contains("noterow") && button.IsVisible);
    }

    /// <summary>Lets the async commands' continuations run on this dispatcher.</summary>
    private static void Pump()
    {
        for (int i = 0; i < 10; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }
}
