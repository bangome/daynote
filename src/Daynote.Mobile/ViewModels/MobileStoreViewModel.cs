using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Account;
using Daynote.App.Localization;
using Daynote.Core.Sync;
using Daynote.Mobile.Platform;

namespace Daynote.Mobile.ViewModels;

/// <summary>One paid plan on the iPhone's plans page: Pro or Premium, at the interval the toggle shows.</summary>
public sealed class StorePlanCard
{
    internal StorePlanCard(BillingTier tier, IRelayCommand buy)
    {
        Tier = tier;
        BuyCommand = buy;
    }

    public BillingTier Tier { get; }

    public required string Name { get; init; }

    /// <summary>What it holds: "이미지·파일 동기화 2GB".</summary>
    public required string Detail { get; init; }

    /// <summary>The App Store's localized price, per period: "₩2,900 / 월".</summary>
    public required string PriceText { get; init; }

    /// <summary>
    /// Title, length and price, together beside the button, as App Store guideline 3.1.2 requires
    /// of an auto-renewing subscription: "Daynote Pro (월간) · 1개월 · ₩2,900".
    /// </summary>
    public required string TermsText { get; init; }

    /// <summary>The App Store product this account is subscribed to.</summary>
    public bool IsCurrent { get; init; }

    public bool CanBuy { get; init; }

    public required string ButtonText { get; init; }

    /// <summary>Drawn on the tinted ground: the plan in use, or Premium as the recommendation.</summary>
    public bool IsHighlighted { get; init; }

    public IRelayCommand BuyCommand { get; }
}

/// <summary>
/// The iPhone's plans page: what the account has, the two paid plans at the App Store's own prices,
/// and the purchase, restore and management that come with selling through In-App Purchase
/// (docs/CLOUD_SYNC.md §14.8, App Store guidelines 3.1.1 and 3.1.2).
/// </summary>
/// <remarks>
/// <para>
/// StoreKit sells; the server decides. A purchase is shown as done only once the server has read it
/// from Apple and answered with the new billing state — never on the sheet closing — and the
/// transaction is finished only then, so one that could not be confirmed comes back on the next
/// launch and is tried again.
/// </para>
/// <para>
/// An account whose subscription was bought on the desktop is shown as subscribed and "managed on
/// your computer", with no price, link or button that points anywhere else: the App Store forbids
/// steering, and selling a second subscription beside it would bill twice. The server says when that
/// is so (<see cref="BillingLinks.AppleCanPurchase"/>), and is asked again just before every purchase.
/// </para>
/// </remarks>
public sealed partial class MobileStoreViewModel : ObservableObject
{
    /// <summary>Where the App Store manages this Apple ID's subscriptions.</summary>
    public const string ManageSubscriptionsUrl = "https://apps.apple.com/account/subscriptions";

    private readonly IStorePurchases _store;
    private readonly Func<CancellationToken, ValueTask<string?>> _accountToken;
    private readonly Func<string, CancellationToken, ValueTask<(Entitlement Entitlement, BillingLinks Links)>> _submit;
    private readonly Func<Task> _refreshBilling;
    private readonly Dictionary<string, StoreProduct> _products = new(StringComparer.Ordinal);

    /// <summary>
    /// The transactions the server has recorded, by the id sent: how a purchase knows its own
    /// transaction went through, rather than whichever one the handler saw last.
    /// </summary>
    private readonly HashSet<string> _confirmed = new(StringComparer.Ordinal);

    /// <summary>Apple's standard licence agreement, which the app uses as its EULA.</summary>
    public const string StandardEulaUrl = "https://www.apple.com/legal/internet-services/itunes/dev/stdeula/";

    private readonly Action<string>? _openExternal;

    /// <summary>Restored subscriptions the server confirmed during the restore in progress.</summary>
    private int _restoreConfirmed;

    public MobileStoreViewModel(
        IStorePurchases store,
        AccountViewModel account,
        Func<CancellationToken, ValueTask<string?>> accountToken,
        Func<string, CancellationToken, ValueTask<(Entitlement Entitlement, BillingLinks Links)>> submit,
        Func<Task>? refreshBilling = null,
        Action<string>? openExternal = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(account);
        _store = store;
        Account = account;
        _accountToken = accountToken;
        _submit = submit;
        _refreshBilling = refreshBilling ?? (() => account.RefreshBillingCommand.ExecuteAsync(null));
        _openExternal = openExternal;

        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AccountViewModel.Billing) or nameof(AccountViewModel.Entitlement)
                or nameof(AccountViewModel.IsSignedIn) or nameof(AccountViewModel.SignedInEmail))
            {
                Refresh();
            }

            // Signed in at last: what StoreKit delivered before there was an account to record it
            // against (a renewal at launch, a purchase a crash interrupted) can be sent now.
            if (e.PropertyName is nameof(AccountViewModel.IsSignedIn) or nameof(AccountViewModel.SignedInEmail)
                && account.IsSignedIn)
            {
                _store.RetryHeld();
            }
        };
        LocalizationService.Instance.LanguageChanged += (_, _) => Refresh();

        // Attached here, at composition, which is launch: transactions StoreKit held for the app —
        // a renewal, an approval that came later, a purchase a crash interrupted — are dealt with
        // as soon as there is an account to record them against.
        _store.TransactionHandler = HandleTransactionAsync;
    }

    public AccountViewModel Account { get; }

    public MobileStrings Strings => MobileStrings.Instance;

    [ObservableProperty]
    private bool _isAnnual = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingProducts;

    /// <summary>A calm outcome: started, restored, waiting for approval.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    private BillingLinks Billing => Account.Billing;

    private Entitlement Entitlement => Account.Entitlement;

    public bool IsSignedIn => Account.IsSignedIn;

    public bool ShowsSignInPrompt => !IsSignedIn;

    /// <summary>Subscribed through Paddle on the desktop: shown as such, with nothing to buy.</summary>
    public bool IsManagedElsewhere => IsSignedIn
        && Billing.Provider == BillingProvider.Paddle
        && !Billing.AppleCanPurchase
        && Entitlement.State is EntitlementState.Active or EntitlementState.Grace;

    /// <summary>The plan cards, the toggle and the purchase buttons.</summary>
    public bool ShowsPlans => IsSignedIn && !IsManagedElsewhere && (Billing.AppleProducts?.Count ?? 0) > 0;

    /// <summary>Subscribed through the App Store, so "구독 관리" has something to manage.</summary>
    public bool HasAppleSubscription => IsSignedIn && Billing.Provider == BillingProvider.Apple && Entitlement.HasSubscribed;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsMonthly => !IsAnnual;

    public bool HasDuplicate => IsSignedIn && Billing.DuplicateSubscription;

    /// <summary>"Pro", "Premium", "체험 중" or "무료": the plan pill.</summary>
    public string CurrentPlanName => IsSignedIn ? Account.PlanBadge : AppStrings.AccountPlanFree;

    /// <summary>"Premium · 연간", or what the free plan is.</summary>
    public string CurrentPlanTitle => !IsSignedIn ? CurrentPlanName : Entitlement.State switch
    {
        EntitlementState.Active or EntitlementState.Grace => Account.SubscriptionPlanText,
        // The trial is Pro-level: the title says what is open, the pill that it is a trial.
        EntitlementState.Trial => AccountViewModel.TierName(BillingTier.Pro),
        _ => CurrentPlanName,
    };

    /// <summary>When it renews, when the trial ends, or what the free plan includes.</summary>
    public string CurrentPlanDetail
    {
        get
        {
            if (!IsSignedIn)
            {
                return MobileStrings.Get("StoreFreeDetail");
            }

            string date = Entitlement.Until is { } until
                ? until.ToLocalTime().ToString("D", LocalizationService.Instance.Culture)
                : string.Empty;
            return Entitlement.State switch
            {
                EntitlementState.Active when date.Length > 0 => MobileStrings.Format("StoreRenewsFormat", date),
                EntitlementState.Grace => AppStrings.BillingStateGrace,
                EntitlementState.Trial => Account.EntitlementSummary,
                _ => MobileStrings.Get("StoreFreeDetail"),
            };
        }
    }

    public bool HasStorage => IsSignedIn && Account.HasStorage;

    public string StorageText => MobileStrings.Format("StoreStorageFormat", Account.StorageText);

    /// <summary>How full the quota is, for the bar; none on Premium, whose ceiling is not a figure it is sold by.</summary>
    public double StorageFraction => Entitlement is { QuotaBytes: > 0 } e && !(e.PaidTier == BillingTier.Premium && Account.IsPaying)
        ? Math.Clamp((double)(e.UsedBytes ?? 0) / e.QuotaBytes.Value, 0, 1)
        : 0;

    public bool ShowsStorageBar => HasStorage && StorageFraction > 0;

    public IReadOnlyList<PlanComparisonRow> PlanRows => PlanComparison.Rows;

    public StorePlanCard ProCard => Card(BillingTier.Pro, BuyProCommand);

    public StorePlanCard PremiumCard => Card(BillingTier.Premium, BuyPremiumCommand);

    /// <summary>True when StoreKit gave no price for anything on sale: the App Store is unreachable.</summary>
    public bool HasNoPrices => ShowsPlans && !IsLoadingProducts && _products.Count == 0;

    private BillingPlan SelectedPlan => IsAnnual ? BillingPlan.Annual : BillingPlan.Monthly;

    private StorePlanCard Card(BillingTier tier, IRelayCommand buy)
    {
        AppStoreProduct? product = Billing.FindAppleProduct(tier, SelectedPlan);
        StoreProduct? priced = product is null ? null : _products.GetValueOrDefault(product.ProductId);
        bool current = product is not null && HasAppleSubscription && Account.IsPaying
            && string.Equals(Billing.AppleProductId, product.ProductId, StringComparison.Ordinal);
        bool onApple = HasAppleSubscription && Account.IsPaying;

        string name = AccountViewModel.TierName(tier);
        string interval = IsAnnual ? AppStrings.BillingPlanAnnual : AppStrings.BillingPlanMonthly;
        string price = priced is null
            ? MobileStrings.Get("StorePriceLoading")
            : string.Format(
                CultureInfo.CurrentCulture,
                IsAnnual ? AppStrings.BillingPricePerYearFormat : AppStrings.BillingPricePerMonthFormat,
                priced.PriceText);
        string title = priced?.Title is { Length: > 0 } t ? t : $"Daynote {name} ({interval})";

        return new StorePlanCard(tier, buy)
        {
            Name = name,
            Detail = MobileStrings.Get(tier == BillingTier.Premium ? "StorePremiumDetail" : "StoreProDetail"),
            PriceText = price,
            TermsText = MobileStrings.Format(
                "StoreTermFormat",
                title,
                MobileStrings.Get(IsAnnual ? "StoreLengthYear" : "StoreLengthMonth"),
                priced?.PriceText ?? "—"),
            IsCurrent = current,
            CanBuy = !current && priced is not null && Billing.AppleCanPurchase && !IsBusy,
            ButtonText = current
                ? AppStrings.PlanInUse
                : !onApple ? MobileStrings.Get("StoreSubscribe")
                : tier == BillingTier.Premium && Entitlement.PaidTier == BillingTier.Pro ? MobileStrings.Get("StoreUpgrade")
                : MobileStrings.Get("StoreSwitch"),
            IsHighlighted = onApple ? Entitlement.PaidTier == tier : tier == BillingTier.Premium,
        };
    }

    /// <summary>
    /// Reads the billing state again and asks StoreKit for the prices of what is on sale. Called when
    /// the page opens; StoreKit caches nothing for us, and the prices are per storefront.
    /// </summary>
    [RelayCommand]
    public async Task OpenAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        if (IsSignedIn)
        {
            _store.RetryHeld();
            await _refreshBilling().ConfigureAwait(true);
        }

        await LoadProductsAsync().ConfigureAwait(true);
    }

    private async Task LoadProductsAsync()
    {
        string[] wanted = [.. (Billing.AppleProducts ?? []).Select(product => product.ProductId)
            .Where(id => !_products.ContainsKey(id))];
        if (wanted.Length == 0)
        {
            Refresh();
            return;
        }

        IsLoadingProducts = true;
        try
        {
            foreach (StoreProduct product in await _store.LoadProductsAsync(wanted, CancellationToken.None).ConfigureAwait(true))
            {
                _products[product.ProductId] = product;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An unreachable App Store is said once, on the page, rather than as a failure banner:
            // the cards keep their place and say the price is loading.
            System.Diagnostics.Trace.TraceWarning($"StoreKit products failed: {exception.Message}");
        }
        finally
        {
            IsLoadingProducts = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void SelectMonthly() => IsAnnual = false;

    [RelayCommand]
    private void SelectAnnual() => IsAnnual = true;

    [RelayCommand]
    private Task BuyPro() => BuyAsync(BillingTier.Pro);

    [RelayCommand]
    private Task BuyPremium() => BuyAsync(BillingTier.Premium);

    private async Task BuyAsync(BillingTier tier)
    {
        if (IsBusy || !IsSignedIn)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            // Asked again now rather than trusted from when the page opened: a subscription bought on
            // the desktop in the meantime must stop this one, and the server is the one that knows.
            await _refreshBilling().ConfigureAwait(true);
            if (!Billing.AppleCanPurchase || Billing.FindAppleProduct(tier, SelectedPlan) is not { } product)
            {
                return;
            }

            if (!_store.CanMakePayments)
            {
                ErrorMessage = MobileStrings.Get("StoreCannotPay");
                return;
            }

            if (await _accountToken(CancellationToken.None).ConfigureAwait(true) is not { Length: > 0 } token)
            {
                return;
            }

            bool wasOnApple = HasAppleSubscription && Account.IsPaying;
            StorePurchaseResult result = await _store.PurchaseAsync(product.ProductId, token, CancellationToken.None).ConfigureAwait(true);
            switch (result.Status)
            {
                case StorePurchaseStatus.Purchased when result.TransactionId is { } id && _confirmed.Contains(id):
                    StatusMessage = MobileStrings.Format(
                        wasOnApple ? "StoreChangedFormat" : "StoreDoneFormat",
                        AccountViewModel.TierName(tier));
                    break;
                case StorePurchaseStatus.Purchased:
                    ErrorMessage ??= MobileStrings.Get("StoreVerifyFailed");
                    break;
                case StorePurchaseStatus.Pending:
                    StatusMessage = MobileStrings.Get("StorePending");
                    break;
                case StorePurchaseStatus.Failed:
                    ErrorMessage = result.Message is { Length: > 0 } message ? message : MobileStrings.Get("StoreFailed");
                    break;
                default:
                    // Cancelled: the person closed the sheet, which needs no comment.
                    break;
            }
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    /// <summary>
    /// "구매 복원": StoreKit hands back this Apple ID's purchases, each goes to the server like a new
    /// one, and the server decides which account it belongs to. Required by App Review for any app
    /// that sells a subscription.
    /// </summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        if (!IsSignedIn)
        {
            ErrorMessage = MobileStrings.Get("StoreSignInTitle");
            return;
        }

        IsBusy = true;
        _restoreConfirmed = 0;
        try
        {
            int restored = await _store.RestoreAsync(CancellationToken.None).ConfigureAwait(true);
            if (restored == 0)
            {
                StatusMessage = MobileStrings.Get("StoreRestoreNone");
            }
            else if (ErrorMessage is null)
            {
                StatusMessage = MobileStrings.Get(_restoreConfirmed > 0 ? "StoreRestoreDone" : "StoreVerifyFailed");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorMessage = exception.Message is { Length: > 0 } message ? message : MobileStrings.Get("StoreFailed");
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    /// <summary>Set by the shell: takes the person to the account page, where signing in is.</summary>
    public Action? RequestSignIn { get; set; }

    [RelayCommand]
    private void SignIn() => RequestSignIn?.Invoke();

    /// <summary>"구독 관리": the App Store's own page, where an Apple subscription is changed or cancelled.</summary>
    [RelayCommand]
    private void Manage() => _store.OpenSubscriptionManagement();

    [RelayCommand]
    private void OpenTerms()
    {
        // The App Store's own standard EULA: the terms an In-App Purchase is sold under (guideline
        // 3.1.2), which the site's terms of service are not.
        if (_openExternal is { } open)
        {
            open(StandardEulaUrl);
        }
        else
        {
            Account.OpenTermsCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void OpenPrivacy() => Account.OpenPrivacyCommand.Execute(null);

    /// <summary>
    /// Records a transaction with the server. True finishes it: recorded, or set aside as another
    /// account's — nothing this device does will change either. False leaves it with StoreKit, which
    /// delivers it again on the next launch: signed out, offline, or the server could not reach Apple.
    /// </summary>
    internal async Task<bool> HandleTransactionAsync(StoreTransaction transaction)
    {
        if (!IsSignedIn)
        {
            return false;
        }

        try
        {
            (Entitlement entitlement, BillingLinks links) = await _submit(transaction.TransactionId, CancellationToken.None)
                .ConfigureAwait(true);
            Account.Entitlement = entitlement;
            Account.Billing = links;
            _confirmed.Add(transaction.TransactionId);
            if (transaction.IsRestore)
            {
                _restoreConfirmed++;
            }

            return true;
        }
        catch (AccountException failure) when (failure.Failure == AccountFailure.PurchaseBelongsToAnotherAccount)
        {
            // Said only when someone is buying or restoring here. A renewal of another account's
            // subscription arriving in the background is that account's business, and the
            // server's notifications keep its row current.
            if (IsBusy)
            {
                ErrorMessage = MobileStrings.Get("StoreOtherAccount");
            }

            return true;
        }
        catch (AccountException failure) when (failure.Failure == AccountFailure.PurchaseRefused)
        {
            // Never going to be accepted; finishing it stops it coming back on every launch.
            if (IsBusy)
            {
                ErrorMessage = MobileStrings.Get("StoreFailed");
            }

            return true;
        }
        catch (AccountException)
        {
            return false;
        }
    }

    partial void OnIsAnnualChanged(bool value) => Refresh();

    partial void OnIsBusyChanged(bool value) => Refresh();

    partial void OnIsLoadingProductsChanged(bool value) => OnPropertyChanged(nameof(HasNoPrices));

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void Refresh()
    {
        foreach (string name in new[]
        {
            nameof(IsSignedIn), nameof(ShowsSignInPrompt), nameof(IsManagedElsewhere), nameof(ShowsPlans),
            nameof(HasAppleSubscription), nameof(IsMonthly), nameof(HasDuplicate), nameof(CurrentPlanName),
            nameof(CurrentPlanTitle), nameof(CurrentPlanDetail), nameof(HasStorage), nameof(StorageText),
            nameof(StorageFraction), nameof(ShowsStorageBar), nameof(ProCard), nameof(PremiumCard),
            nameof(HasNoPrices),
        })
        {
            OnPropertyChanged(name);
        }
    }
}
