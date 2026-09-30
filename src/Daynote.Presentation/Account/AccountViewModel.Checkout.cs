using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Localization;
using Daynote.Core.Sync;

namespace Daynote.App.Account;

/// <summary>Where the checkout dialog is: closed, choosing, waiting on the browser, or done.</summary>
public enum CheckoutStage
{
    Closed,
    Form,
    Waiting,
    Done,
}

/// <summary>One column of the plan table: 무료, Pro or Premium, and what its footer offers.</summary>
public sealed class PlanColumn
{
    internal PlanColumn(BillingTier? tier, IRelayCommand choose)
    {
        Tier = tier;
        ChooseCommand = choose;
    }

    /// <summary>Null for the free column.</summary>
    public BillingTier? Tier { get; }

    public required string Name { get; init; }

    public required string Price { get; init; }

    public required string Per { get; init; }

    /// <summary>Drawn on the tinted background: the plan in use, or Pro as the recommendation.</summary>
    public bool IsHighlighted { get; init; }

    /// <summary>Carries the small "이용 중" tag beside its name.</summary>
    public bool IsCurrent { get; init; }

    public bool CanBuy { get; init; }

    public string ButtonText { get; init; } = string.Empty;

    /// <summary>The filled button, rather than the outlined one.</summary>
    public bool IsPrimary { get; init; }

    public string Note { get; init; } = string.Empty;

    public bool HasNote => !CanBuy && Note.Length > 0;

    /// <summary>Opens the checkout on this column's tier.</summary>
    public IRelayCommand ChooseCommand { get; }
}

/// <summary>
/// The plan table, the subscription card and the checkout dialog (Daynote Desktop B v2): the
/// subscription surfaces of the settings page's 계정 section.
/// </summary>
/// <remarks>
/// Payment itself never happens in the app. The dialog chooses a tier and an interval and states the
/// price; its confirm button asks the server for a checkout for exactly that and opens Paddle's page
/// in the browser. The dialog then waits, re-reading the billing state, and shows its success state
/// only once the server reports the new subscription — never on the click alone, which proves
/// nothing about a payment.
/// <para>
/// An account that is already paying does not go through a checkout at all: the server moves its
/// subscription in place (<c>/v1/billing/change</c>), because a second checkout would bill twice.
/// Cancelling stays at the provider's portal, which is the one place it can be done.
/// </para>
/// <para>
/// Every price is the server's (<see cref="BillingLinks.AvailableOffers"/>), shown in the currency of
/// the interface language. A server from before the price list sends none, and only Pro existed
/// then, so Pro falls back to the catalog's own two figures and Premium is simply not offered.
/// </para>
/// </remarks>
public sealed partial class AccountViewModel
{
    /// <summary>How often the waiting dialog re-reads the billing state.</summary>
    internal static TimeSpan CheckoutPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gives up polling after this long; focusing the window still re-reads it.</summary>
    private static readonly TimeSpan CheckoutPollLimit = TimeSpan.FromMinutes(20);

    [ObservableProperty]
    private CheckoutStage checkoutStage = CheckoutStage.Closed;

    /// <summary>The tier the plan picker, the dialog and the upgrade card have selected.</summary>
    [ObservableProperty]
    private BillingTier checkoutTier = BillingTier.Pro;

    /// <summary>Set when the dialog's last confirm was a plan change rather than a purchase.</summary>
    private bool checkoutWasChange;

    private CancellationTokenSource? checkoutPoll;

    public bool IsCheckoutOpen => CheckoutStage != CheckoutStage.Closed;

    public bool IsCheckoutForm => CheckoutStage == CheckoutStage.Form;

    public bool IsCheckoutWaiting => CheckoutStage == CheckoutStage.Waiting;

    public bool IsCheckoutDone => CheckoutStage == CheckoutStage.Done;

    /// <summary>True while a subscription is paid up and working: Active, or Grace while a card is retried.</summary>
    public bool IsPaying => Entitlement.State is EntitlementState.Active or EntitlementState.Grace;

    /// <summary>
    /// True when confirming moves the running subscription rather than buying one. Only for an
    /// account the server says it can change — a subscription being retried or paused is put right
    /// in the portal first.
    /// </summary>
    public bool IsCheckoutChange => IsPaying && Billing.CanChange;

    /// <summary>The whole plan table, shown whenever something is on sale.</summary>
    public bool ShowPlanTable => OffersSubscription && Billing.AvailableOffers.Count > 0;

    public bool IsCheckoutPro => CheckoutTier == BillingTier.Pro;

    public bool IsCheckoutPremium => CheckoutTier == BillingTier.Premium;

    public bool CanChoosePro => Billing.Sells(BillingTier.Pro);

    public bool CanChoosePremium => Billing.Sells(BillingTier.Premium);

    public string CheckoutTitle => IsCheckoutChange ? AppStrings.CheckoutChangeTitle : AppStrings.CheckoutTitle;

    public string MonthlyPrice => PriceText(CheckoutTier, BillingPlan.Monthly);

    public string AnnualPrice => PriceText(CheckoutTier, BillingPlan.Annual);

    public bool CanChooseMonthly => Billing.Find(CheckoutTier, BillingPlan.Monthly) is not null;

    public bool CanChooseAnnual => Billing.Find(CheckoutTier, BillingPlan.Annual) is not null;

    /// <summary>"31% 할인" for the dialog's tier, or empty when either price is unknown.</summary>
    public string CheckoutSavingText => SavingText(CheckoutTier);

    /// <summary>The same for the plan table's interval toggle, which the design ties to Pro.</summary>
    public string AnnualSavingText => SavingText(BillingTier.Pro);

    public bool HasAnnualSaving => AnnualSavingText.Length > 0;

    public bool HasCheckoutSaving => CheckoutSavingText.Length > 0;

    /// <summary>"Pro · 연간".</summary>
    public string CheckoutPlanLabel => string.Format(
        CultureInfo.CurrentCulture,
        AppStrings.CheckoutPlanLabelFormat,
        TierName(CheckoutTier),
        IsAnnualSelected ? AppStrings.BillingPlanAnnual : AppStrings.BillingPlanMonthly);

    public string CheckoutStartText => FormatDate(DateTimeOffset.Now);

    /// <summary>The first renewal: one interval from today, which is when Paddle bills next.</summary>
    public string CheckoutRenewText => FormatDate(IsAnnualSelected
        ? DateTimeOffset.Now.AddYears(1)
        : DateTimeOffset.Now.AddMonths(1));

    public string CheckoutConfirmLabel => IsCheckoutChange
        ? AppStrings.CheckoutChangeConfirm
        : string.Format(CultureInfo.CurrentCulture, AppStrings.CheckoutPayFormat, PriceMain);

    /// <summary>
    /// A change has nothing already chosen to confirm again, and the one it would confirm is the
    /// running subscription; a purchase needs an interval of the chosen tier on sale.
    /// </summary>
    public bool CanConfirmCheckout => !IsBusy
        && Billing.Find(CheckoutTier, SelectedPlan) is not null
        && !(IsCheckoutChange && CheckoutTier == Entitlement.PaidTier && Entitlement.Plan == SelectedPlan);

    public string CheckoutDoneTitle => string.Format(
        CultureInfo.CurrentCulture,
        checkoutWasChange ? AppStrings.CheckoutChangedTitleFormat : AppStrings.CheckoutDoneTitleFormat,
        TierName(Entitlement.State == EntitlementState.Active ? Entitlement.PaidTier : CheckoutTier));

    /// <summary>The line under the address in the settings card: the trial's days, or the renewal.</summary>
    public string PlanSubline => Entitlement.State == EntitlementState.Active && Entitlement.Until is { } until
        ? string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.AccountPlanSubRenewsFormat,
            TierName(Entitlement.PaidTier),
            FormatDate(until))
        : EntitlementSummary;

    /// <summary>The navy "Pro 구독 중" card, for a subscription that is paid up.</summary>
    public bool ShowSubscribedCard => !IsPhone && Entitlement.State == EntitlementState.Active;

    public string SubscribedTitle => string.Format(
        CultureInfo.CurrentCulture,
        AppStrings.BillingSubscribedFormat,
        TierName(Entitlement.PaidTier));

    /// <summary>"다음 결제일 2027. 9. 30. · ₩24,000 / 년" — the price only when the interval is known.</summary>
    public string SubscribedDetail
    {
        get
        {
            string date = Entitlement.Until is { } until ? FormatDate(until) : "—";
            if (Entitlement.Plan is not { } plan)
            {
                return AppStrings.BillingRowRenews + " " + date;
            }

            string price = string.Format(
                CultureInfo.CurrentCulture,
                plan == BillingPlan.Annual ? AppStrings.BillingPricePerYearFormat : AppStrings.BillingPricePerMonthFormat,
                PriceText(Entitlement.PaidTier, plan));
            return string.Format(CultureInfo.CurrentCulture, AppStrings.BillingSubscribedDetailFormat, date, price);
        }
    }

    /// <summary>"Premium으로 변경", offered to a Pro subscriber when Premium is on sale.</summary>
    public bool CanUpgradeToPremium => !IsPhone
        && Entitlement.State == EntitlementState.Active
        && Entitlement.PaidTier == BillingTier.Pro
        && Billing.CanChange
        && Billing.Sells(BillingTier.Premium);

    /// <summary>The trial's upgrade card in settings, and its row in the account popover.</summary>
    public bool ShowTrialUpgrade => !IsPhone && ShowUpgrade && Entitlement.State == EntitlementState.Trial;

    /// <summary>The same card after a lapse, with the lapse copy instead of a countdown.</summary>
    public bool ShowLapsedUpgrade => !IsPhone && ShowUpgrade && Entitlement.State == EntitlementState.Expired;

    public string TrialUpgradeTitle => AppStrings.BillingTrialBannerTitle(
        Entitlement.DaysRemaining(DateTimeOffset.UtcNow) ?? 0);

    /// <summary>
    /// Attachment storage: "1.2GB / 2GB", or "1.2GB 사용" on Premium, whose ceiling is a fair-use
    /// limit rather than a figure it is sold by. Shown once the server reports it and there is
    /// something to report — file sync on, or files stored from before.
    /// </summary>
    public bool HasStorage => Entitlement.QuotaBytes is not null
        && Entitlement.UsedBytes is not null
        && (Entitlement.CanSyncFiles || Entitlement.UsedBytes > 0);

    public string StorageText
    {
        get
        {
            long used = Entitlement.UsedBytes ?? 0;
            if (IsPaying && Entitlement.PaidTier == BillingTier.Premium)
            {
                return string.Format(CultureInfo.CurrentCulture, AppStrings.StorageUsedFormat, FormatBytes(used));
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.StorageUsageFormat,
                FormatBytes(used),
                FormatBytes(Entitlement.QuotaBytes ?? 0));
        }
    }

    /// <summary>The plan table's three columns, rebuilt from the state each time it is read.</summary>
    public IReadOnlyList<PlanColumn> PlanColumns
    {
        get
        {
            BillingTier? paid = IsPaying ? Entitlement.PaidTier : null;
            return
            [
                new PlanColumn(null, OpenCheckoutProCommand)
                {
                    Name = AppStrings.PlanFreeName,
                    Price = FormatMoney(new Money(Currency, 0)),
                    Per = AppStrings.PlanFreeNote,
                    IsCurrent = paid is null && Entitlement.State == EntitlementState.Expired,
                    Note = paid is not null ? string.Empty
                        : Entitlement.State == EntitlementState.Trial ? AppStrings.PlanAfterTrial
                        : Entitlement.State == EntitlementState.Expired ? AppStrings.PlanCurrent
                        : string.Empty,
                },
                PaidColumn(BillingTier.Pro, paid, OpenCheckoutProCommand),
                PaidColumn(BillingTier.Premium, paid, OpenCheckoutPremiumCommand),
            ];
        }
    }

    private PlanColumn PaidColumn(BillingTier tier, BillingTier? paid, IRelayCommand choose)
    {
        bool current = paid == tier;
        // Premium to Pro is not offered from the table: the design leaves it out, and the portal is
        // not where it happens either, so a Premium subscriber downgrades by asking support or at
        // renewal. Everything else not already current can be chosen when it is on sale.
        bool buy = !current
            && !(paid == BillingTier.Premium && tier == BillingTier.Pro)
            && Billing.Sells(tier)
            && (paid is null ? ShowUpgrade : Billing.CanChange);

        return new PlanColumn(tier, choose)
        {
            Name = TierName(tier),
            Price = PriceText(tier, SelectedPlan),
            Per = IsAnnualSelected ? PerText(tier) : AppStrings.PlanPerMonthly,
            IsHighlighted = paid is null ? tier == BillingTier.Pro : current,
            IsCurrent = current,
            CanBuy = buy,
            ButtonText = paid == BillingTier.Pro && tier == BillingTier.Premium ? AppStrings.PlanChange : AppStrings.PlanSelect,
            IsPrimary = tier == BillingTier.Pro || paid is not null,
            Note = current ? AppStrings.PlanCurrent : string.Empty,
        };
    }

    /// <summary>Opens the dialog on the tier the context suggests: Pro to buy, Premium to upgrade.</summary>
    [RelayCommand]
    private void OpenCheckout() =>
        OpenCheckoutOn(IsPaying ? BillingTier.Premium : BillingTier.Pro);

    [RelayCommand]
    private void OpenCheckoutPro() => OpenCheckoutOn(BillingTier.Pro);

    [RelayCommand]
    private void OpenCheckoutPremium() => OpenCheckoutOn(BillingTier.Premium);

    private void OpenCheckoutOn(BillingTier tier)
    {
        // Phones never sell: both stores require their own purchase flow for a subscription.
        if (IsPhone || !OffersSubscription || !Billing.Sells(tier))
        {
            return;
        }

        CheckoutTier = tier;
        EnsureSelectedPlanIsSold();
        checkoutWasChange = false;
        ErrorMessage = null;
        CheckoutStage = CheckoutStage.Form;
    }

    [RelayCommand]
    private void SelectCheckoutPro() => SelectCheckoutTier(BillingTier.Pro);

    [RelayCommand]
    private void SelectCheckoutPremium() => SelectCheckoutTier(BillingTier.Premium);

    private void SelectCheckoutTier(BillingTier tier)
    {
        if (!Billing.Sells(tier))
        {
            return;
        }

        CheckoutTier = tier;
        EnsureSelectedPlanIsSold();
    }

    /// <summary>
    /// The dialog's confirm. A purchase opens Paddle's checkout in the browser and waits for the
    /// server to report it; a change is made by the server at once and answered with the new state.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmCheckoutAsync()
    {
        if (!CanConfirmCheckout)
        {
            return;
        }

        if (IsCheckoutChange)
        {
            if (await ChangePlanAsync(CheckoutTier, SelectedPlan).ConfigureAwait(true))
            {
                checkoutWasChange = true;
                CheckoutStage = CheckoutStage.Done;
            }

            return;
        }

        await CheckoutAsync(CheckoutTier, SelectedPlan).ConfigureAwait(true);
        if (ErrorMessage is null && CheckoutStage == CheckoutStage.Form)
        {
            CheckoutStage = CheckoutStage.Waiting;
            StartCheckoutPoll();
        }
    }

    /// <summary>Opens a fresh checkout page: the first may have been closed, and links are per click.</summary>
    [RelayCommand]
    private Task ReopenCheckoutAsync() => CheckoutAsync(CheckoutTier, SelectedPlan);

    [RelayCommand]
    private void CloseCheckout()
    {
        StopCheckoutPoll();
        CheckoutStage = CheckoutStage.Closed;
    }

    /// <summary>
    /// Called when the app's window comes back to the front — typically from the browser, where the
    /// payment just finished. Re-reads the billing state if the dialog is waiting on one.
    /// </summary>
    public void NotifyActivated()
    {
        if (CheckoutStage == CheckoutStage.Waiting)
        {
            _ = RefreshBillingCommand.ExecuteAsync(null);
        }
    }

    private void StartCheckoutPoll()
    {
        StopCheckoutPoll();
        checkoutPoll = new CancellationTokenSource(CheckoutPollLimit);
        _ = PollCheckoutAsync(checkoutPoll.Token);
    }

    private void StopCheckoutPoll()
    {
        checkoutPoll?.Cancel();
        checkoutPoll?.Dispose();
        checkoutPoll = null;
    }

    private async Task PollCheckoutAsync(CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested && CheckoutStage == CheckoutStage.Waiting)
            {
                await Task.Delay(CheckoutPollInterval, cancellation).ConfigureAwait(true);
                await RefreshBillingAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Closed, done, or past the limit. The window's activation still re-reads the state.
        }
    }

    /// <summary>
    /// The success state follows the server, not the click: the dialog turns to "started" when the
    /// billing state says the tier it was opened on is paid for.
    /// </summary>
    private void CompleteCheckoutIfPaid()
    {
        if (CheckoutStage == CheckoutStage.Waiting
            && Entitlement.State == EntitlementState.Active
            && Entitlement.PaidTier == CheckoutTier)
        {
            StopCheckoutPoll();
            CheckoutStage = CheckoutStage.Done;
        }
    }

    /// <summary>Keeps the interval on one the chosen tier is actually sold at.</summary>
    private void EnsureSelectedPlanIsSold()
    {
        if (Billing.Find(CheckoutTier, SelectedPlan) is null)
        {
            BillingPlan other = SelectedPlan == BillingPlan.Annual ? BillingPlan.Monthly : BillingPlan.Annual;
            if (Billing.Find(CheckoutTier, other) is not null)
            {
                SelectedPlan = other;
            }
        }
    }

    /// <summary>The currency the interface language shows prices in: won in Korean, dollars in English.</summary>
    private static string Currency =>
        LocalizationService.Instance.Language == AppLanguage.English ? "USD" : "KRW";

    /// <summary>
    /// The price of an offer, from the server's list. A server from before the list sends no prices;
    /// only Pro existed then, and its two figures are still in the catalog. Anything else is "—".
    /// </summary>
    private string PriceText(BillingTier tier, BillingPlan plan)
    {
        if (Billing.Find(tier, plan)?.PriceIn(Currency) is { } price)
        {
            return FormatMoney(price);
        }

        if (tier == BillingTier.Pro && Billing.Find(tier, plan) is { Prices.Count: 0 })
        {
            return plan == BillingPlan.Annual ? AppStrings.BillingPriceAnnual : AppStrings.BillingPriceMonthly;
        }

        return "—";
    }

    /// <summary>"연간 · 월 ₩2,000꼴": the annual price spread over twelve months.</summary>
    private string PerText(BillingTier tier)
    {
        if (Billing.Find(tier, BillingPlan.Annual)?.PriceIn(Currency) is not { } annual)
        {
            return tier == BillingTier.Pro ? AppStrings.BillingPriceSubAnnual : AppStrings.BillingPlanAnnual;
        }

        // Won rounds to the hundred, as prices are written; dollars to the cent.
        long monthly = annual.Decimals == 0
            ? (long)Math.Round(annual.MinorUnits / 12m / 100m, MidpointRounding.AwayFromZero) * 100
            : (long)Math.Round(annual.MinorUnits / 12m, MidpointRounding.AwayFromZero);
        return string.Format(
            CultureInfo.CurrentCulture,
            AppStrings.PlanPerAnnualFormat,
            FormatMoney(annual with { MinorUnits = monthly }));
    }

    /// <summary>How much less the annual price is than twelve monthly ones, as "31% 할인".</summary>
    private string SavingText(BillingTier tier)
    {
        if (Billing.Find(tier, BillingPlan.Monthly)?.PriceIn(Currency) is not { MinorUnits: > 0 } monthly
            || Billing.Find(tier, BillingPlan.Annual)?.PriceIn(Currency) is not { } annual)
        {
            return string.Empty;
        }

        int percent = (int)Math.Round(100m - (annual.MinorUnits * 100m / (monthly.MinorUnits * 12m)), MidpointRounding.AwayFromZero);
        return percent > 0
            ? string.Format(CultureInfo.CurrentCulture, AppStrings.BillingSavingFormat, percent)
            : string.Empty;
    }

    /// <summary>"₩24,000", "$19.99" — the symbol for the two catalog currencies, the code otherwise.</summary>
    internal static string FormatMoney(Money money)
    {
        string number = money.Amount.ToString(money.Decimals == 0 ? "N0" : "N2", CultureInfo.InvariantCulture);
        return money.Currency.ToUpperInvariant() switch
        {
            "KRW" => "₩" + number,
            "USD" => "$" + number,
            _ => money.Currency.ToUpperInvariant() + " " + number,
        };
    }

    /// <summary>"350MB" under a gigabyte, "1.2GB" over it; binary units, as the quota is.</summary>
    internal static string FormatBytes(long bytes)
    {
        const double Gib = 1024d * 1024 * 1024;
        const double Mib = 1024d * 1024;
        return bytes >= Gib
            ? (bytes / Gib).ToString("0.#", CultureInfo.InvariantCulture) + "GB"
            : Math.Ceiling(bytes / Mib).ToString("0", CultureInfo.InvariantCulture) + "MB";
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    partial void OnCheckoutStageChanged(CheckoutStage value)
    {
        _ = value;
        foreach (string name in new[]
        {
            nameof(IsCheckoutOpen), nameof(IsCheckoutForm), nameof(IsCheckoutWaiting), nameof(IsCheckoutDone),
            nameof(CheckoutDoneTitle),
        })
        {
            OnPropertyChanged(name);
        }
    }

    partial void OnCheckoutTierChanged(BillingTier value)
    {
        _ = value;
        OnPropertyChanged(nameof(PriceMain));
        OnPropertyChanged(nameof(PriceSub));
        OnPropertyChanged(nameof(CheckoutLabel));
        RefreshCheckoutPresentation();
    }

    /// <summary>Re-raises everything this partial derives, from the three hooks that feed it.</summary>
    private void RefreshCheckoutPresentation()
    {
        CompleteCheckoutIfPaid();
        foreach (string name in new[]
        {
            nameof(IsPaying), nameof(IsCheckoutChange), nameof(ShowPlanTable), nameof(IsCheckoutPro),
            nameof(IsCheckoutPremium), nameof(CanChoosePro), nameof(CanChoosePremium), nameof(CheckoutTitle),
            nameof(MonthlyPrice), nameof(AnnualPrice), nameof(CanChooseMonthly), nameof(CanChooseAnnual),
            nameof(CheckoutSavingText), nameof(AnnualSavingText), nameof(HasAnnualSaving), nameof(HasCheckoutSaving),
            nameof(CheckoutPlanLabel), nameof(CheckoutRenewText), nameof(CheckoutConfirmLabel),
            nameof(CanConfirmCheckout), nameof(CheckoutDoneTitle), nameof(PlanSubline), nameof(ShowSubscribedCard),
            nameof(SubscribedTitle), nameof(SubscribedDetail), nameof(CanUpgradeToPremium), nameof(ShowTrialUpgrade),
            nameof(ShowLapsedUpgrade), nameof(TrialUpgradeTitle), nameof(HasStorage), nameof(StorageText),
            nameof(PlanColumns),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
