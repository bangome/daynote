using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The iPhone's plans page (docs/CLOUD_SYNC.md §14.8): prices from StoreKit, a purchase shown as
/// done only once the server has it, restore, the desktop subscriber who must be shown nothing to
/// buy, and the signed-out account that must sign in first.
/// </summary>
/// <remarks>
/// StoreKit is <see cref="FakeStore"/>, which answers the way the iOS head does: a purchase hands
/// its transaction to the handler and finishes it only on a true. The server is a pair of delegates.
/// Real purchases only happen in TestFlight's sandbox, on a device.
/// </remarks>
[TestClass]
public sealed class StoreTests
{
    private static readonly string OutputDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "mobile-screens");

    private const string UserId = "4b1f6d2e-9a39-4c47-8a0e-5f7c1d2b3a40";

    internal static readonly AppStoreProduct[] OnSale =
    [
        new(BillingTier.Pro, BillingPlan.Monthly, "cc.arachat.daynote.pro.monthly"),
        new(BillingTier.Pro, BillingPlan.Annual, "cc.arachat.daynote.pro.annual"),
        new(BillingTier.Premium, BillingPlan.Monthly, "cc.arachat.daynote.premium.monthly"),
        new(BillingTier.Premium, BillingPlan.Annual, "cc.arachat.daynote.premium.annual"),
    ];

    internal static BillingLinks Selling(
        BillingProvider? provider = null,
        string? current = null,
        bool canPurchase = true,
        string? pending = null,
        DateTimeOffset? pendingEffective = null) =>
        new(true, provider == BillingProvider.Paddle, Provider: provider, AppleProducts: OnSale,
            AppleCanPurchase: canPurchase, AppleProductId: current,
            ApplePendingProductId: pending, ApplePendingEffective: pendingEffective);

    /// <summary>The next renewal of the Premium annual subscriber below: midday in Seoul and in UTC alike.</summary>
    private static readonly DateTimeOffset RenewalDay = new(2026, 10, 11, 3, 0, 0, TimeSpan.Zero);

    private const string PremiumAnnual = "cc.arachat.daynote.premium.annual";

    private const string ProAnnual = "cc.arachat.daynote.pro.annual";

    internal static readonly Entitlement Trial = new(
        EntitlementState.Trial, DateTimeOffset.UtcNow.AddDays(10), true, false, BillingTier.Pro, null, 2L << 30, 300L << 20);

    private static Entitlement Paid(BillingTier tier, BillingPlan plan) => new(
        EntitlementState.Active, DateTimeOffset.UtcNow.AddDays(28), true, true, tier, plan,
        tier == BillingTier.Premium ? 200L << 30 : 2L << 30, 1200L << 20);

    [TestMethod]
    public void Prices_come_from_the_App_Store_with_the_terms_beside_them() => WithStore((store, server, _, _) =>
    {
        Assert.IsTrue(store.ShowsPlans);
        Assert.AreEqual("₩24,000 / 년", store.ProCard.PriceText);
        Assert.AreEqual("₩48,000 / 년", store.PremiumCard.PriceText);
        Assert.AreEqual("Daynote Premium (연간) · 1년 · ₩48,000", store.PremiumCard.TermsText);

        store.SelectMonthlyCommand.Execute(null);
        Assert.AreEqual("₩2,900 / 월", store.ProCard.PriceText);
        Assert.AreEqual("Daynote Pro (월간) · 1개월 · ₩2,900", store.ProCard.TermsText);
        Assert.IsTrue(store.ProCard.CanBuy && store.PremiumCard.CanBuy);
        Assert.AreEqual(MobileStrings.Get("StoreSubscribe"), store.ProCard.ButtonText);
    });

    [TestMethod]
    public void A_purchase_is_done_only_once_the_server_has_it() => WithStore((store, server, fake, account) =>
    {
        server.Answer = (Paid(BillingTier.Premium, BillingPlan.Annual), Selling(BillingProvider.Apple, "cc.arachat.daynote.premium.annual"));

        Run(store.PremiumCard.BuyCommand);

        Assert.AreEqual("cc.arachat.daynote.premium.annual", fake.LastPurchase);
        Assert.AreEqual(UserId, fake.LastAccountToken, "The purchase does not carry the account as its appAccountToken.");
        CollectionAssert.AreEqual(new[] { "1000000000000001" }, server.Submitted);
        CollectionAssert.AreEqual(new[] { "1000000000000001" }, fake.Finished);
        Assert.AreEqual("Premium 구독이 시작되었습니다.", store.StatusMessage);
        Assert.AreEqual(BillingTier.Premium, account.Entitlement.Tier);
        Assert.IsTrue(store.PremiumCard.IsCurrent);
        Assert.AreEqual(AppStrings.PlanInUse, store.PremiumCard.ButtonText);
        Assert.IsFalse(store.PremiumCard.CanBuy);
    });

    [TestMethod]
    public void A_purchase_the_server_cannot_confirm_is_kept_and_never_called_an_error() => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(AccountFailure.ServerError, "purchase_pending");

        Run(store.ProCard.BuyCommand);

        Assert.IsEmpty(fake.Finished, "An unconfirmed transaction was finished, so it can never be retried.");
        Assert.IsNull(store.ErrorMessage, "A successful purchase was shown an error.");
        Assert.AreEqual(MobileStrings.Get("StoreConfirmLater"), store.StatusMessage);
        Assert.IsFalse(store.IsConfirming);
        CollectionAssert.AreEqual(MobileStoreViewModel.ConfirmDelays.ToArray(), Waits, "It did not keep trying for long enough.");
        Assert.IsGreaterThanOrEqualTo(MobileStoreViewModel.ConfirmDelays.Count + 1, server.Submitted.Count, "The held transaction was not sent again.");
        Assert.IsGreaterThanOrEqualTo(MobileStoreViewModel.ConfirmDelays.Count, RefreshCount, "The billing state was not polled meanwhile.");

        // Coming back to the app tries once more, and this time the server has it.
        server.Failure = null;
        Pump(store.NotifyResumed);
        Assert.AreEqual("Pro 구독이 시작되었습니다.", store.StatusMessage);
    });

    [TestMethod]
    public void A_purchase_confirmed_on_a_retry_says_it_started() => WithStore((store, server, fake, _) =>
    {
        server.FailTimes = 1;

        Run(store.ProCard.BuyCommand);

        Assert.AreEqual("Pro 구독이 시작되었습니다.", store.StatusMessage);
        Assert.IsNull(store.ErrorMessage);
        CollectionAssert.AreEqual(new[] { MobileStoreViewModel.ConfirmDelays[0] }, Waits);
        CollectionAssert.AreEqual(new[] { "1000000000000001" }, fake.Finished);
    });

    [TestMethod]
    public void A_purchase_activated_by_Apples_notification_is_picked_up_by_polling() => WithStore((store, server, fake, account) =>
    {
        server.Failure = new AccountException(AccountFailure.ServerError, "purchase_pending");
        // The server learns of the purchase from App Store Server Notifications in the meantime: by
        // the second poll (the first read is the one before the sheet).
        int start = RefreshCount;
        RefreshHook = () =>
        {
            if (RefreshCount == start + 3)
            {
                account.Entitlement = Paid(BillingTier.Pro, BillingPlan.Annual);
                account.Billing = Selling(BillingProvider.Apple, "cc.arachat.daynote.pro.annual");
            }
        };

        Run(store.ProCard.BuyCommand);

        Assert.AreEqual("Pro 구독이 시작되었습니다.", store.StatusMessage);
        Assert.IsNull(store.ErrorMessage);
        Assert.HasCount(2, Waits);
    });

    [TestMethod]
    public void Prices_are_asked_for_every_time_and_again_when_the_storefront_changes() => WithStore((store, _, fake, _) =>
    {
        int asked = fake.LoadCalls;
        Pump(store.OpenAsync);
        Assert.AreEqual(asked + 1, fake.LoadCalls, "The page showed prices from an earlier ask.");

        fake.Dollars = true;
        fake.ChangeStorefront();
        Pump(() => Task.Delay(1));
        Assert.AreEqual(asked + 2, fake.LoadCalls);
        Assert.AreEqual("$19.99 / 년", store.ProCard.PriceText);
    });

    [TestMethod]
    public void Another_transaction_confirmed_meanwhile_does_not_count_as_this_purchase() => WithStore((store, server, fake, _) =>
    {
        fake.RenewalDuringPurchase = true;
        server.FailOnly = "1000000000000001";

        Run(store.ProCard.BuyCommand);

        Assert.IsNull(store.ErrorMessage);
        Assert.AreEqual(MobileStrings.Get("StoreConfirmLater"), store.StatusMessage, "An unrecorded purchase was announced as started.");
    });

    [TestMethod]
    public void A_background_renewal_of_another_accounts_subscription_says_nothing() => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(AccountFailure.PurchaseBelongsToAnotherAccount, "other");

        bool finish = Pump2(fake.TransactionHandler!(new StoreTransaction("6000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false)));

        Assert.IsTrue(finish);
        Assert.IsNull(store.ErrorMessage, "An error with nothing behind it from the person holding the phone.");
    });

    [TestMethod]
    public void Terms_open_the_App_Store_licence_agreement() => WithStore((store, _, _, _) =>
    {
        store.OpenTermsCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { MobileStoreViewModel.StandardEulaUrl }, Opened);
    });

    [TestMethod]
    public void A_cancelled_sheet_says_nothing_and_sends_nothing() => WithStore((store, server, fake, _) =>
    {
        fake.Next = StorePurchaseStatus.Cancelled;

        Run(store.ProCard.BuyCommand);

        Assert.IsEmpty(server.Submitted);
        Assert.IsNull(store.ErrorMessage);
        Assert.IsNull(store.StatusMessage);
        Assert.IsFalse(store.IsBusy);
    });

    [TestMethod]
    public void A_failed_purchase_says_so() => WithStore((store, server, fake, _) =>
    {
        fake.Next = StorePurchaseStatus.Failed;

        Run(store.ProCard.BuyCommand);

        Assert.AreEqual("Cannot connect to iTunes Store", store.ErrorMessage);
        Assert.IsEmpty(server.Submitted);
    });

    [TestMethod]
    public void Ask_to_Buy_waits_for_approval() => WithStore((store, _, fake, _) =>
    {
        fake.Next = StorePurchaseStatus.Pending;

        Run(store.ProCard.BuyCommand);

        Assert.AreEqual(MobileStrings.Get("StorePending"), store.StatusMessage);
    });

    [TestMethod]
    public void Another_accounts_purchase_is_finished_and_explained() => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(AccountFailure.PurchaseBelongsToAnotherAccount, "other");

        Run(store.ProCard.BuyCommand);

        CollectionAssert.AreEqual(new[] { "1000000000000001" }, fake.Finished, "It would come back on every launch.");
        Assert.AreEqual(MobileStrings.Get("StoreOtherAccount"), store.ErrorMessage);
    });

    [TestMethod]
    public void Restore_sends_each_purchase_to_the_server() => WithStore((store, server, fake, _) =>
    {
        fake.Restorable.Add("900000000000042");
        server.Answer = (Paid(BillingTier.Pro, BillingPlan.Monthly), Selling(BillingProvider.Apple, "cc.arachat.daynote.pro.monthly"));

        Run(store.RestoreCommand);

        CollectionAssert.AreEqual(new[] { "900000000000042" }, server.Submitted);
        Assert.AreEqual(MobileStrings.Get("StoreRestoreDone"), store.StatusMessage);
        Assert.IsTrue(store.HasAppleSubscription);
    });

    [TestMethod]
    public void A_purchase_the_server_refuses_for_good_is_finished() => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(AccountFailure.PurchaseRefused, "not ours");

        Run(store.ProCard.BuyCommand);

        CollectionAssert.AreEqual(new[] { "1000000000000001" }, fake.Finished, "It would come back on every launch.");
        Assert.AreEqual(MobileStrings.Get("StoreFailed"), store.ErrorMessage);
    });

    [TestMethod]
    public void Signing_in_sends_what_StoreKit_held_back() => WithStorePage(
        Entitlement.Unknown,
        BillingLinks.None,
        (_, _, shell) =>
        {
            int before = Fake!.Retries;
            shell.Account!.SignedInEmail = "someone@example.com";
            Assert.IsGreaterThan(before, Fake.Retries, "Transactions held while signed out are never sent.");
        },
        signedIn: false);

    [TestMethod]
    public void Nothing_StoreKit_holds_is_sent_while_signed_out() => WithStorePage(
        Entitlement.Unknown,
        BillingLinks.None,
        (_, store, shell) =>
        {
            bool finished = Pump2(Fake!.TransactionHandler!(new StoreTransaction("2000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false)));
            Fake.RetryHeld();
            Pump(store.NotifyResumed);

            Assert.IsFalse(finished);
            Assert.IsEmpty(Server!.Submitted, "A held transaction was sent with no account to record it against.");
        },
        signedIn: false);

    [TestMethod]
    public void After_a_401_held_transactions_wait_for_the_next_sign_in() => WithStore((store, server, fake, account) =>
    {
        server.Failure = new AccountException(AccountFailure.InvalidCredentials, "401");
        var renewal = new StoreTransaction("2000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false);
        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)));
        Assert.HasCount(1, server.Submitted);

        // The page reopening, the app coming back, StoreKit handing it over again: none of them
        // sends it while the session is refused.
        server.Failure = null;
        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)));
        Pump(store.NotifyResumed);
        Run(store.OpenCommand);
        Assert.HasCount(1, server.Submitted, "A transaction was sent again after the server refused the session.");

        // Signing in again is what sends it.
        account.SignedInEmail = null;
        account.SignedInEmail = "someone@example.com";
        Assert.IsTrue(Pump2(fake.TransactionHandler!(renewal)));
        Assert.HasCount(2, server.Submitted);
    });

    [TestMethod]
    public void A_revoked_session_signs_out_and_the_page_asks_for_a_sign_in() => WithStorePage(
        Trial,
        Selling(),
        (page, store, shell) =>
        {
            Server!.Failure = new AccountException(AccountFailure.SessionExpired, "The session expired.");

            Assert.IsFalse(Pump2(Fake!.TransactionHandler!(new StoreTransaction("2000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false))));
            Settle(page);

            Assert.IsFalse(shell.Account!.IsSignedIn, "The app still claims a session the server revoked.");
            Assert.IsTrue(store.ShowsSignInPrompt);
            Assert.IsFalse(store.ShowsPlans);
            Assert.IsTrue(TextShown(page, MobileStrings.Get("StoreSignInTitle")));
            Assert.IsTrue(TextShown(page, AppStrings.AccountSessionEnded), "The page does not say the session ended.");
            Assert.AreEqual(AppStrings.AccountSessionEnded, shell.AccountCardSubtitle, "The account card does not say the session ended.");
        });

    [TestMethod]
    public void Many_held_renewals_of_one_subscription_are_sent_once() => WithStore((store, server, fake, _) =>
    {
        // StoreKit 1 hands back one transaction per renewal, all at once at launch; the server reads
        // the subscription's state from any one of them.
        server.Gate = new TaskCompletionSource();
        Task<bool>[] settling =
        [
            .. Enumerable.Range(1, 10).Select(i => fake.TransactionHandler!(
                new StoreTransaction($"30000000000000{i:00}", "cc.arachat.daynote.pro.monthly", IsRestore: false))),
        ];
        server.Gate.SetResult();
        Pump(() => Task.WhenAll(settling));

        Assert.HasCount(1, server.Submitted, "Each renewal of one subscription was sent on its own.");
        Assert.IsTrue(settling.All(task => task.Result), "The renewals that waited were not finished with the first.");
    });

    [TestMethod]
    public void A_subscription_answered_once_is_not_sent_again_this_run() => WithStore((store, server, fake, _) =>
    {
        var first = new StoreTransaction("3000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false);
        Assert.IsTrue(Pump2(fake.TransactionHandler!(first)));

        // The next launch's or resume's replay of more renewals of it: finished, nothing sent.
        for (int i = 2; i <= 6; i++)
        {
            Assert.IsTrue(Pump2(fake.TransactionHandler!(
                new StoreTransaction($"300000000000000{i}", "cc.arachat.daynote.pro.monthly", IsRestore: false))));
        }

        Assert.HasCount(1, server.Submitted);
    });

    [TestMethod]
    [DataRow(AccountFailure.PurchaseBelongsToAnotherAccount)]
    [DataRow(AccountFailure.PurchaseRefused)]
    public void A_subscription_refused_for_good_is_finished_and_not_sent_again(AccountFailure refusal) => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(refusal, "refused");
        Assert.IsTrue(Pump2(fake.TransactionHandler!(new StoreTransaction("3000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false))));
        Assert.IsTrue(Pump2(fake.TransactionHandler!(new StoreTransaction("3000000000000002", "cc.arachat.daynote.pro.monthly", IsRestore: false))));

        Assert.HasCount(1, server.Submitted, "StoreKit would keep handing a refused subscription back.");
    });

    [TestMethod]
    public void A_429_holds_everything_until_its_Retry_After() => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(AccountFailure.RateLimited, "429") { RetryAfter = TimeSpan.FromMinutes(5) };
        var renewal = new StoreTransaction("3000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false);
        var other = new StoreTransaction("4000000000000001", "cc.arachat.daynote.premium.annual", IsRestore: false);
        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)));

        server.Failure = null;
        Now += TimeSpan.FromMinutes(4);
        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)), "A held transaction was sent inside the server's Retry-After.");
        Assert.IsFalse(Pump2(fake.TransactionHandler!(other)));
        Pump(store.NotifyResumed);
        Assert.HasCount(1, server.Submitted);

        Now += TimeSpan.FromMinutes(1);
        Assert.IsTrue(Pump2(fake.TransactionHandler!(renewal)));
        Assert.HasCount(2, server.Submitted);
    });

    [TestMethod]
    public void A_5xx_without_Retry_After_backs_off_from_30_seconds_and_doubles() => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(AccountFailure.ServerError, "503");
        var renewal = new StoreTransaction("3000000000000001", "cc.arachat.daynote.pro.monthly", IsRestore: false);

        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)));
        Now += TimeSpan.FromSeconds(29);
        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)));
        Assert.HasCount(1, server.Submitted, "Sent again before the first 30 seconds were up.");

        Now += TimeSpan.FromSeconds(1);
        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)));
        Assert.HasCount(2, server.Submitted);

        // The second wait is twice the first.
        Now += TimeSpan.FromSeconds(59);
        Assert.IsFalse(Pump2(fake.TransactionHandler!(renewal)));
        Assert.HasCount(2, server.Submitted);
        Now += TimeSpan.FromSeconds(1);
        server.Failure = null;
        Assert.IsTrue(Pump2(fake.TransactionHandler!(renewal)));
        Assert.HasCount(3, server.Submitted);
    });

    [TestMethod]
    public void The_persons_purchase_goes_before_replays_of_old_renewals() => WithStore((store, server, fake, _) =>
    {
        // Old renewals StoreKit holds are replayed while the sheet is up and while it is confirmed:
        // none of them may spend the limit the purchase's own confirmation needs.
        fake.ReplayDuringPurchase = [.. Enumerable.Range(1, 10).Select(i =>
            new StoreTransaction($"30000000000000{i:00}", "cc.arachat.daynote.premium.monthly", IsRestore: false))];
        server.FailTimes = 1;

        Run(store.ProCard.BuyCommand);

        CollectionAssert.AreEqual(new[] { "1000000000000001", "1000000000000001" }, server.Submitted,
            "Replays of old renewals were sent ahead of, or beside, the purchase.");
        Assert.AreEqual("Pro 구독이 시작되었습니다.", store.StatusMessage);

        // With the purchase confirmed, the replays go — once for their subscription.
        fake.RetryHeld();
        Assert.HasCount(3, server.Submitted);
    });

    [TestMethod]
    public void A_429_while_confirming_ends_calmly_instead_of_spinning() => WithStore((store, server, fake, _) =>
    {
        server.Failure = new AccountException(AccountFailure.RateLimited, "429") { RetryAfter = TimeSpan.FromMinutes(10) };

        Run(store.ProCard.BuyCommand);

        Assert.IsFalse(store.IsConfirming, "The page is still spinning.");
        Assert.AreEqual(MobileStrings.Get("StoreConfirmLater"), store.StatusMessage);
        Assert.IsNull(store.ErrorMessage, "A purchase that went through was shown an error.");
        Assert.HasCount(1, server.Submitted, "The purchase was sent again inside the server's Retry-After.");
    });

    [TestMethod]
    public void Confirming_has_a_hard_time_limit_however_slow_the_answers() => WithStore((store, server, fake, _) =>
    {
        // Every billing read takes a minute: the budget runs out long before the delays do.
        server.Failure = new AccountException(AccountFailure.ServerError, "purchase_pending");
        RefreshHook = () => Now += TimeSpan.FromMinutes(1);

        Run(store.ProCard.BuyCommand);

        Assert.IsFalse(store.IsConfirming);
        Assert.AreEqual(MobileStrings.Get("StoreConfirmLater"), store.StatusMessage);
        Assert.IsLessThan(MobileStoreViewModel.ConfirmDelays.Count, Waits.Count, "The time limit did not stop it.");
    });

    [TestMethod]
    public void A_billing_state_that_will_not_load_offers_a_retry() => WithStorePage(
        Entitlement.Unknown,
        BillingLinks.None,
        (page, store, shell) =>
        {
            shell.Account!.IsBillingUnavailable = true;
            Settle(page);

            Assert.IsTrue(store.ShowsBillingError);
            Assert.IsTrue(TextShown(page, AppStrings.BillingLoadFailed), "The page stands empty instead of saying what failed.");
            Assert.IsTrue(VisibleButtons(page).Contains(AppStrings.Retry));

            int before = RefreshCount;
            Run(store.OpenCommand);
            Assert.IsGreaterThan(before, RefreshCount, "다시 시도 did not read the billing state again.");

            shell.Account.IsBillingUnavailable = false;
            shell.Account.Billing = Selling();
            Assert.IsFalse(store.ShowsBillingError);
            Assert.IsTrue(store.ShowsPlans);
        });

    [TestMethod]
    public void Restore_with_nothing_to_restore_says_so() => WithStore((store, server, _, _) =>
    {
        Run(store.RestoreCommand);

        Assert.IsEmpty(server.Submitted);
        Assert.AreEqual(MobileStrings.Get("StoreRestoreNone"), store.StatusMessage);
    });

    [TestMethod]
    public void A_Pro_App_Store_subscriber_is_offered_the_upgrade() => WithStore(
        (store, _, _, _) =>
        {
            store.SelectMonthlyCommand.Execute(null);
            Assert.IsTrue(store.ProCard.IsCurrent);
            Assert.AreEqual(MobileStrings.Get("StoreUpgrade"), store.PremiumCard.ButtonText);
            Assert.IsTrue(store.PremiumCard.CanBuy);
            Assert.IsTrue(store.HasAppleSubscription, "구독 관리 has nothing to manage.");
        },
        Paid(BillingTier.Pro, BillingPlan.Monthly),
        Selling(BillingProvider.Apple, "cc.arachat.daynote.pro.monthly"));

    [TestMethod]
    public void A_Premium_App_Store_subscriber_is_told_a_move_to_Pro_waits_for_the_renewal() => WithStore(
        (store, _, _, _) =>
        {
            Assert.AreEqual("다음 갱신부터 변경", store.ProCard.ButtonText);
            Assert.IsTrue(store.ProCard.CanBuy);
            Assert.IsFalse(store.HasPendingChange);

            // The other Premium interval is the same tier: a plain switch.
            store.SelectMonthlyCommand.Execute(null);
            Assert.AreEqual(MobileStrings.Get("StoreSwitch"), store.PremiumCard.ButtonText);
            Assert.AreEqual("다음 갱신부터 변경", store.ProCard.ButtonText);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            try
            {
                Assert.AreEqual("Switch at renewal", store.ProCard.ButtonText);
            }
            finally
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
            }
        },
        Paid(BillingTier.Premium, BillingPlan.Annual),
        Selling(BillingProvider.Apple, PremiumAnnual));

    [TestMethod]
    public void A_downgrade_booked_for_the_renewal_is_said_on_the_plan_in_force() => WithStorePage(
        Paid(BillingTier.Premium, BillingPlan.Annual),
        Selling(BillingProvider.Apple, PremiumAnnual, pending: ProAnnual, pendingEffective: RenewalDay),
        (page, store, _) =>
        {
            const string notice = "다음 갱신일(10월 11일)부터 Pro(연간)로 바뀝니다. 그때까지 Premium을 그대로 쓸 수 있어요.";
            Assert.IsTrue(store.HasPendingChange);
            Assert.AreEqual(notice, store.PendingChangeText);
            Assert.IsTrue(TextShown(page, notice), "The pending change is not on the page.");
            Assert.IsTrue(store.PremiumCard.IsCurrent, "The entitlement stays Premium until the renewal.");

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            try
            {
                Assert.AreEqual("Changes to Pro (annual) on Oct 11. Premium stays on until then.", store.PendingChangeText);
            }
            finally
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
            }
        });

    [TestMethod]
    public void The_plan_booked_for_the_renewal_is_marked_scheduled_not_offered_again() => WithStore(
        (store, server, fake, _) =>
        {
            Assert.IsTrue(store.ProCard.IsScheduled);
            Assert.AreEqual("예약됨", store.ProCard.ButtonText);
            Assert.IsFalse(store.ProCard.CanBuy);
            Assert.IsFalse(store.PremiumCard.IsScheduled);

            // Pro monthly is not the booked product: it can still be chosen instead.
            store.SelectMonthlyCommand.Execute(null);
            Assert.IsFalse(store.ProCard.IsScheduled);
            Assert.AreEqual("다음 갱신부터 변경", store.ProCard.ButtonText);

            LocalizationService.Instance.SetLanguage(AppLanguage.English);
            try
            {
                store.SelectAnnualCommand.Execute(null);
                Assert.AreEqual("Scheduled", store.ProCard.ButtonText);
            }
            finally
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
            }
        },
        Paid(BillingTier.Premium, BillingPlan.Annual),
        Selling(BillingProvider.Apple, PremiumAnnual, pending: ProAnnual, pendingEffective: RenewalDay));

    [TestMethod]
    public void A_downgrade_purchase_says_when_it_applies_instead_of_a_change_that_never_shows() => WithStore(
        (store, server, fake, account) =>
        {
            // StoreKit returns; the server still reports Premium, with Pro booked for the renewal.
            server.Answer = (Paid(BillingTier.Premium, BillingPlan.Annual),
                Selling(BillingProvider.Apple, PremiumAnnual, pending: ProAnnual, pendingEffective: RenewalDay));

            Run(store.ProCard.BuyCommand);

            Assert.AreEqual(ProAnnual, fake.LastPurchase);
            CollectionAssert.AreEqual(new[] { "1000000000000001" }, fake.Finished);
            Assert.AreEqual("다음 갱신일(10월 11일)부터 Pro(연간)로 바뀝니다. 그때까지 Premium을 그대로 쓸 수 있어요.", store.StatusMessage);
            Assert.IsNull(store.ErrorMessage);
            Assert.AreEqual(BillingTier.Premium, account.Entitlement.Tier);
            Assert.IsTrue(store.ProCard.IsScheduled);
        },
        Paid(BillingTier.Premium, BillingPlan.Annual),
        Selling(BillingProvider.Apple, PremiumAnnual));

    [TestMethod]
    public void A_downgrade_purchase_before_the_server_reports_it_says_Apple_scheduled_it() => WithStore(
        (store, server, _, _) =>
        {
            // A server older than the pending fields, or not yet told: the old product, nothing pending.
            server.Answer = (Paid(BillingTier.Premium, BillingPlan.Annual), Selling(BillingProvider.Apple, PremiumAnnual));

            Run(store.ProCard.BuyCommand);

            Assert.AreEqual("Apple에서 변경을 예약했습니다. 다음 갱신일부터 적용됩니다.", store.StatusMessage);
            Assert.IsNull(store.ErrorMessage);
        },
        Paid(BillingTier.Premium, BillingPlan.Annual),
        Selling(BillingProvider.Apple, PremiumAnnual));

    [TestMethod]
    public void A_desktop_subscriber_is_shown_as_subscribed_with_nothing_to_buy() => WithStore(
        (store, server, fake, _) =>
        {
            Assert.IsTrue(store.IsManagedElsewhere);
            Assert.IsFalse(store.ShowsPlans);

            Run(store.PremiumCard.BuyCommand);
            Assert.IsNull(fake.LastPurchase, "A second subscription was sold beside the desktop's.");
            Assert.IsEmpty(server.Submitted);
        },
        Paid(BillingTier.Pro, BillingPlan.Annual),
        Selling(BillingProvider.Paddle, canPurchase: false));

    [TestMethod]
    public void The_desktop_subscribers_page_has_no_price_link_or_purchase() => WithStorePage(
        Paid(BillingTier.Pro, BillingPlan.Annual),
        Selling(BillingProvider.Paddle, canPurchase: false),
        (page, store, _) =>
        {
            Assert.IsTrue(TextShown(page, MobileStrings.Get("StoreManagedTitle")));
            string[] visible = [.. VisibleTexts(page)];
            Assert.IsFalse(visible.Any(text => text.Contains('₩') || text.Contains('$')), "A price is shown to a desktop subscriber.");
            Assert.IsFalse(VisibleButtons(page).Any(text => text == MobileStrings.Get("StoreManage")
                || text == MobileStrings.Get("StoreSubscribe") || text == MobileStrings.Get("StoreTerms")));
        });

    [TestMethod]
    public void Signed_out_the_page_asks_for_a_sign_in_first() => WithStorePage(
        Entitlement.Unknown,
        BillingLinks.None,
        (page, store, shell) =>
        {
            Assert.IsTrue(store.ShowsSignInPrompt);
            Assert.IsFalse(store.ShowsPlans);
            Assert.IsTrue(TextShown(page, MobileStrings.Get("StoreSignInTitle")));

            store.SignInCommand.Execute(null);
            Assert.IsFalse(shell.IsStoreOpen);
            Assert.IsTrue(shell.IsAccountOpen, "Sign in did not lead to the account page.");
        },
        signedIn: false);

    [TestMethod]
    public void The_plans_page_is_reached_from_the_account_page_on_the_iPhone_only() => WithStorePage(
        Trial,
        Selling(),
        (page, store, shell) =>
        {
            Assert.IsTrue(shell.HasStore);
            Assert.IsTrue(TextShown(page, MobileStrings.Get("StoreAutoRenew")), "The auto-renewal terms are not beside the buttons.");
            Assert.IsTrue(VisibleButtons(page).Contains(MobileStrings.Get("StoreTerms")));
            Assert.IsTrue(VisibleButtons(page).Contains(MobileStrings.Get("StorePrivacy")));
            Assert.IsTrue(VisibleButtons(page).Contains(MobileStrings.Get("StoreRestore")));
            Assert.IsFalse(shell.ShowDock, "The tab bar shows over the plans page.");
        });

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void The_plans_page_renders(string variant)
    {
        Directory.CreateDirectory(OutputDirectory);
        string suffix = variant.ToLowerInvariant();
        foreach ((string name, Entitlement entitlement, BillingLinks billing) in new[]
        {
            ("store", Trial, Selling()),
            ("store-apple-pro", Paid(BillingTier.Pro, BillingPlan.Annual), Selling(BillingProvider.Apple, "cc.arachat.daynote.pro.annual")),
            ("store-desktop", Paid(BillingTier.Premium, BillingPlan.Annual), Selling(BillingProvider.Paddle, canPurchase: false)),
        })
        {
            WithStorePage(entitlement, billing, (page, _, shell) =>
            {
                shell.IsDark = variant == "Dark";
                Settle(page);
                Capture(page, Path.Combine(OutputDirectory, $"{name}-{suffix}.png"));
            });
        }
    }

    /// <summary>
    /// The App Store review screenshot each subscription needs: the plans page at a 6.9-inch
    /// iPhone's size (1320x2868): the plan in force, both plans with their terms, restore and manage. Rendered on request only, like the
    /// store images.
    /// </summary>
    [TestMethod]
    public void Review_screenshot()
    {
        if (Environment.GetEnvironmentVariable("DAYNOTE_STORE_SHOTS") != "1")
        {
            Assert.Inconclusive("Store images are rendered on request: set DAYNOTE_STORE_SHOTS=1.");
        }

        Directory.CreateDirectory(OutputDirectory);
        // 440x956 points at 3x is the 6.9-inch iPhone's 1320x2868; the view itself stays 390 wide,
        // so the scale is 1320/390 and the height follows.
        WithStorePage(Trial, Selling(), (page, store, shell) =>
        {
            page.FindAncestorOfType<Views.MainView>()!.PreviewSafeArea = new Thickness(0, 54, 0, 30);
            Settle(page);
            Capture(page, Path.Combine(OutputDirectory, "store-review-1320x2868.png"));
        }, scale: 1320.0 / 390, height: 2868 / (1320.0 / 390));
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────────

    private delegate void StoreBody(MobileStoreViewModel store, FakeServer server, FakeStore fake, AccountViewModel account);

    /// <summary>The view model alone, over the real account view model, signed in, prices loaded.</summary>
    private static void WithStore(StoreBody body, Entitlement? entitlement = null, BillingLinks? billing = null) =>
        WithStorePage(entitlement ?? Trial, billing ?? Selling(), (_, store, shell) =>
        {
            body(store, Server!, Fake!, shell.Account!);
        });

    [ThreadStatic]
    private static FakeServer? Server;

    private static readonly List<string> Opened = [];

    private static readonly List<TimeSpan> Waits = [];

    private static int RefreshCount;

    /// <summary>The page's clock: moved on by every wait, and by a test that lets time pass.</summary>
    private static DateTimeOffset Now;

    private static Action? RefreshHook;

    private static bool Pump2(Task<bool> task)
    {
        Pump(() => task);
        return task.Result;
    }

    [ThreadStatic]
    private static FakeStore? Fake;

    private static void WithStorePage(
        Entitlement entitlement,
        BillingLinks billing,
        Action<Views.StorePage, MobileStoreViewModel, MobileShellViewModel> body,
        bool signedIn = true,
        double scale = 1,
        double height = 844)
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Opened.Clear();
        Waits.Clear();
        RefreshCount = 0;
        RefreshHook = null;
        Now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var fake = new FakeStore();
        var server = new FakeServer();
        using var data = new TempDataRoot();
        HeadlessAppFixture.OnUiThread(() =>
        {
            ServiceProvider provider = TestServices.Build(data.Path, Application.Current!, TestServices.ShellSetup.WithAccount(services =>
                services.AddSingleton(sp => new MobileStoreViewModel(
                    fake,
                    sp.GetRequiredService<AccountViewModel>(),
                    _ => ValueTask.FromResult<string?>(UserId),
                    server.SubmitAsync,
                    () =>
                    {
                        RefreshCount++;
                        RefreshHook?.Invoke();
                        return Task.CompletedTask;
                    },
                    url => Opened.Add(url),
                    wait =>
                    {
                        Waits.Add(wait);
                        Now += wait;
                        return Task.CompletedTask;
                    },
                    () => Now))));
            var shell = provider.GetRequiredService<MobileShellViewModel>();

            // 390x844 logical, the narrowest mainstream iPhone; scaled as a whole for a store image.
            var view = new Views.MainView { DataContext = shell, Width = 390, Height = height };
            var host = new Window
            {
                Width = 390 * scale,
                Height = height * scale,
                Content = new LayoutTransformControl { LayoutTransform = new Avalonia.Media.ScaleTransform(scale, scale), Child = view },
            };
            host.Show();
            Pump(() => shell.InitializeAsync());

            Server = server;
            Fake = fake;
            AccountViewModel account = shell.Account!;
            if (signedIn)
            {
                account.SignedInEmail = "someone@example.com";
            }

            shell.GoToPageCommand.Execute(MobilePage.Settings);
            shell.OpenAccountCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            account.Entitlement = entitlement;
            account.Billing = billing;
            shell.OpenStoreCommand.Execute(null);
            Pump(() => Task.Delay(1));
            Settle(view);

            try
            {
                Views.StorePage page = view.GetVisualDescendants().OfType<Views.StorePage>().Single();
                body(page, shell.Store!, shell);
            }
            finally
            {
                host.Close();
            }
        });
    }

    private static void Run(System.Windows.Input.ICommand command)
    {
        if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand)
        {
            Pump(() => asyncCommand.ExecuteAsync(null));
        }
        else
        {
            command.Execute(null);
        }
    }

    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "Timed out.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }

        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Settle(Control view)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(view)?.UpdateLayout();
        }
    }

    private static void Capture(Control page, string path)
    {
        using WriteableBitmap? frame = (TopLevel.GetTopLevel(page) as Window)?.CaptureRenderedFrame();
        frame?.Save(path, new PngBitmapEncoderOptions());
    }

    private static IEnumerable<string> VisibleTexts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .Select(text => text.Text!);

    private static IEnumerable<string> VisibleButtons(Control root) =>
        root.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .Select(button => button.Content as string ?? string.Empty);

    private static bool TextShown(Control root, string text) => VisibleTexts(root).Contains(text);

    /// <summary>The server: records what it was told, and answers with a billing state or a failure.</summary>
    internal sealed class FakeServer
    {
        public List<string> Submitted { get; } = [];

        public (Entitlement, BillingLinks)? Answer { get; set; }

        public AccountException? Failure { get; set; }

        /// <summary>Fails just this transaction id, as Apple being unreachable for one call would.</summary>
        public string? FailOnly { get; set; }

        /// <summary>Fails the first this many calls, as Apple not yet knowing a new purchase does.</summary>
        public int FailTimes { get; set; }

        /// <summary>Holds every answer until it is set, as a slow server would, so calls overlap.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async ValueTask<(Entitlement Entitlement, BillingLinks Links)> SubmitAsync(string transactionId, CancellationToken token)
        {
            Submitted.Add(transactionId);
            if (Gate is { } gate)
            {
                await gate.Task.ConfigureAwait(true);
            }

            if (Failure is { } failure)
            {
                throw failure;
            }

            if (FailOnly == transactionId)
            {
                throw new AccountException(AccountFailure.Offline, "offline");
            }

            if (FailTimes > 0)
            {
                FailTimes--;
                throw new AccountException(AccountFailure.ServerError, "purchase_pending");
            }

            return Answer ?? (Paid(BillingTier.Pro, BillingPlan.Annual), Selling(BillingProvider.Apple));
        }
    }

    /// <summary>StoreKit, as the iOS head drives it: the handler decides whether a transaction is finished.</summary>
    internal sealed class FakeStore : IStorePurchases
    {
        private int serial;

        public StorePurchaseStatus Next { get; set; } = StorePurchaseStatus.Purchased;

        public bool RenewalDuringPurchase { get; set; }

        /// <summary>Held transactions StoreKit hands back while the sheet is up, as a launch replay would.</summary>
        public StoreTransaction[] ReplayDuringPurchase { get; set; } = [];

        public string? LastPurchase { get; private set; }

        public string? LastAccountToken { get; private set; }

        public List<string> Finished { get; } = [];

        public List<string> Restorable { get; } = [];

        public bool CanMakePayments => true;

        public Func<StoreTransaction, Task<bool>>? TransactionHandler { get; set; }

        public Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken)
        {
            LoadCalls++;
            var prices = Dollars ? new Dictionary<string, (string Title, string Price)>
            {
                ["cc.arachat.daynote.pro.monthly"] = ("Daynote Pro Monthly", "$2.49"),
                ["cc.arachat.daynote.pro.annual"] = ("Daynote Pro Annual", "$19.99"),
                ["cc.arachat.daynote.premium.monthly"] = ("Daynote Premium Monthly", "$4.99"),
                ["cc.arachat.daynote.premium.annual"] = ("Daynote Premium Annual", "$39.99"),
            } : new Dictionary<string, (string Title, string Price)>
            {
                ["cc.arachat.daynote.pro.monthly"] = ("Daynote Pro (월간)", "₩2,900"),
                ["cc.arachat.daynote.pro.annual"] = ("Daynote Pro (연간)", "₩24,000"),
                ["cc.arachat.daynote.premium.monthly"] = ("Daynote Premium (월간)", "₩5,900"),
                ["cc.arachat.daynote.premium.annual"] = ("Daynote Premium (연간)", "₩48,000"),
            };
            return Task.FromResult<IReadOnlyList<StoreProduct>>(
                [.. productIds.Where(prices.ContainsKey).Select(id => new StoreProduct(id, prices[id].Title, prices[id].Price))]);
        }

        public async Task<StorePurchaseResult> PurchaseAsync(string productId, string accountToken, CancellationToken cancellationToken)
        {
            LastPurchase = productId;
            LastAccountToken = accountToken;
            switch (Next)
            {
                case StorePurchaseStatus.Purchased:
                    string id = $"10000000000000{++serial:00}";
                    foreach (StoreTransaction replay in ReplayDuringPurchase)
                    {
                        if (await TransactionHandler!(replay).ConfigureAwait(true))
                        {
                            Finished.Add(replay.TransactionId);
                        }
                        else
                        {
                            held.Add(replay);
                        }
                    }

                    var purchased = new StoreTransaction(id, productId, IsRestore: false);
                    if (await TransactionHandler!(purchased).ConfigureAwait(true))
                    {
                        Finished.Add(id);
                    }
                    else
                    {
                        held.Add(purchased);
                    }

                    // A renewal of something else settles during the sheet, and confirms: the result
                    // must still be about this purchase.
                    if (RenewalDuringPurchase)
                    {
                        await TransactionHandler!(new StoreTransaction("5000000000000001", productId, IsRestore: false)).ConfigureAwait(true);
                    }

                    return new StorePurchaseResult(StorePurchaseStatus.Purchased, TransactionId: id);
                case StorePurchaseStatus.Failed:
                    return new StorePurchaseResult(StorePurchaseStatus.Failed, "Cannot connect to iTunes Store");
                default:
                    return new StorePurchaseResult(Next);
            }
        }

        public async Task<int> RestoreAsync(CancellationToken cancellationToken)
        {
            foreach (string id in Restorable)
            {
                if (await TransactionHandler!(new StoreTransaction(id, "cc.arachat.daynote.pro.monthly", IsRestore: true)).ConfigureAwait(true))
                {
                    Finished.Add(id);
                }
            }

            return Restorable.Count;
        }

        public void OpenSubscriptionManagement()
        {
        }

        public int Retries { get; private set; }

        private readonly List<StoreTransaction> held = [];

        public event EventHandler? StorefrontChanged;

        public int LoadCalls { get; private set; }

        public bool Dollars { get; set; }

        public void ChangeStorefront() => StorefrontChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>As the iOS head does: what the handler declined is held and sent again here.</summary>
        public void RetryHeld()
        {
            Retries++;
            StoreTransaction[] waiting = [.. held];
            held.Clear();
            foreach (StoreTransaction transaction in waiting)
            {
                Task<bool> settling = TransactionHandler!(transaction);
                if (settling.IsCompleted && settling.Result)
                {
                    Finished.Add(transaction.TransactionId);
                }
                else
                {
                    held.Add(transaction);
                }
            }
        }
    }
}
