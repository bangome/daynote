using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Account;

/// <summary>
/// What the account window says about the subscription.
/// </summary>
/// <remarks>
/// A billing screen is the one place where a wrong label is worse than a blank one: "다음 결제일" and
/// "이용 종료일" are the same date with opposite meanings, and a banner that appears in the wrong state
/// either nags or hides a payment failure. So each state is asserted rather than eyeballed.
/// </remarks>
[TestClass]
public sealed class AccountPresentationTests
{
    private FakeAccounts accounts = null!;

    [TestInitialize]
    public void Setup() => accounts = new FakeAccounts();

    [TestMethod]
    public void The_avatar_takes_its_letter_from_the_address()
    {
        accounts.Email = "jiwon@example.test";
        AccountViewModel account = SignedIn();

        Assert.AreEqual("J", account.AvatarInitial);
        Assert.AreEqual("jiwon@example.test", account.DisplayName);
    }

    [TestMethod]
    public void Signed_out_there_is_no_letter_to_show()
    {
        AccountViewModel account = Create();

        Assert.AreEqual("?", account.AvatarInitial);
        Assert.AreEqual(string.Empty, account.DisplayName);
    }

    [TestMethod]
    [DataRow(EntitlementState.Active, false)]
    [DataRow(EntitlementState.Trial, false)]
    [DataRow(EntitlementState.Grace, true)]
    [DataRow(EntitlementState.Expired, true)]
    public void The_plan_pill_is_filled_only_while_the_subscription_runs(
        EntitlementState state,
        bool somethingIsWrong)
    {
        AccountViewModel account = With(state, DateTimeOffset.UtcNow.AddDays(20));

        Assert.AreEqual(state == EntitlementState.Active, account.IsPlanPaid);
        Assert.AreEqual(state == EntitlementState.Grace, account.IsPlanAttention);

        // Grace and Expired are the two states with something wrong; the banner colour follows.
        Assert.AreEqual(somethingIsWrong, account.IsBannerUrgent);
    }

    [TestMethod]
    public void An_active_subscription_shows_no_banner_and_no_upgrade_card()
    {
        AccountViewModel account = With(EntitlementState.Active, DateTimeOffset.UtcNow.AddDays(20));

        Assert.IsFalse(account.HasBanner, "A working subscription has nothing to announce.");
        Assert.IsFalse(account.ShowUpgrade, "There is nothing to upgrade to.");
        Assert.IsTrue(account.ShowSubscription);
    }

    [TestMethod]
    public void A_trial_says_nothing_until_it_is_nearly_over()
    {
        AccountViewModel early = With(EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(12));
        Assert.IsFalse(early.HasBanner, "A countdown from day one would be an advertisement.");

        AccountViewModel late = With(EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(3).AddHours(1));
        Assert.IsTrue(late.HasBanner);
        StringAssert.Contains(late.BannerTitle, "3", "The banner states the days left.");
        Assert.IsFalse(late.IsBannerUrgent, "A trial ending is not a failure.");
    }

    [TestMethod]
    public void A_lapse_says_plainly_that_nothing_was_deleted()
    {
        AccountViewModel account = With(EntitlementState.Expired, until: null);

        Assert.IsTrue(account.HasBanner);
        Assert.IsTrue(account.IsBannerUrgent);
        Assert.AreEqual(AppStrings.BillingLapseNote, account.BannerBody);
        Assert.IsTrue(account.ShowUpgrade, "Resubscribing has to be one click from the notice.");
    }

    [TestMethod]
    public void The_date_row_is_labelled_by_what_the_date_means()
    {
        DateTimeOffset until = DateTimeOffset.UtcNow.AddDays(18);

        Assert.AreEqual(AppStrings.BillingRowRenews, With(EntitlementState.Active, until).SubscriptionDateLabel);

        // The same date, but the subscription is not renewing into it.
        Assert.AreEqual(AppStrings.BillingRowEnds, With(EntitlementState.Grace, until).SubscriptionDateLabel);
    }

    [TestMethod]
    public void Picking_an_interval_changes_the_price_and_the_button()
    {
        AccountViewModel account = With(EntitlementState.Expired, until: null);

        Assert.IsTrue(account.IsAnnualSelected, "Annual leads, as it does on the pricing page.");
        Assert.AreEqual(AppStrings.BillingPriceAnnual, account.PriceMain);
        StringAssert.Contains(account.CheckoutLabel, AppStrings.BillingPriceAnnual);

        account.SelectMonthlyCommand.Execute(null);

        Assert.IsTrue(account.IsMonthlySelected);
        Assert.AreEqual(AppStrings.BillingPriceMonthly, account.PriceMain);
        Assert.AreEqual(AppStrings.BillingPriceUnitMonthly, account.PriceUnit);
        StringAssert.Contains(account.CheckoutLabel, AppStrings.BillingPriceMonthly);
    }

    [TestMethod]
    public void Checkout_buys_the_interval_that_is_selected()
    {
        AccountViewModel account = With(EntitlementState.Expired, until: null);
        account.SelectMonthlyCommand.Execute(null);

        account.CheckoutSelectedCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.AreEqual(BillingPlan.Monthly, accounts.LastCheckoutPlan);
    }

    [TestMethod]
    public void A_failed_payment_sends_the_user_to_the_provider_rather_than_a_new_purchase()
    {
        // Buying a second subscription would not fix a declined card. The portal is where one lives.
        AccountViewModel account = With(EntitlementState.Grace, DateTimeOffset.UtcNow.AddDays(4));

        account.ResolveBannerCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.AreEqual(1, accounts.PortalSessionsMinted);
        Assert.IsNull(accounts.LastCheckoutPlan);
    }

    [TestMethod]
    public void Nothing_can_be_bought_when_no_plan_is_on_sale()
    {
        accounts.Billing = BillingLinks.None;
        AccountViewModel account = With(EntitlementState.Expired, until: null);

        Assert.IsFalse(account.ShowUpgrade, "A price nobody configured must not be advertised.");
    }

    [TestMethod]
    public void The_footer_names_the_build()
    {
        AccountViewModel account = SignedIn();

        StringAssert.Contains(account.AppVersionText, ".", "A version has to be recognisable as one.");
        Assert.IsFalse(
            account.AppVersionText.Contains('+', StringComparison.Ordinal),
            "The commit suffix on an informational version is noise in a window footer.");
    }

    private AccountViewModel Create()
    {
        string? opened = null;
        return new AccountViewModel(
            accounts.Service,
            accounts.Store,
            () => ValueTask.FromResult(SyncReport.For(SyncOutcome.Completed)),
            new AccountViewModelTests.FakeExporter(),
            url => opened = url,
            @"C:\conflicts");
    }

    private AccountViewModel SignedIn()
    {
        AccountViewModel account = Create();
        account.SignInCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return account;
    }

    /// <summary>Signs in against a fake server reporting the given billing state.</summary>
    private AccountViewModel With(EntitlementState state, DateTimeOffset? until)
    {
        accounts.Entitlement = new Entitlement(
            state,
            until,
            CanSync: state is EntitlementState.Active or EntitlementState.Trial or EntitlementState.Grace,
            HasSubscribed: state is not EntitlementState.Trial);
        return SignedIn();
    }
}
