using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The sticky note draws one title bar, not two.
/// </summary>
/// <remarks>
/// <c>ExtendClientAreaToDecorationsHint</c> only moves the client area up under the decorations — the
/// theme still draws a title bar there. This window draws its own row with the note's name and its
/// pin and close buttons, so for as long as it did not also say <c>BorderOnly</c> the two stacked:
/// two bars, the title twice, two close buttons. <see cref="MainWindow"/> has said it since the
/// chrome work; this window was missed.
/// <para>
/// Asserted on the window rather than on a screenshot because the fault is a property, and because
/// the doubled bar is drawn by the platform: it does not exist in a headless render at all, so no
/// amount of pixel checking here would have caught it.
/// </para>
/// </remarks>
[TestClass]
public sealed class StickyNoteChromeTests
{
    [TestMethod]
    public void It_drops_the_theme_title_bar_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("macOS keeps its traffic lights and the theme bar with them.");
        }

        WithStickyNote(sticky => Assert.AreEqual(
            WindowDecorations.BorderOnly,
            sticky.WindowDecorations,
            "The theme would draw a second title bar over the note's own."));
    }

    [TestMethod]
    public void Its_own_strip_behaves_as_the_title_bar()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The roles only matter where the app draws the strip itself.");
        }

        WithStickyNote(sticky =>
        {
            Control strip = Find(sticky, "TitleBarRow");
            Assert.AreEqual(
                WindowDecorationsElementRole.TitleBar,
                WindowDecorationProperties.GetElementRole(strip),
                "Without the role the strip is client area and the note cannot be dragged by it.");

            // A Grid with no brush takes no part in hit testing, so the role alone is not enough.
            Assert.IsNotNull(strip.GetValue(Panel.BackgroundProperty), "The strip needs a brush to be hit-tested.");

            foreach (string name in new[] { "PinButton", "CloseButton" })
            {
                Assert.AreEqual(
                    WindowDecorationsElementRole.User,
                    WindowDecorationProperties.GetElementRole(Find(sticky, name)),
                    $"{name} would have its clicks eaten by the non-client hit test.");
            }
        });
    }

    [TestMethod]
    public void The_pin_shows_its_state_as_a_class_not_as_fading()
    {
        WithStickyNote(sticky =>
        {
            var pin = (Button)Find(sticky, "PinButton");

            Assert.IsTrue(sticky.Topmost, "A new note opens pinned.");
            Assert.IsTrue(pin.Classes.Contains("pinned"), "The pin should be filled while the note is on top.");

            RaiseClick(pin);
            Assert.IsFalse(sticky.Topmost);
            Assert.IsFalse(pin.Classes.Contains("pinned"), "Unpinned, the mark goes back to an outline.");

            RaiseClick(pin);
            Assert.IsTrue(sticky.Topmost);
            Assert.IsTrue(pin.Classes.Contains("pinned"));
        });
    }

    private static void RaiseClick(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static Control Find(Window window, string name) =>
        window.GetLogicalDescendants().OfType<Control>().Single(c => c.Name == name);

    /// <summary>
    /// Opens a sticky note. The chrome is applied in <c>OnOpened</c>, because
    /// <see cref="Window.WindowDecorations"/> means nothing until the platform impl exists — so the
    /// window has to be shown, not merely constructed.
    /// </summary>
    private static void WithStickyNote(Action<StickyNoteWindow> body)
    {
        HeadlessAppFixture.OnUiThread(() =>
        {
            var sticky = new StickyNoteWindow();
            try
            {
                sticky.Show();
                body(sticky);
            }
            finally
            {
                sticky.Close();
            }
        });
    }
}
