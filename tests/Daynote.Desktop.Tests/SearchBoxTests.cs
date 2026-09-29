using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The ⌘K palette: the sidebar's search box and the chord open it, typing in it reaches the unified
/// search, Escape and a click outside close it and take the query with them.
/// </summary>
/// <remarks>
/// Reported from use, back when the box sat in the title bar: nothing appears. The earlier test set
/// <c>Search.Query</c> directly, which exercised the view model and skipped the one thing a user does
/// — put characters in the TextBox — so a broken <c>Text</c> binding would have passed it. These
/// drive real key input instead.
/// </remarks>
[TestClass]
public sealed class SearchBoxTests
{
    [TestMethod]
    public void Typing_in_the_box_reaches_the_query()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.OpenPaletteCommand.Execute(null);
            Pump();
            window.UpdateLayout();

            var box = window.FindControl<TextBox>("SearchBox")!;
            Assert.IsTrue(box.IsEffectivelyVisible, "The palette's query box is not on screen.");
            box.Focus();
            window.KeyTextInput("회의");
            Pump();

            Assert.AreEqual("회의", box.Text, "The TextBox did not take the typed text.");
            Assert.AreEqual("회의", shell.Search.Query, "The typed text never reached Search.Query; the Text binding is one-way.");
            Assert.IsTrue(shell.Search.IsOpen, "A non-empty query must switch the palette to results.");
        });
    }

    [TestMethod]
    public void The_chord_opens_the_palette_with_the_caret_in_it()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            Assert.IsFalse(shell.IsPaletteOpen);

            KeyModifiers chord = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            window.KeyPress(Key.K, (RawInputModifiers)chord, PhysicalKey.K, "k");
            Pump();
            window.UpdateLayout();

            Assert.IsTrue(shell.IsPaletteOpen, "The chord did not open the palette.");
            Assert.IsTrue(window.FindControl<Border>("PaletteScrim")!.IsVisible, "The palette is open but not drawn.");
            Assert.IsTrue(window.FindControl<TextBox>("SearchBox")!.IsFocused, "The caret is not in the query box.");
        });
    }

    [TestMethod]
    public void Escape_closes_the_palette_and_drops_the_query()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.OpenPaletteCommand.Execute(null);
            Pump();
            window.UpdateLayout();
            var box = window.FindControl<TextBox>("SearchBox")!;
            box.Focus();
            window.KeyTextInput("회의");
            Pump();

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Pump();

            Assert.IsFalse(shell.IsPaletteOpen, "Escape left the palette open.");
            Assert.AreEqual(string.Empty, shell.Search.Query, "The query outlived the palette.");
        });
    }

    [TestMethod]
    public void A_quick_action_runs_and_closes_the_palette()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            shell.OpenPaletteCommand.Execute(null);
            Pump();

            // "타임라인 보기", the third action, as the design lists them.
            shell.QuickActions[2].RunCommand.Execute(null);
            Pump();

            Assert.IsFalse(shell.IsPaletteOpen, "The palette stayed over what the action opened.");
            Assert.IsTrue(shell.IsTimelineMode, "The timeline action did not open the timeline.");
        });
    }

    private static void Pump()
    {
        for (int i = 0; i < 20; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }
}
