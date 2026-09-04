using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Tests.Account;

/// <summary>
/// The subscription surface (docs/CLOUD_SYNC.md §14).
/// </summary>
/// <remarks>
/// What is asserted here is mostly about what the user is told. A trial has to announce its end
/// before it arrives (Store policy 10.8.4), a lapse must not read as data loss, and none of it may
/// interfere with the notes on this PC.
/// </remarks>
[TestClass]
public sealed class SubscriptionViewModelTests
{
    private FakeAccounts accounts = null!;
    private FakeSyncStore store = null!;
    private AccountViewModelTests.FakeExporter exporter = null!;
    private SyncReport nextReport = null!;
    private readonly List<string> opened = [];

    [TestInitialize]
    public void Setup()
    {
        store = new FakeSyncStore();
        accounts = new FakeAccounts(store);
        exporter = new AccountViewModelTests.FakeExporter();
        nextReport = SyncReport.For(SyncOutcome.Completed);
        opened.Clear();
    }

    private AccountViewModel Create() => new(
        accounts.Service,
        store,
        () => ValueTask.FromResult(nextReport),
        exporter,
        opened.Add,
        @"C:\conflicts");

    [TestMethod]
    public async Task A_trial_says_how_many_days_are_left()
    {
        accounts.Entitlement = new Entitlement(
            EntitlementState.Trial,
            DateTimeOffset.UtcNow.AddDays(9).AddHours(1),
            true,
            false);
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.EntitlementSummary, "9");
        Assert.IsFalse(vm.IsUnpaid);
        // Nine days out is not the moment to start nagging.
        Assert.IsFalse(vm.ShouldWarnAboutEntitlement);
    }

    [TestMethod]
    public async Task A_trial_about_to_end_is_pointed_out_before_it_ends()
    {
        accounts.Entitlement = new Entitlement(
            EntitlementState.Trial,
            DateTimeOffset.UtcNow.AddDays(2),
            true,
            false);
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.ShouldWarnAboutEntitlement);
        Assert.AreEqual(SyncStatusKind.Synced, vm.Status.Kind, "A trial still syncing is not a problem state.");
    }

    [TestMethod]
    public async Task A_lapse_stops_sync_and_says_nothing_was_deleted()
    {
        accounts.Entitlement = new Entitlement(EntitlementState.Expired, null, false, true);
        nextReport = SyncReport.For(SyncOutcome.SubscriptionRequired);
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsUnpaid);
        Assert.AreEqual(SyncStatusKind.Unpaid, vm.Status.Kind);
        // Still signed in: a lapse is not a sign-out, and the panel must keep offering the way back.
        Assert.IsTrue(vm.IsSignedIn);
        // The copy-kept promise is the message, not an error.
        Assert.IsNull(vm.ErrorMessage);
        Assert.AreEqual(AppStrings.BillingExpired, vm.EntitlementSummary);
    }

    [TestMethod]
    public async Task An_expired_trial_offers_to_subscribe_and_a_lapsed_account_to_renew()
    {
        accounts.Entitlement = new Entitlement(EntitlementState.Expired, null, false, false);
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

        Assert.AreEqual(AppStrings.BillingTrialOver, vm.EntitlementSummary);
        Assert.IsFalse(vm.Entitlement.HasSubscribed);

        accounts.Entitlement = new Entitlement(EntitlementState.Expired, null, false, true);
        await vm.RefreshBillingCommand.ExecuteAsync(null);

        Assert.AreEqual(AppStrings.BillingExpired, vm.EntitlementSummary);
        Assert.IsTrue(vm.Entitlement.HasSubscribed);
    }

    [TestMethod]
    public async Task A_failed_payment_keeps_syncing_through_the_retry_window()
    {
        // The extra hour is not decoration: the countdown floors, so "four days from now" measured a
        // few milliseconds later would be 3.99 days and read as three.
        accounts.Entitlement = new Entitlement(
            EntitlementState.Grace,
            DateTimeOffset.UtcNow.AddDays(4).AddHours(1),
            true,
            true);
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsUnpaid);
        Assert.IsTrue(vm.ShouldWarnAboutEntitlement);
        StringAssert.Contains(vm.EntitlementSummary, "4");
        Assert.AreEqual(SyncStatusKind.Synced, vm.Status.Kind);
    }

    [TestMethod]
    public async Task Checkout_and_management_open_in_the_browser()
    {
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.CanCheckout);
        Assert.IsTrue(vm.CanCheckoutAnnual);
        Assert.IsTrue(vm.CanCheckoutMonthly);
        Assert.IsTrue(vm.CanManageSubscription);

        await vm.CheckoutAnnualCommand.ExecuteAsync(null);
        await vm.ManageSubscriptionCommand.ExecuteAsync(null);

        // Hosted pages, not an in-app payment form: the provider owns the card fields.
        CollectionAssert.AreEqual(
            new[] { "https://pay.test/checkout?txn=one-shot", "https://pay.test/manage?token=single-use" },
            opened);
        // Both links were minted for this click rather than read from something stored: the
        // checkout carries this account's id, and the portal link expires.
        Assert.AreEqual(1, accounts.CheckoutSessionsMinted);
        Assert.AreEqual(BillingPlan.Annual, accounts.LastCheckoutPlan);
        Assert.AreEqual(1, accounts.PortalSessionsMinted);
    }

    [TestMethod]
    public async Task The_monthly_button_buys_the_monthly_plan()
    {
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

        await vm.CheckoutMonthlyCommand.ExecuteAsync(null);

        Assert.AreEqual(BillingPlan.Monthly, accounts.LastCheckoutPlan);
        Assert.AreEqual(1, accounts.CheckoutSessionsMinted);
    }

    [TestMethod]
    public async Task A_plan_the_server_does_not_sell_gets_no_button()
    {
        accounts.Billing = new BillingLinks(true, false, OffersMonthly: false, OffersAnnual: true);
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.CanCheckoutAnnual);
        Assert.IsFalse(vm.CanCheckoutMonthly);

        await vm.CheckoutMonthlyCommand.ExecuteAsync(null);
        Assert.AreEqual(0, accounts.CheckoutSessionsMinted);
    }

    [TestMethod]
    public async Task No_management_link_is_offered_before_there_is_anything_to_manage()
    {
        accounts.Billing = new BillingLinks(true, false);
        AccountViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.CanCheckout);
        Assert.IsFalse(vm.CanManageSubscription);

        await vm.ManageSubscriptionCommand.ExecuteAsync(null);
        Assert.AreEqual(0, opened.Count);
        Assert.AreEqual(0, accounts.PortalSessionsMinted);
    }

    [TestMethod]
    public async Task Signing_out_forgets_the_billing_state()
    {
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.AreEqual(EntitlementState.Unknown, vm.Entitlement.State);
        Assert.IsFalse(vm.CanCheckout);
        Assert.IsFalse(vm.CanManageSubscription);
    }

    [TestMethod]
    public async Task Being_unable_to_read_the_billing_state_is_not_an_error()
    {
        AccountViewModel vm = Create();
        await vm.SignInCommand.ExecuteAsync(null);

        // The next read fails the way an offline laptop does.
        accounts.NextFailure = new AccountException(AccountFailure.Offline, "developer-facing English");
        await vm.RefreshBillingCommand.ExecuteAsync(null);

        Assert.IsNull(vm.ErrorMessage);
        // The last known state is still on screen rather than being blanked.
        Assert.AreEqual(EntitlementState.Active, vm.Entitlement.State);
    }

    [TestMethod]
    public void The_unpaid_chip_asks_for_attention_and_has_a_label_in_both_languages()
    {
        var chip = new SyncStatusView(SyncStatusKind.Unpaid);
        Assert.IsTrue(chip.IsVisible);
        Assert.IsTrue(chip.NeedsAttention);

        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
            string korean = chip.Label;
            LocalizationService.Instance.SetLanguage(AppLanguage.English);

            Assert.AreNotEqual(korean, chip.Label);
            Assert.IsFalse(string.IsNullOrWhiteSpace(korean));
            Assert.AreEqual("Subscription needed", chip.Label);
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }
}
