using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
    public void With_a_hardware_keyboard_an_at_at_a_line_start_opens_the_sheet_and_no_card()
    {
        var device = new FakeDevice { HasHardwareKeyboard = true };
        TestServices.WithInitialisedShell(1210, 834, (view, shell) =>
        {
            view.Device = device;
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            ScreenshotTests.Pump(() => shell.OpenNoteCommand.ExecuteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
            Settle(view);

            EditorPage editor = view.GetVisualDescendants().OfType<EditorPage>().Single();
            Assert.IsNull(editor.FindControl<Border>("CapturePopover"), "The @ card is still in the editor.");
            TextBox body = editor.FindControl<TextBox>("Body")!;
            body.Text += "\n";
            string before = body.Text!;

            TodoSheetTests.TypeAt(view, body, before.Length);

            Assert.IsTrue(shell.IsTodoSheetOpen, "The @ did not open the sheet.");
            Assert.AreEqual(before, body.Text, "The @ stayed in the note.");
        });
    }

    /// <summary>
    /// Settings - and the account, the plans and the profile switch behind it - is reachable in
    /// every layout, by a tap on what is on screen, and so is the way back to the day. The tablet
    /// has no tab bar and an iPad no back gesture, so this is not a given.
    /// </summary>
    [TestMethod]
    [DataRow(402, 874, MobileLayout.Phone)]
    [DataRow(690, 829, MobileLayout.TwoPane)]
    [DataRow(1032, 1376, MobileLayout.TabletCompact)]
    [DataRow(1210, 834, MobileLayout.Tablet)]
    public void Every_layout_reaches_settings_and_comes_back(double width, double height, MobileLayout layout)
    {
        TestServices.WithInitialisedShell(width, height, (view, shell) =>
        {
            Settle(view);
            Assert.AreEqual(layout, shell.Layout);

            foreach (MobilePage page in new[] { MobilePage.Lists, MobilePage.Search, MobilePage.Settings })
            {
                shell.GoToPageCommand.Execute(page);
                Settle(view);
                if (layout == MobileLayout.TabletCompact)
                {
                    Assert.IsTrue(view.FindControl<Button>("SidebarButton")!.IsEffectivelyVisible, $"No way to the sidebar from {page}.");
                }
            }

            shell.GoToPageCommand.Execute(MobilePage.Day);
            Settle(view);
            OpenSidebarIfFolded(view, layout);
            Click(view, VisibleButton(view, b => b.Command == shell.GoToPageCommand && Equals(b.CommandParameter, MobilePage.Settings)));
            Assert.IsTrue(shell.IsSettingsPage, $"{layout}: no tap reached settings.");
            Assert.IsFalse(shell.IsSidebarOpen, "The overlay should close behind the choice.");

            OpenSidebarIfFolded(view, layout);
            Click(view, VisibleButton(view, b => b.Command == shell.GoToTodayPageCommand));
            Assert.IsTrue(shell.IsDayPage, $"{layout}: no tap came back to the day.");
        });

        static void OpenSidebarIfFolded(MainView view, MobileLayout layout)
        {
            if (layout == MobileLayout.TabletCompact)
            {
                Click(view, view.FindControl<Button>("SidebarButton")!);
            }
        }
    }

    /// <summary>
    /// M6's drag lives in the sheet's top strip, which also holds buttons: a tap on the month
    /// sheet's year arrow must still be a tap, and a pull down must still close the sheet.
    /// </summary>
    [TestMethod]
    public void A_sheet_s_top_strip_keeps_its_taps_and_still_drags_closed()
    {
        TestServices.WithInitialisedShell(402, 874, (view, shell) =>
        {
            shell.OpenMonthPickerCommand.Execute(null);
            Settle(view);
            int year = shell.PickerYear;

            Button next = VisibleButton(view, b => b.Command == shell.PickerNextYearCommand);
            Border sheet = view.FindControl<Border>("Sheet")!;
            Point inSheet = next.TranslatePoint(default, sheet)!.Value;
            Assert.IsLessThan(56, inSheet.Y, "The arrow should sit in the draggable strip for this to test anything.");
            Click(view, next);
            Assert.AreEqual(year + 1, shell.PickerYear, "The tap in the strip was swallowed by the drag.");
            Assert.IsTrue(shell.IsMonthPickerOpen);

            var window = (Window)TopLevel.GetTopLevel(view)!;
            Point top = sheet.TranslatePoint(new Point(sheet.Bounds.Width / 2, 8), window)!.Value;
            window.MouseDown(top, Avalonia.Input.MouseButton.Left);
            for (int dy = 4; dy <= 360; dy += 24)
            {
                window.MouseMove(top + new Point(0, dy));
            }

            window.MouseUp(top + new Point(0, 360), Avalonia.Input.MouseButton.Left);
            Settle(view);
            Assert.IsFalse(shell.IsMonthPickerOpen, "Pulled most of the way down, the sheet should close.");
        });
    }

    /// <summary>The head's device source outlives a view; a view that has gone must not stay subscribed to it.</summary>
    [TestMethod]
    public void A_view_lets_go_of_the_device_when_it_is_detached()
    {
        var device = new FakeDevice();
        TestServices.WithInitialisedShell(690, 829, (view, _) =>
        {
            view.Device = device;
            Assert.AreEqual(1, device.Listeners, "Attached, the view follows the device.");

            var window = (Window)TopLevel.GetTopLevel(view)!;
            window.Content = null;
            Settle(window);
            Assert.AreEqual(0, device.Listeners, "Detached, it still listens: a profile switch would leak it.");

            window.Content = view;
            Settle(window);
            Assert.AreEqual(1, device.Listeners, "Back on screen, it follows again.");
        });
    }

    /// <summary>
    /// Unticking a finished row on the tablet's day panel brings it back out of the fold; it is not
    /// a new to-do, so no orange and no success haptic (M2).
    /// </summary>
    [TestMethod]
    public void Unticking_a_done_row_on_the_tablet_is_not_an_arrival()
    {
        TestServices.WithInitialisedShell(1210, 834, (view, shell) =>
        {
            ScreenshotTests.Seed(shell, LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now)));
            Settle(view);
            DayTodoPanel panel = view.FindControl<DayTodoPanel>("DayPanel")!;
            Click(view, panel.FindControl<Button>("DoneToggle")!);

            var arrived = new List<string>();
            MotionPlayer.Interceptor = (_, channel, board) =>
            {
                arrived.Add(channel);
                board.Seek(board.Duration);
                return Task.FromResult(true);
            };

            TodoCheck done = panel.FindControl<ItemsControl>("DoneList")!.GetVisualDescendants().OfType<TodoCheck>().First();
            string key = done.Key!;
            Click(view, done);
            for (int i = 0; i < 20; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            Settle(view);
            Assert.IsTrue(panel.FindControl<ItemsControl>("OpenList")!.GetVisualDescendants().OfType<TodoCheck>().Any(c => c.Key == key),
                "The unticked row should be back among the open ones.");
            Assert.DoesNotContain("arrive", arrived, "An unticked row replayed the new-to-do arrival.");
        });
    }

    private static Button VisibleButton(Control view, Func<Button, bool> match) =>
        view.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.Bounds.Width > 0 && match(b));

    private static void Click(Control view, Control target)
    {
        var window = (Window)TopLevel.GetTopLevel(view)!;
        Settle(view);
        Point centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        window.MouseMove(new Point(1, 1));
        Settle(view);
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
        private EventHandler? _changed;

        public Rect? Hinge { get; set; }

        public bool HasHardwareKeyboard { get; set; }

        public int Listeners => _changed?.GetInvocationList().Length ?? 0;

        public event EventHandler? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
    }
}
