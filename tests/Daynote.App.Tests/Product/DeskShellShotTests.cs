using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Daynote.App.Settings;
using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Application = System.Windows.Application;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Renders the product window to a PNG, light and dark, so the design can be looked at.
/// </summary>
/// <remarks>
/// Off by default: it writes files and is here to be read by a person, not to assert. Run it with
/// <c>DAYNOTE_SHELL_SHOTS=1</c>, and <c>DAYNOTE_SHELL_SHOTS_DIR</c> to say where.
/// </remarks>
[TestClass]
public sealed class DeskShellShotTests
{
    [STATestMethod]
    [DataRow(false, false, "light", DisplayName = "light")]
    [DataRow(true, false, "dark", DisplayName = "dark")]
    [DataRow(false, true, "timeline-light", DisplayName = "timeline")]
    public void Shell_renders(bool dark, bool timeline, string name)
    {
        if (Environment.GetEnvironmentVariable("DAYNOTE_SHELL_SHOTS") != "1")
        {
            Assert.Inconclusive("Shell shots are rendered on request: set DAYNOTE_SHELL_SHOTS=1.");
        }

        string directory = Environment.GetEnvironmentVariable("DAYNOTE_SHELL_SHOTS_DIR")
            ?? Path.Combine(Path.GetTempPath(), "daynote-shell-shots");
        Directory.CreateDirectory(directory);

        Application application = ProductWindowCompositionTests.EnsureApplicationResources(dark);
        WorkspaceTestContext context = WorkspaceTestContext.Create();
        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        try
        {
            Run(harness.Shell.InitializeAsync());
            harness.Shell.IsDark = dark;

            // A note with a link in it, so the shot shows the body's line height and the colour
            // the highlighter gives a mark in whichever theme is up.
            Run(harness.Shell.NewNoteCommand.ExecuteAsync(null));
            if (timeline)
            {
                Run(harness.Shell.ToggleTimelineCommand.ExecuteAsync(null));

                // The rows are folded into days on the dispatcher; without letting that run the
                // shot is of an empty timeline.
                for (int i = 0; i < 8; i += 1)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                        () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                }
            }

            harness.Shell.Notes.EditorText =
                "회의 준비\n- [ ] 자료 정리\n- [ ] https://example.com 확인\n\n지난 주 논의 내용을 정리한다.";

            var window = new ProductWindow(harness.Shell);
            var content = (System.Windows.Controls.Grid)window.Content;

            // RenderTargetBitmap draws the content, not the window behind it, and the design's page
            // colour lives on the window. Without this the page comes out transparent and every
            // unbacked surface reads as washed-out in the file.
            content.SetResourceReference(
                System.Windows.Controls.Panel.BackgroundProperty, "Daynote.Product.Brush.Bg1");
            content.Measure(new Size(1240, 800));
            content.Arrange(new Rect(0, 0, 1240, 800));
            content.UpdateLayout();

            var target = new RenderTargetBitmap(1240, 800, 96, 96, PixelFormats.Pbgra32);
            target.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            string path = Path.Combine(directory, $"shell-{name}.png");
            using (FileStream file = File.Create(path))
            {
                encoder.Save(file);
            }

            Assert.IsTrue(new FileInfo(path).Length > 0, path);
        }
        finally
        {
            harness.DisposeAsync().AsTask().GetAwaiter().GetResult();
            context.DisposeAsync().AsTask().GetAwaiter().GetResult();
            application.Resources.MergedDictionaries.Clear();
        }
    }

    [STATestMethod]
    [DataRow(false, SettingsSection.General, "general-light", DisplayName = "settings, general")]
    [DataRow(false, SettingsSection.Shortcuts, "shortcuts-light", DisplayName = "settings, shortcuts")]
    [DataRow(true, SettingsSection.Data, "data-dark", DisplayName = "settings, data")]
    [DataRow(false, SettingsSection.Account, "account-light", DisplayName = "settings, account")]
    public void Settings_renders(bool dark, SettingsSection section, string name)
    {
        if (Environment.GetEnvironmentVariable("DAYNOTE_SHELL_SHOTS") != "1")
        {
            Assert.Inconclusive("Shell shots are rendered on request: set DAYNOTE_SHELL_SHOTS=1.");
        }

        string directory = ShotDirectory();
        Application application = ProductWindowCompositionTests.EnsureApplicationResources(dark);
        var store = new Lifecycle.InMemorySettingsStore();
        var shortcuts = new Daynote.App.Input.ConfigurableShortcuts(store);

        // The account page is the one that needs a signed-in account to show anything.
        // On trial and never paid for, which is the state the plan has to be offered in.
        var accounts = new Tests.Account.FakeAccounts
        {
            Email = "jiwon@example.test",
            Entitlement = new Daynote.Core.Sync.Entitlement(
                Daynote.Core.Sync.EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(4), true, false),

            // The catalogue the worker actually sells, both tiers at both intervals. The default
            // fake falls back to a one-tier legacy list, and the shot then shows Premium priced at
            // a dash — a picture of the screen nobody is served.
            Billing = new Daynote.Core.Sync.BillingLinks(
                CanCheckout: true,
                CanManage: true,
                Offers:
                [
                    Offer(Daynote.Core.Sync.BillingTier.Pro, Daynote.Core.Sync.BillingPlan.Monthly, 2_900),
                    Offer(Daynote.Core.Sync.BillingTier.Pro, Daynote.Core.Sync.BillingPlan.Annual, 24_000),
                    Offer(Daynote.Core.Sync.BillingTier.Premium, Daynote.Core.Sync.BillingPlan.Monthly, 5_900),
                    Offer(Daynote.Core.Sync.BillingTier.Premium, Daynote.Core.Sync.BillingPlan.Annual, 48_000),
                ]),
        };
        var account = new Daynote.App.Account.AccountViewModel(
            accounts.Service,
            accounts.Store,
            () => ValueTask.FromResult(Daynote.Core.Sync.SyncReport.For(Daynote.Core.Sync.SyncOutcome.Completed)),
            new Tests.Account.AccountViewModelTests.FakeExporter(),
            _ => { },
            @"C:\conflicts");
        Run(account.SignInCommand.ExecuteAsync(null));
        Run(account.RefreshBillingCommand.ExecuteAsync(null));

        var settings = new SettingsViewModel(
            new Lifecycle.FakeStartupTaskService(Daynote.Core.Startup.StartupTaskState.Disabled),
            new Lifecycle.RecordingHotkeyService(),
            store,
            new Lifecycle.FakeBackupService(),
            new Lifecycle.FakeBackupFilePicker(),
            shortcuts,
            () => Task.FromResult(true),
            () => { },
            () => { },
            @"C:\\Users\\Test\\AppData\\Local\\Daynote")
        {
            Account = account,
        };
        Run(settings.LoadAsync());
        settings.Section = section;

        Render(new SettingsView { DataContext = settings }, 900, 660, Path.Combine(directory, $"settings-{name}.png"));

        // The account view on its own and tall enough to reach the plan table, which the dialog
        // only shows a scrollbar's worth of.
        if (section == SettingsSection.Account)
        {
            Render(
                new Daynote.App.Account.AccountDetailsView { DataContext = account },
                620,
                1500,
                Path.Combine(directory, "account-detail.png"));

            // And the other half: what someone who has not signed in is shown.
            var signedOut = new Daynote.App.Account.AccountViewModel(
                new Tests.Account.FakeAccounts().Service,
                accounts.Store,
                () => ValueTask.FromResult(Daynote.Core.Sync.SyncReport.For(Daynote.Core.Sync.SyncOutcome.Completed)),
                new Tests.Account.AccountViewModelTests.FakeExporter(),
                _ => { },
                @"C:\conflicts");
            Render(
                new Daynote.App.Account.AccountSignInView { DataContext = signedOut },
                620,
                900,
                Path.Combine(directory, "account-signin.png"));
        }

        application.Resources.MergedDictionaries.Clear();
    }

    private static Daynote.Core.Sync.BillingOffer Offer(
        Daynote.Core.Sync.BillingTier tier,
        Daynote.Core.Sync.BillingPlan plan,
        long won) => new(tier, plan, [new Daynote.Core.Sync.Money("KRW", won)]);

    /// <summary>Waits for the shell while the dispatcher keeps running; see DeskTabStripTests.</summary>
    private static void Run(Task task)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        _ = task.ContinueWith(
            _ => frame.Continue = false,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static string ShotDirectory()
    {
        string directory = Environment.GetEnvironmentVariable("DAYNOTE_SHELL_SHOTS_DIR")
            ?? Path.Combine(Path.GetTempPath(), "daynote-shell-shots");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Lays the element out at a fixed size and writes what it draws.</summary>
    private static void Render(FrameworkElement element, int width, int height, string path)
    {
        var host = new System.Windows.Controls.Grid { Width = width, Height = height, Children = { element } };
        host.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Daynote.Product.Brush.Bg1");

        // The window supplies the page ink in the app, and everything inside inherits it. A view
        // rendered on its own has no such ancestor and would fall back to black, which on the dark
        // page reads as a contrast bug that is not there.
        host.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Daynote.Product.Brush.Text");
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
