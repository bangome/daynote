using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Daynote.Motion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The layout by window width (Daynote Tablet §00, Mobile B Foldables §00): which one a width gets,
/// that a running window follows a resize without losing what is being typed, that a hinge moves
/// the split onto the fold, that the panel a resize brings in plays M7, and that a hardware
/// keyboard turns the @ bar into the card.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LayoutTests
{
    [TestCleanup]
    public void Restore()
    {
        MotionEnvironment.Instant = true;
        MotionPlayer.Interceptor = null;
    }

    [TestMethod]
    [DataRow(375, 667, MobileLayout.Phone)]
    [DataRow(402, 874, MobileLayout.Phone)]
    [DataRow(599, 900, MobileLayout.Phone)]
    [DataRow(600, 900, MobileLayout.TwoPane)]
    [DataRow(690, 829, MobileLayout.TwoPane)]
    [DataRow(839, 1100, MobileLayout.TwoPane)]
    [DataRow(840, 1100, MobileLayout.TabletCompact)]
    [DataRow(834, 1210, MobileLayout.TwoPane)]
    [DataRow(1032, 1376, MobileLayout.TabletCompact)]
    [DataRow(1099, 800, MobileLayout.TabletCompact)]
    [DataRow(1100, 800, MobileLayout.Tablet)]
    [DataRow(1210, 834, MobileLayout.Tablet)]
    [DataRow(874, 402, MobileLayout.Phone)]
    [DataRow(915, 412, MobileLayout.Phone)]
    public void The_layout_is_chosen_by_the_window_not_the_device(double width, double height, MobileLayout expected) =>
        Assert.AreEqual(expected, MobileLayouts.For(width, height));

    [TestMethod]
    public void A_resize_switches_the_layout_live_and_keeps_the_draft_and_the_caret()
    {
        TestServices.WithInitialisedShell(402, 874, (view, shell) =>
        {
            var window = (Window)TopLevel.GetTopLevel(view)!;
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            ScreenshotTests.Pump(() => shell.Notes.SelectNoteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
            shell.IsEditorOpen = true;
            Settle(view);

            EditorPage editor = view.GetVisualDescendants().OfType<EditorPage>().Single();
            TextBox body = editor.FindControl<TextBox>("Body")!;
            body.Text += "\n펼치기 전에 쓰던 줄";
            body.CaretIndex = body.Text!.Length - 3;
            int caret = body.CaretIndex;
            string draft = body.Text;
            Assert.AreEqual(MobileLayout.Phone, shell.Layout);

            foreach ((double width, double height, MobileLayout layout) in new[]
            {
                (690.0, 829.0, MobileLayout.TwoPane),
                (1210.0, 834.0, MobileLayout.Tablet),
                (900.0, 1100.0, MobileLayout.TabletCompact),
                (402.0, 874.0, MobileLayout.Phone),
            })
            {
                window.Width = width;
                window.Height = height;
                Settle(view);

                Assert.AreEqual(layout, shell.Layout, $"{width}x{height}");
                Assert.AreSame(editor, view.GetVisualDescendants().OfType<EditorPage>().Single(), "The editor was rebuilt.");
                Assert.AreEqual(draft, body.Text, $"{layout}: the draft changed.");
                Assert.AreEqual(caret, body.CaretIndex, $"{layout}: the caret moved.");
                Assert.IsTrue(shell.IsEditorOpen, $"{layout}: the note closed.");
                Assert.IsTrue(editor.IsEffectivelyVisible, $"{layout}: the note is not on screen.");
            }
        });
    }

    [TestMethod]
    public void Two_panes_keep_the_note_open_beside_another_tab()
    {
        TestServices.WithInitialisedShell(690, 829, (view, shell) =>
        {
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            ScreenshotTests.Pump(() => shell.OpenNoteCommand.ExecuteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
            Settle(view);
            Assert.AreEqual(MobileLayout.TwoPane, shell.Layout);

            shell.GoToPageCommand.Execute(MobilePage.Lists);
            Settle(view);
            Assert.IsTrue(shell.IsEditorOpen, "Lists | note (F2): the note stays beside the panel.");
            Assert.IsFalse(shell.ShowDock, "The rail stands in for the tab bar.");
            Assert.IsTrue(view.FindControl<NavRail>("Rail")!.IsEffectivelyVisible);
        });
    }

    [TestMethod]
    public void A_hinge_down_the_window_puts_the_split_on_the_fold()
    {
        var device = new FakeDevice { Hinge = new Rect(345, 0, 0, 829) };
        TestServices.WithInitialisedShell(690, 829, (view, shell) =>
        {
            view.Device = device;
            Settle(view);

            Panel pages = view.FindControl<Panel>("PageColumn")!;
            Point at = pages.TranslatePoint(new Point(pages.Bounds.Width, 0), view)!.Value;
            Assert.AreEqual(345, at.X, 0.5, "The panel should end where the hinge begins.");

            device.Hinge = null;
            device.Raise();
            Settle(view);
            at = pages.TranslatePoint(new Point(pages.Bounds.Width, 0), view)!.Value;
            Assert.AreNotEqual(345, at.X, 0.5, "With no hinge the split is the layout's own.");
        });
    }

    [TestMethod]
    public void Unfolding_brings_the_panel_in_with_M7_and_the_note_stays_put()
    {
        TestServices.WithInitialisedShell(344, 882, (view, shell) =>
        {
            var window = (Window)TopLevel.GetTopLevel(view)!;
            var played = new List<(Visual Owner, string Channel, Storyboard Board)>();
            MotionPlayer.Interceptor = (owner, channel, board) =>
            {
                played.Add((owner, channel, board));
                board.Seek(board.Duration);
                return Task.FromResult(true);
            };

            window.Width = 690;
            window.Height = 829;
            Settle(view);

            Assert.AreEqual(MobileLayout.TwoPane, shell.Layout);
            (Visual owner, _, Storyboard m7) = played.Single(p => p.Channel == "m7");
            Assert.AreSame(view.FindControl<Panel>("PageColumn"), owner, "Only the panel appears; the note stays where it is.");
            Assert.AreEqual(320, m7.Duration.TotalMilliseconds, 1, "snappy");
        });
    }

    [TestMethod]
    public void A_hardware_keyboard_turns_the_at_bar_into_the_card_under_the_caret()
    {
        var device = new FakeDevice { HasHardwareKeyboard = true };
        TestServices.WithInitialisedShell(1210, 834, (view, shell) =>
        {
            view.Device = device;
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            ScreenshotTests.Pump(() => shell.OpenNoteCommand.ExecuteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
            Settle(view);

            EditorPage editor = view.GetVisualDescendants().OfType<EditorPage>().Single();
            Border card = editor.FindControl<Border>("CapturePopover")!;
            StackPanel bar = editor.FindControl<StackPanel>("CaptureBar")!;
            Assert.AreSame(card, bar.Parent, "With a keyboard, the reading lives in the card.");

            TextBox body = editor.FindControl<TextBox>("Body")!;
            body.Text += "\n회의자료 초안 공유 @오늘 5시";
            body.CaretIndex = body.Text!.Length;
            Settle(view);
            Assert.IsTrue(shell.Capture.IsOpen, "The @ did not open a reading.");
            Assert.IsTrue(card.IsVisible, "The card is not up.");
            Assert.IsTrue(editor.FindControl<TextBlock>("CaptureHints")!.IsVisible, "The key hints belong on the card.");

            device.HasHardwareKeyboard = false;
            device.Raise();
            Settle(view);
            Assert.AreNotSame(card, bar.Parent, "Without one it goes back to being the bar.");
            Assert.IsFalse(card.IsVisible);
            Assert.IsTrue(shell.Capture.IsOpen, "Changing shape kept what was being typed.");
        });
    }

    private static void Settle(Control view)
    {
        for (int i = 0; i < 5; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
        }
    }

    private sealed class FakeDevice : IDeviceShape
    {
        public Rect? Hinge { get; set; }

        public bool HasHardwareKeyboard { get; set; }

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
