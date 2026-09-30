using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.App.Tests.Account;
using Daynote.Core.Sync;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The subscription surfaces in each state Daynote Desktop B v2 draws, written to
/// <c>frames/subscription-*.png</c> at the prototype's review size so they can be laid beside it.
/// </summary>
/// <remarks>
/// As with <see cref="DesignFrameTests"/>, the assertions prove the evidence is real and that each
/// frame shows the state it claims: the settings 계정 page on a trial, on Pro and on Premium, the
/// checkout dialog and its success state, and the popover's upgrade row.
/// </remarks>
[TestClass]
public sealed class SubscriptionFrameTests
{
    private const int Width = 1600;
    private const int Height = 1100;

    private static readonly string FramesDirectory = Path.Combine(AppContext.BaseDirectory, "frames");

    private static readonly BillingOffer[] Offers =
    [
        new(BillingTier.Pro, BillingPlan.Monthly, [new Money("KRW", 2900), new Money("USD", 249)]),
        new(BillingTier.Pro, BillingPlan.Annual, [new Money("KRW", 24000), new Money("USD", 1999)]),
        new(BillingTier.Premium, BillingPlan.Monthly, [new Money("KRW", 5900), new Money("USD", 499)]),
        new(BillingTier.Premium, BillingPlan.Annual, [new Money("KRW", 49000), new Money("USD", 3999)]),
    ];

    [TestMethod]
    public void The_subscription_states_render()
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            TestServices.WithInitialisedShell(Width, Height, (window, shell) =>
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
                var accounts = new FakeAccounts { Email = "jiwon.park@example.com" };

                // Trial, three days left, everything on sale.
                AccountViewModel trial = SignIn(shell, accounts, new BillingLinks(true, false, Offers: Offers), new Entitlement(
                    EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(3).AddHours(1), true, false,
                    BillingTier.Pro, null, 2L << 30, 0));
                OpenAccountSettings(shell);
                Assert.IsTrue(trial.ShowTrialUpgrade && trial.ShowPlanTable);
                Shoot(window, "subscription-trial");
                ScrollToPlanTable(window);
                Shoot(window, "subscription-plan-table");

                trial.OpenCheckoutProCommand.Execute(null);
                Assert.IsTrue(trial.IsCheckoutForm);
                Shoot(window, "subscription-checkout-form");
                trial.CloseCheckoutCommand.Execute(null);
                shell.CloseSettingsCommand.Execute(null);

                AccountBar bar = window.GetVisualDescendants().OfType<AccountBar>().Single();
                var toggle = (Button)bar.GetLogicalChildren().Single();
                toggle.Flyout!.ShowAt(toggle);
                Pump(window);
                Shoot(window, "subscription-popover");
                toggle.Flyout.Hide();

                // Pro, annual, with a gigabyte used; Premium on sale and changeable.
                var paid = new BillingLinks(true, true, Offers: Offers, CanChange: true);
                AccountViewModel pro = SignIn(shell, accounts, paid, new Entitlement(
                    EntitlementState.Active, DateTimeOffset.UtcNow.AddYears(1), true, true,
                    BillingTier.Pro, BillingPlan.Annual, 2L << 30, 1_288_490_189));
                OpenAccountSettings(shell);
                Assert.IsTrue(pro.ShowSubscribedCard && pro.CanUpgradeToPremium);
                Shoot(window, "subscription-pro");
                ScrollToPlanTable(window);
                Shoot(window, "subscription-plan-table-pro");

                // The success state, as it shows once the server reports the subscription.
                pro.OpenCheckoutProCommand.Execute(null);
                pro.CheckoutStage = CheckoutStage.Done;
                Assert.IsTrue(pro.IsCheckoutDone);
                Shoot(window, "subscription-checkout-done");
                pro.CloseCheckoutCommand.Execute(null);

                AccountViewModel premium = SignIn(shell, accounts, paid, new Entitlement(
                    EntitlementState.Active, DateTimeOffset.UtcNow.AddYears(1), true, true,
                    BillingTier.Premium, BillingPlan.Annual, 200L << 30, 12L << 30));
                OpenAccountSettings(shell);
                Assert.AreEqual("Premium", premium.PlanBadge);
                Shoot(window, "subscription-premium");

                // Nothing on sale (production today): the page is identity alone.
                AccountViewModel unsold = SignIn(shell, accounts, BillingLinks.None, new Entitlement(
                    EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(3).AddHours(1), true, false));
                OpenAccountSettings(shell);
                Assert.IsFalse(unsold.ShowPlanTable || unsold.ShowTrialUpgrade);
                Shoot(window, "subscription-not-on-sale");
                shell.CloseSettingsCommand.Execute(null);
            });
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    private static AccountViewModel SignIn(DesktopShellViewModel shell, FakeAccounts accounts, BillingLinks billing, Entitlement entitlement)
    {
        accounts.Billing = billing;
        accounts.Entitlement = entitlement;
        var account = new AccountViewModel(
            accounts.Service,
            accounts.Store,
            () => ValueTask.FromResult(SyncReport.For(SyncOutcome.Completed)),
            new NoExport(),
            _ => { },
            Path.Combine(Path.GetTempPath(), "daynote-tests-conflicts"));
        Wait(account.SignInCommand.ExecuteAsync(null));
        shell.Account = account;
        Pump();
        return account;
    }

    /// <summary>What "요금제 비교" does: the settings page scrolled down to the plan table.</summary>
    private static void ScrollToPlanTable(Window window)
    {
        ScrollViewer scroller = window.GetVisualDescendants().OfType<SettingsPanel>().Single()
            .GetVisualDescendants().OfType<ScrollViewer>().First();
        Control table = scroller.GetVisualDescendants().OfType<Control>().First(control => control.Name == "PlanTable");
        Assert.IsTrue(table.IsEffectivelyVisible, "The plan table is not on the page.");
        scroller.Offset = new Avalonia.Vector(0, scroller.Extent.Height);
        Pump(window);
    }

    private static void OpenAccountSettings(DesktopShellViewModel shell)
    {
        shell.SettingsViewModel!.Section = SettingsSection.Account;
        shell.OpenSettingsCommand.Execute(null);
        Pump();
    }

    private static void Shoot(Window window, string name)
    {
        Pump(window);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap, $"{name}: nothing rendered.");
        Assert.AreEqual(Width, bitmap.PixelSize.Width, $"{name}: wrong width.");

        Directory.CreateDirectory(FramesDirectory);
        bitmap.Save(Path.Combine(FramesDirectory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static void Wait(Task work)
    {
        for (int i = 0; i < 400 && !work.IsCompleted; i += 1)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.IsTrue(work.IsCompleted, "A step never completed.");
        work.GetAwaiter().GetResult();
    }

    private static void Pump(Window? window = null)
    {
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        window?.UpdateLayout();
    }

    private sealed class NoExport : IRecoveryKeyExporter
    {
        public Task<bool> TryCopyToClipboardAsync(string recoveryKey) => Task.FromResult(false);

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(false);
    }
}
