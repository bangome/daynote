using Daynote.App.Account;
using Daynote.App.Settings;
using Daynote.App.Tests.Account;
using Daynote.App.Tests.Lifecycle;
using Daynote.Core.Startup;
using Daynote.Core.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The settings dialog's account page shows the plan and what is owed on it.
/// </summary>
/// <remarks>
/// The plan, the trial's remaining days, the prices and the management link all come from the
/// billing endpoint. Nothing on the page asked for them, so it drew a signed-in account with no
/// plan and no subscription — and read as a build where subscriptions had been switched off. The
/// account card had always refreshed on open; the page had no equivalent moment until now.
/// </remarks>
[TestClass]
public sealed class DeskAccountPageTests
{
    private FakeAccounts accounts = null!;

    [TestInitialize]
    public void Setup() => accounts = new FakeAccounts { Email = "jiwon@example.test" };

    [TestMethod]
    public async Task Opening_the_account_page_asks_what_the_account_is_entitled_to()
    {
        // A trial that has never been paid for: the plan is what the page has to offer, and the
        // shell knows none of it until the server is asked.
        accounts.Entitlement = new Entitlement(EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(4), true, false);
        AccountViewModel account = Account();
        await account.SignInCommand.ExecuteAsync(null);

        SettingsViewModel settings = Settings(account);
        await settings.LoadAsync();

        settings.Section = SettingsSection.Account;
        await account.RefreshBillingCommand.ExecuteAsync(null);

        Assert.IsTrue(account.ShowUpgrade, "The page offers no plan, so there is nothing to subscribe to on it.");
        Assert.IsTrue(account.CanCheckoutMonthly || account.CanCheckoutAnnual, "Neither interval can be bought.");
        Assert.AreNotEqual(string.Empty, account.PriceMain, "The plan has no price on it.");
    }

    [TestMethod]
    public async Task A_paid_account_shows_what_it_is_paying_for()
    {
        accounts.Entitlement = new Entitlement(EntitlementState.Active, DateTimeOffset.UtcNow.AddDays(30), true, true);
        AccountViewModel account = Account();
        await account.SignInCommand.ExecuteAsync(null);
        await account.RefreshBillingCommand.ExecuteAsync(null);

        Assert.IsTrue(account.ShowSubscription, "A paid account has no subscription card to show.");
        Assert.IsTrue(account.HasSubscriptionDate, "Nothing says when the next payment falls.");
        Assert.IsFalse(account.ShowUpgrade, "A paid account is being sold the plan it already has.");
    }

    [TestMethod]
    public void The_page_does_not_ask_on_behalf_of_an_account_that_is_not_signed_in()
    {
        SettingsViewModel settings = Settings(account: null);

        // No account, no request, and no crash on the way past.
        settings.Section = SettingsSection.Account;
        settings.RefreshAccountPage();

        Assert.IsTrue(settings.IsAccountSection);
    }

    private AccountViewModel Account() => new(
        accounts.Service,
        accounts.Store,
        () => ValueTask.FromResult(SyncReport.For(SyncOutcome.Completed)),
        new AccountViewModelTests.FakeExporter(),
        _ => { },
        @"C:\conflicts");

    private static SettingsViewModel Settings(AccountViewModel? account)
    {
        var store = new InMemorySettingsStore();
        return new SettingsViewModel(
            new FakeStartupTaskService(StartupTaskState.Disabled),
            new RecordingHotkeyService(),
            store,
            new FakeBackupService(),
            new FakeBackupFilePicker(),
            new Daynote.App.Input.ConfigurableShortcuts(store),
            () => Task.FromResult(true),
            () => { },
            () => { },
            @"C:\Users\Test\AppData\Local\Daynote")
        {
            Account = account,
        };
    }
}
