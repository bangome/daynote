using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// The subscription, as the settings panel sees it (docs/CLOUD_SYNC.md §14).
/// </summary>
/// <remarks>
/// Cloud sync is the paid feature; the app is not, and nothing here gates note-taking. The two
/// things this surface has to get right are honesty and calm: it must say when the trial ends
/// before it ends (Store policy 10.8.4), and it must not read as data loss when it lapses — because
/// it is not. The notes on this PC never needed an account, and the copy already uploaded is kept.
/// </remarks>
public sealed partial class AccountViewModel
{
    /// <summary>The billing state last read from the server.</summary>
    [ObservableProperty]
    private Entitlement entitlement = Entitlement.Unknown;

    /// <summary>Where to start or manage a subscription. Both are hosted pages, opened in a browser.</summary>
    [ObservableProperty]
    private BillingLinks billing = BillingLinks.None;

    /// <summary>True when sync is off for want of a subscription.</summary>
    public bool IsUnpaid => Entitlement.State == EntitlementState.Expired;

    /// <summary>True while the trial or a grace period is close enough to its end to mention.</summary>
    public bool ShouldWarnAboutEntitlement => Entitlement.ShouldWarn(DateTimeOffset.UtcNow);

    public bool CanCheckout => Billing.CanCheckout;

    public bool CanCheckoutMonthly => Billing.CanCheckoutMonthly;

    public bool CanCheckoutAnnual => Billing.CanCheckoutAnnual;

    public bool CanManageSubscription => Billing.CanManage;

    /// <summary>
    /// The subscription on record was bought on an iPhone through the App Store. No server can
    /// cancel one, and the desktop's portal and plan change do not reach it: it is managed in the
    /// iPhone's Settings, which the subscription card and the delete confirmation say.
    /// </summary>
    public bool HasAppStoreSubscription => Billing.Provider == BillingProvider.Apple && Entitlement.HasSubscribed;

    /// <summary>One line describing the billing state, with the date or the days left in it.</summary>
    public string EntitlementSummary
    {
        get
        {
            int? days = Entitlement.DaysRemaining(DateTimeOffset.UtcNow);
            return Entitlement.State switch
            {
                EntitlementState.Trial => AppStrings.BillingTrial(days ?? 0),
                EntitlementState.Active => Entitlement.Until is { } until
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        AppStrings.BillingActiveFormat,
                        until.ToLocalTime().ToString("d", CultureInfo.CurrentCulture))
                    : AppStrings.BillingActive,
                EntitlementState.Grace => string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.BillingGraceFormat,
                    days ?? 0),
                EntitlementState.Expired => Entitlement.HasSubscribed
                    ? AppStrings.BillingExpired
                    : AppStrings.BillingTrialOver,
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// The last billing read failed — offline, or a server fault — so what is on screen may be stale
    /// or, if nothing was ever read, empty. The phone's plans page offers a retry on it.
    /// </summary>
    [ObservableProperty]
    private bool isBillingUnavailable;

    /// <summary>Re-reads the billing state. Cheap, and safe to call whenever the panel opens.</summary>
    [RelayCommand]
    private async Task RefreshBillingAsync()
    {
        if (!IsSignedIn)
        {
            return;
        }

        try
        {
            (Entitlement entitlement, BillingLinks links) = await accounts
                .ReadBillingAsync()
                .ConfigureAwait(true);

            Entitlement = entitlement;
            Billing = links;
            IsBillingUnavailable = false;
        }
        catch (AccountException failure) when (failure.Failure == AccountFailure.SessionExpired)
        {
            await EndRejectedSessionAsync().ConfigureAwait(true);
        }
        catch (AccountException)
        {
            // Not knowing the billing state is not worth an error banner: sync will report the
            // truth on its next attempt, and the last known state is still on screen. Callers that
            // have nothing else to show read IsBillingUnavailable.
            IsBillingUnavailable = true;
        }
    }

    /// <summary>
    /// Opens the checkout in the browser. Deliberately not an in-app payment form: the provider is
    /// the merchant of record, so it owns the card fields, the tax, and the receipt. The URL is
    /// created for this click, because the transaction behind it carries this account's id.
    /// </summary>
    [RelayCommand]
    private Task CheckoutAnnualAsync() => CheckoutAsync(BillingTier.Pro, BillingPlan.Annual);

    [RelayCommand]
    private Task CheckoutMonthlyAsync() => CheckoutAsync(BillingTier.Pro, BillingPlan.Monthly);

    /// <summary>
    /// How long a checkout page is reused rather than minted again. Every mint is a new Paddle
    /// transaction, and two of them paid in two tabs are two subscriptions; within this window the
    /// same page opens instead. Long enough for someone to fetch a card, short of Paddle's expiry.
    /// </summary>
    private static readonly TimeSpan CheckoutReuseWindow = TimeSpan.FromMinutes(30);

    private (BillingTier Tier, BillingPlan Plan, string Url, DateTimeOffset At)? lastCheckout;

    private async Task CheckoutAsync(BillingTier tier, BillingPlan plan)
    {
        if (Billing.Find(tier, plan) is null)
        {
            return;
        }

        if (lastCheckout is { } last && last.Tier == tier && last.Plan == plan
            && DateTimeOffset.UtcNow - last.At < CheckoutReuseWindow)
        {
            openExternal(last.Url);
            return;
        }

        await RunAsync(async () =>
        {
            string url = await accounts.CreateCheckoutSessionAsync(tier, plan).ConfigureAwait(true);
            lastCheckout = (tier, plan, url, DateTimeOffset.UtcNow);
            openExternal(url);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Moves the running subscription to another tier or interval (docs/CLOUD_SYNC.md §14). Done by
    /// the server through the provider's subscription update, so the card, the renewal date and the
    /// one subscription stay as they are; the answer is the state after the change.
    /// </summary>
    private async Task<bool> ChangePlanAsync(BillingTier tier, BillingPlan plan)
    {
        if (!Billing.CanChange || Billing.Find(tier, plan) is null)
        {
            return false;
        }

        bool changed = false;
        await RunAsync(async () =>
        {
            (Entitlement entitlement, BillingLinks links) = await accounts
                .ChangePlanAsync(tier, plan)
                .ConfigureAwait(true);
            Billing = links;
            Entitlement = entitlement;
            changed = true;
        }).ConfigureAwait(true);
        return changed;
    }

    /// <summary>
    /// Cancelling, changing a card, and finding an invoice all happen at the provider. The link is
    /// minted here and opened at once, because it is single-use and short-lived.
    /// </summary>
    [RelayCommand]
    private async Task ManageSubscriptionAsync()
    {
        if (!Billing.CanManage)
        {
            return;
        }

        await RunAsync(async () =>
        {
            string url = await accounts.CreatePortalSessionAsync().ConfigureAwait(true);
            openExternal(url);
        }).ConfigureAwait(true);
    }

    partial void OnEntitlementChanged(Entitlement value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsUnpaid));
        OnPropertyChanged(nameof(ShouldWarnAboutEntitlement));
        OnPropertyChanged(nameof(EntitlementSummary));
        OnPropertyChanged(nameof(HasAppStoreSubscription));
        RefreshPresentation();
    }

    partial void OnBillingChanged(BillingLinks value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanCheckout));
        OnPropertyChanged(nameof(CanCheckoutMonthly));
        OnPropertyChanged(nameof(CanCheckoutAnnual));
        OnPropertyChanged(nameof(CanManageSubscription));
        OnPropertyChanged(nameof(HasAppStoreSubscription));
        RefreshPresentation();
    }
}
