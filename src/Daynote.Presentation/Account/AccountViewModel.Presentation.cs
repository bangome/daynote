using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>
/// What the account window and the titlebar menu display: the avatar, the plan badge, the banner,
/// and the plan picker (docs/design-renewal/Daynote Account.dc.html).
/// </summary>
/// <remarks>
/// Presentation only — every value here is derived from <see cref="Entitlement"/>, the signed-in
/// address, and <see cref="SyncStatusView"/>. Nothing is invented: the design mock shows a person's
/// name, a device list, and a payment card, and none of those exist as data (the identity we hold is
/// the address, the server keeps no device registry, and the card belongs to the payment provider).
/// Rather than fill those in with plausible text, the surfaces built on this show what is true and
/// send the rest to the provider's own portal.
/// </remarks>
public sealed partial class AccountViewModel
{
    /// <summary>Which interval the plan picker has selected. Annual first: it is the one the site leads with.</summary>
    [ObservableProperty]
    private BillingPlan selectedPlan = BillingPlan.Annual;

    /// <summary>
    /// The single letter in the avatar. Taken from the address because that is the only identity
    /// Google hands us that we keep — the `profile` scope is requested but the name is not stored.
    /// </summary>
    public string AvatarInitial => SignedInEmail is { Length: > 0 } email
        ? email[..1].ToUpper(CultureInfo.CurrentCulture)
        : "?";

    /// <summary>The strong line of the identity row.</summary>
    public string DisplayName => SignedInEmail ?? string.Empty;

    /// <summary>
    /// Whether this deployment sells anything at all.
    /// </summary>
    /// <remarks>
    /// False when the server lists no plans (<c>/v1/billing/status</c> reports
    /// <c>can_checkout: false</c>, which is what clearing the price ids in wrangler.toml does) and
    /// this account has never subscribed. Everything about billing then goes quiet: no plan badge
    /// beyond 무료, no trial countdown, no lapse banner.
    /// <para>
    /// It exists because the alternative is worse than untidy. The trial is granted by the server
    /// at sign-up regardless, so without this the app would count down to the end of a trial for a
    /// feature nobody can buy, and then announce that it had stopped. Answering the server rather
    /// than a build flag means turning the subscription on later is a redeploy, not an app update,
    /// and it reaches installed copies immediately.
    /// </para>
    /// </remarks>
    public bool OffersSubscription => CanCheckout || CanManageSubscription || Entitlement.HasSubscribed;

    /// <summary>The pill next to the name: 무료 / 체험 중 / Pro / 결제 확인 중.</summary>
    public string PlanBadge => OffersSubscription
        ? Entitlement.State switch
        {
            EntitlementState.Trial => AppStrings.AccountPlanTrial,
            EntitlementState.Active => AppStrings.AccountPlanPro,
            EntitlementState.Grace => AppStrings.AccountPlanGrace,
            _ => AppStrings.AccountPlanFree,
        }
        : AppStrings.AccountPlanFree;

    /// <summary>True only for a paid, healthy subscription — the one state whose pill is filled.</summary>
    public bool IsPlanPaid => Entitlement.State == EntitlementState.Active;

    /// <summary>True when the pill should read as something to look at rather than a plain fact.</summary>
    public bool IsPlanAttention => Entitlement.State == EntitlementState.Grace;

    /// <summary>One line under the avatar in the titlebar menu: the address, plan and sync state.</summary>
    public string AvatarTooltip => string.Format(
        CultureInfo.CurrentCulture,
        AppStrings.AccountAvatarTooltipFormat,
        DisplayName,
        PlanBadge,
        Status.Label);

    public bool HasBanner => OffersSubscription
        && (Entitlement.State is EntitlementState.Grace
            || (Entitlement.State == EntitlementState.Trial && ShouldWarnAboutEntitlement)
            || IsUnpaid);

    /// <summary>True for the two banners that report a problem rather than a countdown.</summary>
    public bool IsBannerUrgent => Entitlement.State == EntitlementState.Grace || IsUnpaid;

    public string BannerTitle
    {
        get
        {
            int days = Entitlement.DaysRemaining(DateTimeOffset.UtcNow) ?? 0;
            return Entitlement.State switch
            {
                EntitlementState.Trial => string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.BillingTrialBannerTitleFormat,
                    days),
                EntitlementState.Grace => AppStrings.BillingGraceBannerTitle,
                _ => AppStrings.BillingExpiredBannerTitle,
            };
        }
    }

    public string BannerBody
    {
        get
        {
            int days = Entitlement.DaysRemaining(DateTimeOffset.UtcNow) ?? 0;
            return Entitlement.State switch
            {
                EntitlementState.Trial => AppStrings.BillingTrialBannerBody,
                EntitlementState.Grace => string.Format(
                    CultureInfo.CurrentCulture,
                    AppStrings.BillingGraceBannerBodyFormat,
                    days),
                // The lapse copy, which exists to say plainly that nothing was deleted.
                _ => AppStrings.BillingLapseNote,
            };
        }
    }

    /// <summary>
    /// The upgrade card. Shown whenever there is something to buy and the subscription is not
    /// already running — including during the trial, so nobody has to wait for it to lapse to pay.
    /// </summary>
    public bool ShowUpgrade => CanCheckout && Entitlement.State != EntitlementState.Active;

    /// <summary>The subscription card, which only has anything to say once money is involved.</summary>
    public bool ShowSubscription => Entitlement.State
        is EntitlementState.Active or EntitlementState.Grace
        || (Entitlement.State == EntitlementState.Trial && Entitlement.HasSubscribed);

    public bool IsMonthlySelected => SelectedPlan == BillingPlan.Monthly;

    public bool IsAnnualSelected => SelectedPlan == BillingPlan.Annual;

    public string PriceMain => IsAnnualSelected ? AppStrings.BillingPriceAnnual : AppStrings.BillingPriceMonthly;

    public string PriceUnit => IsAnnualSelected ? AppStrings.BillingPriceUnitAnnual : AppStrings.BillingPriceUnitMonthly;

    public string PriceSub => IsAnnualSelected ? AppStrings.BillingPriceSubAnnual : AppStrings.BillingPriceSubMonthly;

    public string CheckoutLabel => string.Format(
        CultureInfo.CurrentCulture,
        AppStrings.BillingCheckoutFormat,
        PriceMain);

    /// <summary>Rows of the subscription card. Only what the entitlement actually knows.</summary>
    public string SubscriptionPlanText => IsAnnualSelected
        ? AppStrings.BillingPlanAnnual
        : AppStrings.BillingPlanMonthly;

    public string SubscriptionStateText => Entitlement.State switch
    {
        EntitlementState.Trial => AppStrings.BillingStateTrial,
        EntitlementState.Grace => AppStrings.BillingStateGrace,
        _ => AppStrings.BillingStateActive,
    };

    /// <summary>
    /// "다음 결제일" while the subscription renews, "이용 종료일" once it will not: the same date
    /// means opposite things in those two states and labelling both the same would mislead.
    /// </summary>
    public string SubscriptionDateLabel => Entitlement.State == EntitlementState.Active
        ? AppStrings.BillingRowRenews
        : AppStrings.BillingRowEnds;

    public string SubscriptionDateText => Entitlement.Until is { } until
        ? until.ToLocalTime().ToString("D", CultureInfo.CurrentCulture)
        : "—";

    public bool HasSubscriptionDate => Entitlement.Until is not null;

    /// <summary>The build, shown in the window footer so a bug report can name it.</summary>
    public string AppVersionText
    {
        get
        {
            string? informational = typeof(AccountViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            // "1.5.0+9a3f21c" — the commit suffix is noise in a window footer.
            string version = informational is { Length: > 0 }
                ? informational.Split('+')[0]
                : typeof(AccountViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";

            return string.Format(CultureInfo.CurrentCulture, AppStrings.AccountVersionFormat, version);
        }
    }

    /// <summary>
    /// What Pro buys, in the order the pricing page lists it.
    /// </summary>
    /// <remarks>
    /// Two bullets, because two is what the tier is. It used to claim four; "no limit on connected
    /// devices" is what the free plan now advertises on the site, and the note lock is not gated by
    /// the entitlement anywhere — a paid list may only contain things the free tier does not get.
    /// </remarks>
    public IReadOnlyList<string> ProFeatures =>
    [
        AppStrings.BillingFeatureSync,
        AppStrings.BillingFeatureQuota,
    ];

    [RelayCommand]
    private void SelectMonthly() => SelectedPlan = BillingPlan.Monthly;

    [RelayCommand]
    private void SelectAnnual() => SelectedPlan = BillingPlan.Annual;

    /// <summary>Buys whichever interval the picker has selected.</summary>
    [RelayCommand]
    private Task CheckoutSelectedAsync() => CheckoutAsync(SelectedPlan);

    /// <summary>
    /// The banner's action, which differs by state: buy during a trial or after a lapse, and go to
    /// the provider's portal while a payment is being retried, because that is where a card lives.
    /// </summary>
    [RelayCommand]
    private Task ResolveBannerAsync() => Entitlement.State == EntitlementState.Grace
        ? ManageSubscriptionAsync()
        : CheckoutAsync(SelectedPlan);

    [RelayCommand]
    private void OpenTerms() => openExternal(SiteUrl(AppStrings.AccountTermsUrl));

    [RelayCommand]
    private void OpenPrivacy() => openExternal(SiteUrl(AppStrings.AccountPrivacyUrl));

    /// <summary>Joins a site path onto the service origin, so both follow one deployment.</summary>
    private static string SiteUrl(string path) =>
        Composition.DaynoteAppOptions.DeployedSyncEndpoint.TrimEnd('/') + path;

    partial void OnSelectedPlanChanged(BillingPlan value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsMonthlySelected));
        OnPropertyChanged(nameof(IsAnnualSelected));
        OnPropertyChanged(nameof(PriceMain));
        OnPropertyChanged(nameof(PriceUnit));
        OnPropertyChanged(nameof(PriceSub));
        OnPropertyChanged(nameof(CheckoutLabel));
        OnPropertyChanged(nameof(SubscriptionPlanText));
    }

    /// <summary>
    /// Re-raises everything derived from the entitlement, the address or the sync state. Called from
    /// the three <c>On*Changed</c> hooks rather than duplicated across them.
    /// </summary>
    private void RefreshPresentation()
    {
        foreach (string name in new[]
        {
            nameof(AvatarInitial), nameof(DisplayName), nameof(PlanBadge), nameof(IsPlanPaid),
            nameof(IsPlanAttention), nameof(AvatarTooltip), nameof(HasBanner), nameof(IsBannerUrgent),
            nameof(OffersSubscription),
            nameof(BannerTitle), nameof(BannerBody), nameof(ShowUpgrade), nameof(ShowSubscription),
            nameof(SubscriptionStateText), nameof(SubscriptionDateLabel), nameof(SubscriptionDateText),
            nameof(HasSubscriptionDate), nameof(CheckoutLabel), nameof(PriceMain),
            nameof(ProFeatures), nameof(AppVersionText),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
