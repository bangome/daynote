using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.App.Tests.Account;
using Daynote.Core.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The subscription surfaces of the settings page (Daynote Desktop B v2): the trial card, the
/// subscribed card, the three-column plan table and the checkout dialog — as view-model state, over
/// the shared account fakes.
/// </summary>
/// <remarks>
/// The rule most of these guard: every price on screen is the server's, and the dialog's success
/// state follows the server's billing state, never the click that opened the browser.
/// </remarks>
[TestClass]
public sealed class SubscriptionTiersTests
{
    private static readonly BillingOffer[] Offers =
    [
        new(BillingTier.Pro, BillingPlan.Monthly, [new Money("KRW", 2900), new Money("USD", 249)]),
        new(BillingTier.Pro, BillingPlan.Annual, [new Money("KRW", 24000), new Money("USD", 1999)]),
        new(BillingTier.Premium, BillingPlan.Monthly, [new Money("KRW", 5900), new Money("USD", 499)]),
        new(BillingTier.Premium, BillingPlan.Annual, [new Money("KRW", 48000), new Money("USD", 3999)]),
    ];

    private FakeAccounts accounts = null!;
    private FakeSyncStore store = null!;
    private readonly List<string> opened = [];
    private AppLanguage previousLanguage;

    [TestInitialize]
    public void Setup()
    {
        previousLanguage = LocalizationService.Instance.Language;
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        store = new FakeSyncStore();
        accounts = new FakeAccounts(store)
        {
            Billing = new BillingLinks(true, false, Offers: Offers),
        };
        opened.Clear();
    }

    [TestCleanup]
    public void Cleanup() => LocalizationService.Instance.SetLanguage(previousLanguage);

    /// <summary>
    /// Runs an async test body on the thread pool and blocks the test thread until it ends.
    /// </summary>
    /// <remarks>
    /// The headless Avalonia session belongs to the thread that started it, and the UI tests reach it
    /// through a blocking <c>Dispatcher.UIThread.Invoke</c>, which only works from that thread. An async
    /// test whose awaits really yield (the checkout poll's delays) would hand the rest of the run to a
    /// pool thread, and the next UI test would wait on the dispatcher forever. Blocking here keeps the
    /// test thread where it was.
    /// </remarks>
    private static void Run(Func<Task> body) => Task.Run(body).GetAwaiter().GetResult();

    private async Task<AccountViewModel> SignedIn(Entitlement entitlement, bool isPhone = false)
    {
        accounts.Entitlement = entitlement;
        var vm = new AccountViewModel(
            accounts.Service,
            store,
            () => ValueTask.FromResult(SyncReport.For(SyncOutcome.Completed)),
            new NoExport(),
            opened.Add,
            "/tmp/conflicts")
        {
            IsPhone = isPhone,
        };
        await vm.SignInCommand.ExecuteAsync(null);
        return vm;
    }

    private static Entitlement Trial(int days = 3) => new(
        EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(days).AddHours(1), true, false,
        BillingTier.Pro, null, 2L << 30, 0);

    private static Entitlement Paying(BillingTier tier, BillingPlan plan = BillingPlan.Annual, long used = 0) => new(
        EntitlementState.Active, DateTimeOffset.UtcNow.AddYears(1), true, true,
        tier, plan, tier == BillingTier.Premium ? 200L << 30 : 2L << 30, used);

    [TestMethod]
    public void A_trial_offers_Pro_at_the_servers_price_and_counts_down() => Run(async () =>
    {
        AccountViewModel vm = await SignedIn(Trial());

        Assert.IsTrue(vm.ShowTrialUpgrade);
        Assert.IsFalse(vm.ShowSubscribedCard);
        Assert.AreEqual("체험 중", vm.PlanBadge);
        Assert.AreEqual("체험 기간이 3일 남았습니다", vm.TrialUpgradeTitle);
        Assert.AreEqual("Pro 구독하기 · ₩24,000", vm.ProCtaLabel);
        Assert.AreEqual("31% 할인", vm.AnnualSavingText);
        Assert.AreEqual("0MB / 2GB", vm.StorageText);
    });

    [TestMethod]
    public void The_plan_table_has_three_columns_with_prices_and_recommends_Pro() => Run(async () =>
    {
        AccountViewModel vm = await SignedIn(Trial());

        IReadOnlyList<PlanColumn> columns = vm.PlanColumns;
        CollectionAssert.AreEqual(new[] { "무료", "Pro", "Premium" }, columns.Select(c => c.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "₩0", "₩24,000", "₩48,000" }, columns.Select(c => c.Price).ToArray());
        Assert.AreEqual("연간 · 월 ₩2,000꼴", columns[1].Per);
        Assert.AreEqual("연간 · 월 ₩4,000꼴", columns[2].Per);
        Assert.IsTrue(columns[1].IsHighlighted);
        Assert.AreEqual("체험 종료 후", columns[0].Note);
        Assert.IsTrue(columns[1].CanBuy && columns[2].CanBuy);
        Assert.AreEqual("선택", columns[2].ButtonText);

        vm.SelectMonthlyCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "₩0", "₩2,900", "₩5,900" }, vm.PlanColumns.Select(c => c.Price).ToArray());
        Assert.AreEqual("매월", vm.PlanColumns[2].Per);
    });

    [TestMethod]
    public void The_rows_are_honest_about_what_each_plan_has() => Run(async () =>
    {
        PlanComparisonRow storage = PlanComparison.Rows[^1];
        PlanComparisonRow files = PlanComparison.Rows[^2];

        Assert.IsTrue(storage.Free.IsDash);
        Assert.AreEqual("2GB", storage.Pro.Text);
        Assert.AreEqual("무제한", storage.Premium.Text);
        Assert.IsTrue(files.Free.IsDash && files.Pro.IsCheck && files.Premium.IsCheck);
        Assert.IsTrue(PlanComparison.Rows.Take(4).All(row => row.Free.IsCheck && row.Pro.IsCheck && row.Premium.IsCheck));
        Assert.AreEqual("무제한은 공정 사용 범위 안에서 제공됩니다", AppStrings.PlanFairUseNote);
        await Task.CompletedTask;
    });

    [TestMethod]
    public void English_shows_dollar_prices_from_the_same_list() => Run(async () =>
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        AccountViewModel vm = await SignedIn(Trial());

        CollectionAssert.AreEqual(new[] { "$0.00", "$19.99", "$39.99" }, vm.PlanColumns.Select(c => c.Price).ToArray());
        Assert.AreEqual("Subscribe to Pro · $19.99", vm.ProCtaLabel);
    });

    [TestMethod]
    public void A_Pro_subscriber_sees_the_subscribed_card_and_can_move_to_Premium() => Run(async () =>
    {
        accounts.Billing = new BillingLinks(true, true, Offers: Offers, CanChange: true);
        AccountViewModel vm = await SignedIn(Paying(BillingTier.Pro, used: 1_288_490_189));

        Assert.AreEqual("Pro", vm.PlanBadge);
        Assert.IsTrue(vm.ShowSubscribedCard);
        Assert.IsFalse(vm.ShowTrialUpgrade);
        Assert.AreEqual("Pro 구독 중", vm.SubscribedTitle);
        StringAssert.EndsWith(vm.SubscribedDetail, "₩24,000 / 년");
        Assert.IsTrue(vm.CanUpgradeToPremium);
        Assert.AreEqual("1.2GB / 2GB", vm.StorageText);

        PlanColumn pro = vm.PlanColumns[1];
        PlanColumn premium = vm.PlanColumns[2];
        Assert.IsTrue(pro.IsCurrent && pro.IsHighlighted && !pro.CanBuy);
        Assert.AreEqual("현재 플랜", pro.Note);
        Assert.IsTrue(premium.CanBuy);
        Assert.AreEqual("변경하기", premium.ButtonText);
        Assert.AreEqual(string.Empty, vm.PlanColumns[0].Note);
    });

    [TestMethod]
    public void Premium_shows_usage_without_a_ceiling_and_offers_no_downgrade_from_the_table() => Run(async () =>
    {
        accounts.Billing = new BillingLinks(true, true, Offers: Offers, CanChange: true);
        AccountViewModel vm = await SignedIn(Paying(BillingTier.Premium, used: 5L << 30));

        Assert.AreEqual("Premium", vm.PlanBadge);
        Assert.AreEqual("5GB 사용", vm.StorageText);
        Assert.IsFalse(vm.CanUpgradeToPremium);
        Assert.IsFalse(vm.PlanColumns[1].CanBuy);
        Assert.IsTrue(vm.PlanColumns[2].IsCurrent);
    });

    [TestMethod]
    public void Checkout_opens_the_browser_for_the_chosen_tier_and_waits_for_the_server() => Run(async () =>
    {
        AccountViewModel vm = await SignedIn(Trial());

        vm.OpenCheckoutPremiumCommand.Execute(null);
        Assert.IsTrue(vm.IsCheckoutForm);
        Assert.AreEqual("구독하기", vm.CheckoutTitle);
        Assert.AreEqual("Premium · 연간", vm.CheckoutPlanLabel);
        Assert.AreEqual("₩5,900", vm.MonthlyPrice);
        Assert.AreEqual("₩48,000", vm.AnnualPrice);
        Assert.AreEqual("32% 할인", vm.CheckoutSavingText);
        Assert.AreEqual("₩48,000 결제하기", vm.CheckoutConfirmLabel);

        vm.SelectCheckoutProCommand.Execute(null);
        vm.SelectMonthlyCommand.Execute(null);
        await vm.ConfirmCheckoutCommand.ExecuteAsync(null);

        Assert.AreEqual(BillingTier.Pro, accounts.LastCheckoutTier);
        Assert.AreEqual(BillingPlan.Monthly, accounts.LastCheckoutPlan);
        CollectionAssert.AreEqual(new[] { accounts.CheckoutUrl }, opened);
        // Opening the browser proves nothing about a payment: the dialog waits.
        Assert.IsTrue(vm.IsCheckoutWaiting);

        await vm.RefreshBillingCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsCheckoutWaiting, "Still a trial on the server, so still waiting.");

        accounts.Entitlement = Paying(BillingTier.Pro, BillingPlan.Monthly);
        vm.NotifyActivated();
        await Task.Delay(50);

        Assert.IsTrue(vm.IsCheckoutDone);
        Assert.AreEqual("Pro 구독이 시작되었습니다", vm.CheckoutDoneTitle);
        vm.CloseCheckoutCommand.Execute(null);
        Assert.IsFalse(vm.IsCheckoutOpen);
    });

    [TestMethod]
    public void A_subscriber_changes_plan_in_place_instead_of_buying_twice() => Run(async () =>
    {
        accounts.Billing = new BillingLinks(true, true, Offers: Offers, CanChange: true);
        AccountViewModel vm = await SignedIn(Paying(BillingTier.Pro));

        vm.OpenCheckoutCommand.Execute(null);

        Assert.IsTrue(vm.IsCheckoutChange);
        Assert.IsTrue(vm.IsCheckoutPremium, "From Pro, the dialog opens on Premium.");
        Assert.AreEqual("플랜 변경", vm.CheckoutTitle);
        Assert.AreEqual("플랜 변경하기", vm.CheckoutConfirmLabel);

        await vm.ConfirmCheckoutCommand.ExecuteAsync(null);

        Assert.AreEqual(1, accounts.PlanChanges);
        Assert.AreEqual(0, accounts.CheckoutSessionsMinted);
        Assert.AreEqual(0, opened.Count);
        Assert.IsTrue(vm.IsCheckoutDone);
        Assert.AreEqual("Premium 플랜으로 변경되었습니다", vm.CheckoutDoneTitle);
        Assert.AreEqual("Premium", vm.PlanBadge);
    });

    [TestMethod]
    public void Cancelling_goes_to_the_providers_portal() => Run(async () =>
    {
        accounts.Billing = new BillingLinks(true, true, Offers: Offers, CanChange: true);
        AccountViewModel vm = await SignedIn(Paying(BillingTier.Pro));

        await vm.ManageSubscriptionCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { accounts.PortalUrl }, opened);
    });

    [TestMethod]
    public void Nothing_is_sold_when_the_server_offers_no_plans() => Run(async () =>
    {
        accounts.Billing = BillingLinks.None;
        AccountViewModel vm = await SignedIn(Trial());

        Assert.IsFalse(vm.OffersSubscription);
        Assert.IsFalse(vm.ShowPlanTable);
        Assert.IsFalse(vm.ShowTrialUpgrade);
        vm.OpenCheckoutCommand.Execute(null);
        Assert.IsFalse(vm.IsCheckoutOpen);
    });

    [TestMethod]
    public void A_phone_shows_the_plan_and_storage_but_never_a_checkout() => Run(async () =>
    {
        AccountViewModel vm = await SignedIn(Paying(BillingTier.Premium, used: 3L << 30), isPhone: true);

        Assert.AreEqual("Premium", vm.PlanBadge);
        Assert.IsTrue(vm.HasStorage);
        Assert.AreEqual("3GB 사용", vm.StorageText);
        Assert.IsFalse(vm.ShowPlanTable);
        Assert.IsFalse(vm.ShowSubscribedCard);
        vm.OpenCheckoutProCommand.Execute(null);
        Assert.IsFalse(vm.IsCheckoutOpen);
    });

    [TestMethod]
    public void An_older_server_still_sells_Pro_at_the_catalog_price_and_hides_Premium() => Run(async () =>
    {
        accounts.Billing = new BillingLinks(true, false);
        AccountViewModel vm = await SignedIn(new Entitlement(
            EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(3), true, false));

        Assert.AreEqual("₩24,000", vm.PlanColumns[1].Price);
        Assert.AreEqual("—", vm.PlanColumns[2].Price);
        Assert.IsFalse(vm.PlanColumns[2].CanBuy);
        Assert.IsFalse(vm.CanChoosePremium);
        Assert.IsFalse(vm.HasStorage);
    });

    [TestMethod]
    public void Reopening_the_checkout_opens_the_same_page_rather_than_a_second_transaction() => Run(async () =>
    {
        AccountViewModel vm = await SignedIn(Trial());

        vm.OpenCheckoutProCommand.Execute(null);
        await vm.ConfirmCheckoutCommand.ExecuteAsync(null);
        await vm.ReopenCheckoutCommand.ExecuteAsync(null);
        vm.CloseCheckoutCommand.Execute(null);
        vm.OpenCheckoutProCommand.Execute(null);
        await vm.ConfirmCheckoutCommand.ExecuteAsync(null);

        Assert.AreEqual(1, accounts.CheckoutSessionsMinted, "Each click minted a transaction that could be paid.");
        CollectionAssert.AreEqual(new[] { accounts.CheckoutUrl, accounts.CheckoutUrl, accounts.CheckoutUrl }, opened);

        // A different offer is a different transaction.
        vm.SelectCheckoutPremiumCommand.Execute(null);
        await vm.ReopenCheckoutCommand.ExecuteAsync(null);
        Assert.AreEqual(2, accounts.CheckoutSessionsMinted);
        vm.CloseCheckoutCommand.Execute(null);
    });

    [TestMethod]
    public void The_wait_survives_a_failed_refresh_and_still_sees_the_payment() => Run(async () =>
    {
        TimeSpan interval = AccountViewModel.CheckoutPollInterval;
        AccountViewModel.CheckoutPollInterval = TimeSpan.FromMilliseconds(20);
        try
        {
            AccountViewModel vm = await SignedIn(Trial());
            vm.OpenCheckoutProCommand.Execute(null);
            await vm.ConfirmCheckoutCommand.ExecuteAsync(null);
            Assert.IsTrue(vm.IsCheckoutWaiting);

            accounts.NextFailure = new AccountException(AccountFailure.ServerError, "503");
            await Task.Delay(150);
            Assert.IsNull(accounts.NextFailure, "The poll never asked the server.");
            Assert.IsTrue(vm.IsCheckoutWaiting);

            accounts.Entitlement = Paying(BillingTier.Pro);
            for (int i = 0; i < 100 && !vm.IsCheckoutDone; i++)
            {
                await Task.Delay(20);
            }

            Assert.IsTrue(vm.IsCheckoutDone, "The poll stopped after one failed refresh.");
        }
        finally
        {
            AccountViewModel.CheckoutPollInterval = interval;
        }
    });

    [TestMethod]
    public void A_second_paid_subscription_is_said_out_loud() => Run(async () =>
    {
        accounts.Billing = new BillingLinks(true, true, Offers: Offers, CanChange: true, DuplicateSubscription: true);
        AccountViewModel vm = await SignedIn(Paying(BillingTier.Pro));

        Assert.IsTrue(vm.HasDuplicateSubscription);
    });

    private sealed class NoExport : IRecoveryKeyExporter
    {
        public Task<bool> TryCopyToClipboardAsync(string recoveryKey) => Task.FromResult(false);

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(false);
    }
}
