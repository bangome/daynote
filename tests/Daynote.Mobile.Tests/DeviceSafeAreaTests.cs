using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Daynote.App.Composition;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// Every screen, laid out on the handsets whose notches and bottom strips differ the most, keeps
/// what can be tapped out of the notch, the status bar, the home indicator and the navigation bar.
/// </summary>
/// <remarks>
/// <para>
/// The safe areas are the platforms' own figures in points: a Dynamic Island iPhone reports 62
/// above and 34 below, an iPhone SE only its 20-point status bar, a Pixel with gesture navigation
/// 52 and 24, and with the three-button bar 52 and 48. Turned sideways the island moves to a side
/// (62 left and right, 21 below), and Android's button bar goes to the right-hand edge.
/// </para>
/// <para>
/// A button counts only as far as it is on screen: clipped by the scroll view it sits in, a row
/// scrolled under the tab bar is not a row in the home indicator. Nothing is allowed across the
/// line, the floating tab bar included: off a device nothing clips there, but a phone does, and the
/// design's 4-point dip into the strip came out as a bar with its round bottom cut flat.
/// Frames land beside the other screen renders, under <c>artifacts/mobile-screens/devices</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class DeviceSafeAreaTests
{
    private static readonly string OutputDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "mobile-screens", "devices");

    [TestMethod]
    [DataRow("iphone-16-pro", 402, 874, 0, 62, 0, 34)]
    [DataRow("iphone-se", 375, 667, 0, 20, 0, 0)]
    [DataRow("pixel-gesture", 412, 915, 0, 52, 0, 24)]
    [DataRow("pixel-3-button", 412, 915, 0, 52, 0, 48)]
    [DataRow("iphone-16-pro-landscape", 874, 402, 62, 0, 62, 21)]
    [DataRow("pixel-3-button-landscape", 915, 412, 0, 24, 48, 0)]
    // The wide layouts (Daynote Tablet, Mobile B Foldables). An 11-inch iPad reports 24 above and
    // 20 below the home indicator either way up; a Galaxy Z Fold its 28-point status bar and, with
    // the three-button bar, 48 below - inside (690 by 829, two panes) and on the cover (344 wide, a
    // phone). An unfolded Z Flip is a tall phone.
    [DataRow("ipad-11-portrait", 834, 1210, 0, 24, 0, 20)]
    [DataRow("ipad-11-landscape", 1210, 834, 0, 24, 0, 20)]
    [DataRow("z-fold-inner", 690, 829, 0, 28, 0, 48)]
    [DataRow("z-fold-inner-landscape", 829, 690, 0, 28, 0, 48)]
    [DataRow("z-fold-cover", 344, 882, 0, 28, 0, 48)]
    [DataRow("z-flip", 411, 1006, 0, 28, 0, 48)]
    public void Nothing_tappable_sits_under_the_notch_or_the_bottom_strip(
        string device, double width, double height, double left, double top, double right, double bottom)
    {
        var safe = new Thickness(left, top, right, bottom);
        string directory = Path.Combine(OutputDirectory, device);
        Directory.CreateDirectory(directory);
        var failures = new List<string>();

        // Notifications denied at the prompt the seeded to-dos bring up, so the settings page shows
        // its tallest reminder rows: the denial line, the button to the system settings and, as on an
        // Android 14 phone without the grant, the precise-reminders row.
        var denied = new TestServices.ShellSetup(Platform: platform => platform with
        {
            Reminders = new FakeReminderScheduler
            {
                Permission = Daynote.Mobile.Reminders.ReminderPermission.NotDetermined,
                AnswerToRequest = Daynote.Mobile.Reminders.ReminderPermission.Denied,
                ExactAlarms = Daynote.Mobile.Reminders.ExactAlarmState.NotAllowed,
            },
        });

        TestServices.WithInitialisedShell(width, height, denied, (view, shell) =>
        {
            view.PreviewSafeArea = safe;
            LocalDate today = LocalDates.FromDateOnly(DateOnly.FromDateTime(DateTime.Now));
            ScreenshotTests.Seed(shell, today);

            void Check(string screen)
            {
                Settle(view);
                Save(view, Path.Combine(directory, $"{screen}.png"));
                failures.AddRange(Offenders(view, safe).Select(offender => $"{device}/{screen}: {offender}"));
                failures.AddRange(AddButtonsClear(view).Select(offender => $"{device}/{screen}: {offender}"));
            }

            ScreenshotTests.SeedFiles(shell);
            shell.GoToPageCommand.Execute(MobilePage.Day);
            Check("day");

            // The files section sits under the to-dos, below the fold: scrolled to, its rows count.
            ScrollDayToEnd(view);
            Check("day-files");
            ScrollDayToStart(view);

            ScreenshotTests.Pump(() => shell.Notes.SelectNoteAsync(shell.Notes.Tabs.First(t => t.Title == "주간회의 준비")));
            shell.IsEditorOpen = true;
            Check("editor");

            // The to-do sheet, opened from the day's + 할 일 (a note never opens it), with the
            // keyboard down and then up under it: everything in it that is on screen stays above
            // the keyboard's top edge.
            shell.IsEditorOpen = false;
            ScreenshotTests.Pump(() => shell.AddDayTodoCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsTodoSheetOpen, $"{device}: the to-do sheet did not open.");
            Check("todo-sheet");
            double keyboard = Math.Round(height * 0.38);
            view.PreviewKeyboard = keyboard;
            Check("todo-sheet-keyboard");
            failures.AddRange(AboveKeyboard(view, safe, keyboard).Select(offender => $"{device}/todo-sheet-keyboard: {offender}"));

            // Every field filled and the tallest picker open: the fields scroll, 추가 stays put.
            shell.Entry.Title = "회의자료 초안 공유";
            shell.Entry.Description = "슬라이드 12장\n예산표 첨부\n회의실 3층";
            shell.Entry.SelectKindCommand.Execute(Daynote.Core.Agenda.AgendaKind.Event);
            ScreenshotTests.Pump(() => shell.Entry.TogglePickerCommand.ExecuteAsync(TodoEntryPicker.Repeat));
            Check("todo-sheet-full-keyboard");
            failures.AddRange(AboveKeyboard(view, safe, keyboard).Select(offender => $"{device}/todo-sheet-full-keyboard: {offender}"));
            view.PreviewKeyboard = null;
            shell.CloseTodoSheetCommand.Execute(null);
            shell.IsEditorOpen = false;

            shell.OpenAttachSheetCommand.Execute(null);
            Check("attach-sheet");
            shell.CloseAttachSheetCommand.Execute(null);

            shell.DayFiles[0].ShowMenuCommand.Execute(null);
            Check("file-menu");
            shell.RequestDeleteFileCommand.Execute(null);
            Check("file-confirm");
            shell.CloseFileMenuCommand.Execute(null);

            ScreenshotTests.Pump(() => shell.DayFiles.First(row => row.Item.IsImage).OpenCommand.ExecuteAsync(null));
            Assert.IsTrue(shell.IsImageViewerOpen, $"{device}: the picture did not open in the viewer.");
            Check("viewer");
            shell.CloseImageViewerCommand.Execute(null);

            shell.GoToPageCommand.Execute(MobilePage.Lists);
            Check("lists");

            shell.GoToPageCommand.Execute(MobilePage.Search);
            Check("search");

            shell.GoToPageCommand.Execute(MobilePage.Settings);
            Settle(view);
            Assert.IsTrue(shell.IsReminderPermissionDenied, $"{device}: the settings page is not showing the denied reminder row.");
            Assert.IsTrue(shell.ShowPreciseRemindersRow, $"{device}: the settings page is not showing the precise-reminders row.");
            Check("settings");

            shell.OpenReminderTimeSheetCommand.Execute(null);
            Check("reminder-time");
            shell.CloseReminderTimeSheetCommand.Execute(null);

            shell.OpenAccountCommand.Execute(null);
            Check("account");
            shell.CloseAccountCommand.Execute(null);

            shell.GoToPageCommand.Execute(MobilePage.Day);
            shell.OpenMonthPickerCommand.Execute(null);
            Check("sheet");
            shell.CloseMonthPickerCommand.Execute(null);
        });

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The iPhone's plans page, which only exists with an account and a store: in each of the three
    /// states it has (selling, an App Store subscriber, a desktop subscriber), top and bottom.
    /// </summary>
    [TestMethod]
    [DataRow("iphone-16-pro", 402, 874, 0, 62, 0, 34)]
    [DataRow("iphone-se", 375, 667, 0, 20, 0, 0)]
    [DataRow("iphone-16-pro-landscape", 874, 402, 62, 0, 62, 21)]
    public void The_plans_page_keeps_clear_of_the_notch_and_the_bottom_strip(
        string device, double width, double height, double left, double top, double right, double bottom)
    {
        var safe = new Thickness(left, top, right, bottom);
        string directory = Path.Combine(OutputDirectory, device);
        Directory.CreateDirectory(directory);
        var failures = new List<string>();
        var store = new StoreTests.FakeStore();
        var server = new StoreTests.FakeServer();

        TestServices.WithInitialisedShell(width, height, TestServices.ShellSetup.WithAccount(services =>
            services.AddSingleton(sp => new MobileStoreViewModel(
                store,
                sp.GetRequiredService<Daynote.App.Account.AccountViewModel>(),
                _ => ValueTask.FromResult<string?>("4b1f6d2e-9a39-4c47-8a0e-5f7c1d2b3a40"),
                server.SubmitAsync,
                () => Task.CompletedTask))), (view, shell) =>
        {
            view.PreviewSafeArea = safe;
            Daynote.App.Account.AccountViewModel account = shell.Account!;
            account.SignedInEmail = "someone@example.com";
            shell.GoToPageCommand.Execute(MobilePage.Settings);
            shell.OpenAccountCommand.Execute(null);
            account.Entitlement = StoreTests.Trial;
            account.Billing = StoreTests.Selling();
            Check("account-store-row");

            foreach ((string name, Daynote.Core.Sync.BillingLinks billing) in new[]
            {
                ("store", StoreTests.Selling()),
                ("store-apple", StoreTests.Selling(Daynote.Core.Sync.BillingProvider.Apple, "cc.arachat.daynote.pro.annual")),
                ("store-desktop", StoreTests.Selling(Daynote.Core.Sync.BillingProvider.Paddle, canPurchase: false)),
            })
            {
                account.Billing = billing;
                shell.OpenStoreCommand.Execute(null);
                Check(name);
                ScrollStoreToEnd(view);
                Check($"{name}-end");
                shell.CloseStoreCommand.Execute(null);
            }

            void Check(string screen)
            {
                Settle(view);
                Save(view, Path.Combine(directory, $"{screen}.png"));
                failures.AddRange(Offenders(view, safe).Select(offender => $"{device}/{screen}: {offender}"));
            }
        });

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    private static void ScrollStoreToEnd(Control view)
    {
        Settle(view);
        view.GetVisualDescendants().OfType<Views.StorePage>().Single().GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
    }

    private static ScrollViewer DayScroller(Control view) =>
        view.GetVisualDescendants().OfType<Views.DayPage>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();

    private static void ScrollDayToEnd(Control view)
    {
        Settle(view);
        DayScroller(view).ScrollToEnd();
    }

    private static void ScrollDayToStart(Control view) => DayScroller(view).ScrollToHome();

    /// <summary>Visible buttons whose on-screen part crosses into an inset.</summary>
    private static IEnumerable<string> Offenders(Control view, Thickness safe)
    {
        if (TopLevel.GetTopLevel(view) is not { } root)
        {
            yield break;
        }

        Size size = root.ClientSize;
        var dock = view.FindControl<Grid>("Dock");

        // The tab bar as a whole, not only its buttons: a device clips whatever crosses into the
        // bottom strip, and the bar's round lower edge was cut flat on an iPhone while every button
        // above it still passed.
        if (dock is { IsEffectivelyVisible: true } && dock.TranslatePoint(default, root) is { } dockAt &&
            dockAt.Y + dock.Bounds.Height > size.Height - safe.Bottom + 0.5)
        {
            yield return $"the tab bar reaches {dockAt.Y + dock.Bounds.Height:0.#}, past the safe area's bottom {size.Height - safe.Bottom:0.#}";
        }

        foreach (Button button in view.GetVisualDescendants().OfType<Button>())
        {
            if (!button.IsEffectivelyVisible || button.Bounds.Width <= 0 || button.Bounds.Height <= 0 ||
                OnScreen(button, root) is not { } rect)
            {
                continue;
            }

            bool inDock = dock is not null && button.GetVisualAncestors().Contains(dock);
            double bottomLimit = size.Height - safe.Bottom;

            if (rect.Top < safe.Top - 0.5 || rect.Left < safe.Left - 0.5 ||
                rect.Right > size.Width - safe.Right + 0.5 || rect.Bottom > bottomLimit + 0.5)
            {
                string label = button.Name ?? (button.Content as string) ?? AutomationName(button) ?? button.GetType().Name;
                yield return $"'{label}' at {rect} outside the safe area {safe} of {size}";
            }
        }
    }

    /// <summary>
    /// The ways to a new to-do — the day's + 할 일, the 할 일 tab's +, the tablet panel's row — clear
    /// of the floating tab bar wherever they are on screen: one under the bar cannot be tapped.
    /// </summary>
    private static IEnumerable<string> AddButtonsClear(Control view)
    {
        if (TopLevel.GetTopLevel(view) is not { } root ||
            view.FindControl<Grid>("Dock") is not { IsEffectivelyVisible: true } dock ||
            dock.TranslatePoint(default, root) is not { } dockAt)
        {
            yield break;
        }

        var bar = new Rect(dockAt, dock.Bounds.Size);
        foreach (Button button in view.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Name is "AddTodoButton" or "AddListTodoButton" or "AddTodoRow" && button.IsEffectivelyVisible))
        {
            if (OnScreen(button, root) is { } rect && rect.Intersects(bar))
            {
                yield return $"'{button.Name}' at {rect} is under the tab bar at {bar}";
            }
        }
    }

    /// <summary>The to-do sheet's edge and its buttons, wherever they reach under a keyboard covering <paramref name="keyboard"/> points.</summary>
    private static IEnumerable<string> AboveKeyboard(Control view, Thickness safe, double keyboard)
    {
        if (TopLevel.GetTopLevel(view) is not { } root || view.FindControl<Border>("TodoSheet") is not { } sheet)
        {
            yield break;
        }

        double top = root.ClientSize.Height - safe.Bottom - keyboard;
        if (sheet.TranslatePoint(new Point(0, sheet.Bounds.Height), root) is { } edge && Math.Abs(edge.Y - top) > 0.5)
        {
            yield return $"the sheet ends at {edge.Y:0.#}, not on the keyboard's top edge {top:0.#}";
        }

        // 추가 is not merely above the keyboard but whole: a button scrolled half away is no button.
        if (view.FindControl<Button>("TodoAdd") is not { } add || OnScreen(add, root) is not { } shown ||
            shown.Height < add.Bounds.Height - 0.5 || shown.Bottom > top + 0.5)
        {
            yield return "추가 is not wholly on screen above the keyboard";
        }

        foreach (Button button in sheet.GetVisualDescendants().OfType<Button>())
        {
            if (button.IsEffectivelyVisible && OnScreen(button, root) is { } rect && rect.Bottom > top + 0.5)
            {
                yield return $"'{button.Name ?? AutomationName(button) ?? button.GetType().Name}' at {rect} under the keyboard from {top:0.#}";
            }
        }
    }

    /// <summary>The part of a control inside every scroll view it sits in, in window coordinates; null when none is.</summary>
    private static Rect? OnScreen(Visual control, Visual root)
    {
        if (control.TranslatePoint(default, root) is not { } origin)
        {
            return null;
        }

        var rect = new Rect(origin, control.Bounds.Size);
        foreach (ScrollViewer scroller in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (scroller.TranslatePoint(default, root) is not { } at)
            {
                continue;
            }

            rect = rect.Intersect(new Rect(at, scroller.Bounds.Size));
        }

        return rect.Width > 0.5 && rect.Height > 0.5 ? rect : null;
    }

    private static string? AutomationName(Control control) =>
        Avalonia.Automation.AutomationProperties.GetName(control) is { Length: > 0 } name ? name : null;

    private static void Settle(Control view)
    {
        for (int i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
        }
    }

    private static void Save(Control view, string path)
    {
        using WriteableBitmap? frame = (TopLevel.GetTopLevel(view) as Window)?.CaptureRenderedFrame();
        frame?.Save(path, new PngBitmapEncoderOptions());
    }
}
