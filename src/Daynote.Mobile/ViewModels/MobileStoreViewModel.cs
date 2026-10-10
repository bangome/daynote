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
    private readonly Func<TimeSpan, Task> _delay;

    /// <summary>
    /// How long to wait between tries while a finished purchase is being confirmed: about 45 seconds
    /// in all, after which the page says, calmly, that it will finish on its own. A new purchase can
    /// take a few seconds to reach Apple's server API, and the notification Apple sends may get there
    /// first; both are tried meanwhile.
    /// </summary>
    public static IReadOnlyList<TimeSpan> ConfirmDelays { get; } =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15),
    ];

    /// <summary>The purchase still being confirmed when the tries ran out, picked up again on resume.</summary>
    private (string ProductId, string TransactionId, BillingTier Tier, bool WasOnApple)? _unconfirmed;

    /// <summary>Restored subscriptions the server confirmed during the restore in progress.</summary>
    private int _restoreConfirmed;

    /// <summary>
    /// The call recording each subscription right now, by its original transaction (or, for a
    /// renewal StoreKit 1 gives no original for, its product). StoreKit hands back one transaction per
    /// renewal, and the server reads the subscription's current state from any of them, so the rest
    /// wait for this call's answer instead of each making their own.
    /// </summary>
    private readonly Dictionary<string, Task<bool>> _recording = new(StringComparer.Ordinal);

    /// <summary>
    /// The server refused this session (a 401). Nothing is sent again until the next sign-in: every
    /// call would be refused the same way, and StoreKit would keep handing the same transactions back.
    /// </summary>
    private bool _heldUntilSignIn;

    /// <summary>
    /// Subscriptions the server has answered for in this run of the app — recorded, or refused for
    /// good. StoreKit 1 hands back every unfinished renewal on each launch and resume (a sandbox
    /// subscription renews many times a day), and each costs the account a slot of the server's limit;
    /// the rest of a subscription answered once are finished without asking again.
    /// </summary>
    private readonly HashSet<string> _settled = new(StringComparer.Ordinal);

    /// <summary>The first wait after a 429 or 5xx that named none; it doubles up to <see cref="MaxBackoff"/>.</summary>
    public static TimeSpan FirstBackoff { get; } = TimeSpan.FromSeconds(30);

    public static TimeSpan MaxBackoff { get; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The longest the page says "구매를 확인하는 중" before it settles on "will apply on its own",
    /// whatever the server answers meanwhile.
    /// </summary>
    public static TimeSpan ConfirmBudget { get; } = TimeSpan.FromSeconds(90);

    /// <summary>No transaction is sent before this: the server answered 429 or 5xx.</summary>
    private DateTimeOffset? _blockedUntil;

    private TimeSpan _backoff = FirstBackoff;

    /// <summary>The product whose purchase sheet is up, so its transaction is told from a replay.</summary>
    private string? _buying;

    /// <summary>The fresh purchase being confirmed right now.</summary>
    private string? _confirming;

    private readonly Func<DateTimeOffset> _clock;

    public MobileStoreViewModel(
        IStorePurchases store,
        AccountViewModel account,
        Func<CancellationToken, ValueTask<string?>> accountToken,
        Func<string, CancellationToken, ValueTask<(Entitlement Entitlement, BillingLinks Links)>> submit,
        Func<Task>? refreshBilling = null,
        Action<string>? openExternal = null,
        Func<TimeSpan, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(account);
        _store = store;
        Account = account;
        _accountToken = accountToken;
        _submit = submit;
        _refreshBilling = refreshBilling ?? (() => account.RefreshBillingCommand.ExecuteAsync(null));
        _openExternal = openExternal;
        _delay = delay ?? (wait => Task.Delay(wait));
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);

        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AccountViewModel.Billing) or nameof(AccountViewModel.Entitlement)
                or nameof(AccountViewModel.IsSignedIn) or nameof(AccountViewModel.SignedInEmail)
                or nameof(AccountViewModel.IsBillingUnavailable))
            {
                Refresh();
            }

            // Signed in at last: what StoreKit delivered before there was an account to record it
            // against (a renewal at launch, a purchase a crash interrupted) can be sent now.
            if (e.PropertyName is nameof(AccountViewModel.IsSignedIn) or nameof(AccountViewModel.SignedInEmail)
                && account.IsSignedIn)
            {
                _heldUntilSignIn = false;
                _store.RetryHeld();
            }
        };
        LocalizationService.Instance.LanguageChanged += (_, _) => Refresh();

        // Attached here, at composition, which is launch: transactions StoreKit held for the app —
        // a renewal, an approval that came later, a purchase a crash interrupted — are dealt with
        // as soon as there is an account to record them against.
        _store.TransactionHandler = HandleTransactionAsync;

        // A different App Store account, or a different country: the prices on the page are the old
        // storefront's, and the sheet would charge the new one's.
        _store.StorefrontChanged += (_, _) => _ = LoadProductsAsync();
    }

    public AccountViewModel Account { get; }

    public MobileStrings Strings => MobileStrings.Instance;

    [ObservableProperty]
    private bool _isAnnual = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingProducts;

    /// <summary>A purchase StoreKit completed, waiting for the server to confirm it.</summary>
    [ObservableProperty]
    private bool _isConfirming;

    /// <summary>A calm outcome: started, restored, waiting for approval.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    private BillingLinks Billing => Account.Billing;

    private Entitlement Entitlement => Account.Entitlement;

    public bool IsSignedIn => Account.IsSignedIn;

    public bool ShowsSignInPrompt => !IsSignedIn;

    /// <summary>
    /// Signed in, but the billing state could not be read and there is nothing to show instead: the
    /// page says so and offers to try again, rather than standing empty.
    /// </summary>
    public bool ShowsBillingError => IsSignedIn && Account.IsBillingUnavailable && !ShowsPlans && !IsManagedElsewhere;

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
            CanBuy = !current && priced is not null && Billing.AppleCanPurchase && !IsBusy && !IsConfirming,
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

    /// <summary>
    /// Asks StoreKit for every product on sale, each time the page opens and whenever the storefront
    /// changes. Nothing is kept from an earlier ask: a price read in one storefront and charged in
    /// another (seen in TestFlight: $2.49 on the card, ₩2,900 on the sheet) is the one thing this
    /// page must not show.
    /// </summary>
    private async Task LoadProductsAsync()
    {
        string[] wanted = [.. (Billing.AppleProducts ?? []).Select(product => product.ProductId)];
        if (wanted.Length == 0)
        {
            _products.Clear();
            Refresh();
            return;
        }

        IsLoadingProducts = true;
        try
        {
            IReadOnlyList<StoreProduct> loaded = await _store.LoadProductsAsync(wanted, CancellationToken.None).ConfigureAwait(true);
            _products.Clear();
            foreach (StoreProduct product in loaded)
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
            StorePurchaseResult result;
            _buying = product.ProductId;
            try
            {
                result = await _store.PurchaseAsync(product.ProductId, token, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                _buying = null;
            }

            switch (result.Status)
            {
                case StorePurchaseStatus.Purchased when result.TransactionId is { } id && _confirmed.Contains(id):
                    StatusMessage = Done(tier, wasOnApple);
                    break;
                case StorePurchaseStatus.Purchased when result.TransactionId is { } pendingId:
                    // StoreKit took the money; the server has not confirmed it yet. Never an error:
                    // the purchase succeeded, and it is confirmed here, or on its own, shortly.
                    await ConfirmAsync(product.ProductId, pendingId, tier, wasOnApple).ConfigureAwait(true);
                    break;
                case StorePurchaseStatus.Purchased:
                    StatusMessage = MobileStrings.Get("StoreConfirmLater");
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
    /// Waits for the server to have a purchase StoreKit has completed: sends the held transaction
    /// again after each delay, and reads the billing state too, which picks up an activation that
    /// came through Apple's notification instead. After the last try the page says, without alarm,
    /// that it will finish on its own; the transaction stays with StoreKit, and coming back to the
    /// app (<see cref="NotifyResumed"/>) tries once more.
    /// </summary>
    /// <remarks>
    /// Bounded twice over: by <see cref="ConfirmDelays"/>, and by <see cref="ConfirmBudget"/> of
    /// wall-clock time, so slow answers cannot stretch it. A 429 asking for longer than the budget has
    /// left ends it at once — waiting it out behind a spinner would tell the person nothing.
    /// </remarks>
    private async Task ConfirmAsync(string productId, string transactionId, BillingTier tier, bool wasOnApple)
    {
        IsConfirming = true;
        StatusMessage = MobileStrings.Get("StoreConfirming");
        _confirming = transactionId;
        DateTimeOffset deadline = _clock() + ConfirmBudget;
        try
        {
            foreach (TimeSpan wait in ConfirmDelays)
            {
                if (_blockedUntil is { } until && until >= deadline)
                {
                    break;
                }

                await _delay(wait).ConfigureAwait(true);
                if (await IsConfirmedAsync(productId, transactionId).ConfigureAwait(true))
                {
                    _unconfirmed = null;
                    StatusMessage = Done(tier, wasOnApple);
                    return;
                }

                if (_clock() >= deadline)
                {
                    break;
                }
            }

            _unconfirmed = (productId, transactionId, tier, wasOnApple);
            StatusMessage = MobileStrings.Get("StoreConfirmLater");
        }
        finally
        {
            _confirming = null;
            IsConfirming = false;
        }
    }

    private async Task<bool> IsConfirmedAsync(string productId, string transactionId)
    {
        _store.RetryHeld();
        if (_confirmed.Contains(transactionId))
        {
            return true;
        }

        await _refreshBilling().ConfigureAwait(true);
        return _confirmed.Contains(transactionId)
            || (Billing.Provider == BillingProvider.Apple
                && string.Equals(Billing.AppleProductId, productId, StringComparison.Ordinal)
                && Account.IsPaying);
    }

    private static string Done(BillingTier tier, bool wasOnApple) =>
        MobileStrings.Format(wasOnApple ? "StoreChangedFormat" : "StoreDoneFormat", AccountViewModel.TierName(tier));

    /// <summary>
    /// The app is back in front: a purchase still unconfirmed is tried once more, and anything
    /// StoreKit held is sent.
    /// </summary>
    public async Task NotifyResumed()
    {
        if (!IsSignedIn)
        {
            return;
        }

        _store.RetryHeld();
        if (_unconfirmed is { } pending && !IsConfirming
            && await IsConfirmedAsync(pending.ProductId, pending.TransactionId).ConfigureAwait(true))
        {
            _unconfirmed = null;
            StatusMessage = Done(pending.Tier, pending.WasOnApple);
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
                StatusMessage = MobileStrings.Get(_restoreConfirmed > 0 ? "StoreRestoreDone" : "StoreConfirmLater");
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
    /// <remarks>
    /// <para>
    /// The person's own purchase comes first. While one is on the sheet, being confirmed, or still
    /// unconfirmed, old transactions StoreKit replays are held back so they cannot spend the
    /// server's per-account limit (60 in 15 minutes) that the purchase's confirmation needs.
    /// </para>
    /// <para>
    /// Replays are sent once per subscription (by original transaction, or for a renewal StoreKit 1
    /// gives no original for, by product): a transaction whose subscription is being sent takes that
    /// call's answer, and one whose subscription was answered earlier in this run is finished without
    /// asking. Nothing is sent while signed out, or after the server refused the session, until the
    /// next sign-in (which calls <see cref="IStorePurchases.RetryHeld"/>); nor, after a 429 or 5xx,
    /// until its <c>Retry-After</c>, or a wait starting at <see cref="FirstBackoff"/> and doubling.
    /// </para>
    /// </remarks>
    internal async Task<bool> HandleTransactionAsync(StoreTransaction transaction)
    {
        if (!IsSignedIn || _heldUntilSignIn)
        {
            return false;
        }

        bool fresh = !transaction.IsRestore
            && (string.Equals(_buying, transaction.ProductId, StringComparison.Ordinal)
                || string.Equals(_confirming, transaction.TransactionId, StringComparison.Ordinal)
                || string.Equals(_unconfirmed?.TransactionId, transaction.TransactionId, StringComparison.Ordinal));
        if (IsBlocked)
        {
            return false;
        }

        if (fresh)
        {
            return await SubmitAsync(transaction, fresh: true).ConfigureAwait(true);
        }

        string subscription = transaction.OriginalTransactionId is { Length: > 0 } original ? original
            : transaction.ProductId is { Length: > 0 } product ? product
            : transaction.TransactionId;
        if (_settled.Contains(subscription))
        {
            _confirmed.Add(transaction.TransactionId);
            return true;
        }

        if (_buying is not null || _confirming is not null || _unconfirmed is not null)
        {
            return false;
        }

        if (_recording.TryGetValue(subscription, out Task<bool>? sending))
        {
            bool recorded = await sending.ConfigureAwait(true);
            if (recorded)
            {
                _confirmed.Add(transaction.TransactionId);
            }

            return recorded;
        }

        Task<bool> send = SubmitAsync(transaction, fresh: false);
        _recording[subscription] = send;
        try
        {
            bool finished = await send.ConfigureAwait(true);
            if (finished)
            {
                _settled.Add(subscription);
            }

            return finished;
        }
        finally
        {
            _recording.Remove(subscription);
        }
    }

    private bool IsBlocked => _blockedUntil is { } until && _clock() < until;

    /// <summary>A 429 or 5xx: nothing more is sent until the server's wait, or the next of ours, is over.</summary>
    private void BackOff(TimeSpan? retryAfter)
    {
        _blockedUntil = _clock() + (retryAfter ?? _backoff);
        _backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));
    }

    private async Task<bool> SubmitAsync(StoreTransaction transaction, bool fresh)
    {
        try
        {
            (Entitlement entitlement, BillingLinks links) = await _submit(transaction.TransactionId, CancellationToken.None)
                .ConfigureAwait(true);
            _blockedUntil = null;
            _backoff = FirstBackoff;
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
            System.Diagnostics.Trace.TraceInformation($"App Store transaction {transaction.TransactionId} belongs to another account; finished.");
            if (IsBusy)
            {
                ErrorMessage = MobileStrings.Get("StoreOtherAccount");
            }

            return true;
        }
        catch (AccountException failure) when (failure.Failure == AccountFailure.PurchaseRefused)
        {
            // Never going to be accepted; finishing it stops it coming back on every launch.
            System.Diagnostics.Trace.TraceInformation($"App Store transaction {transaction.TransactionId} refused for good; finished.");
            if (IsBusy)
            {
                ErrorMessage = MobileStrings.Get("StoreFailed");
            }

            return true;
        }
        catch (AccountException failure) when (failure.Failure is AccountFailure.SessionExpired or AccountFailure.InvalidCredentials)
        {
            // A 401: held, all of them, until someone signs in again. A rejected refresh token also
            // ends the session, which is what brings the sign-in prompt up on this page.
            _heldUntilSignIn = true;
            if (failure.Failure == AccountFailure.SessionExpired)
            {
                await Account.EndRejectedSessionAsync().ConfigureAwait(true);
            }

            return false;
        }
        catch (AccountException failure) when (failure.Failure == AccountFailure.RateLimited
            || (failure.Failure == AccountFailure.ServerError && !fresh))
        {
            // A fresh purchase's 5xx is mostly Apple not knowing it yet (purchase_pending), which
            // its own confirmation schedule retries; a 429 is the account's limit, whoever asked.
            BackOff(failure.RetryAfter);
            return false;
        }
        catch (AccountException)
        {
            return false;
        }
    }

    partial void OnIsAnnualChanged(bool value) => Refresh();

    partial void OnIsBusyChanged(bool value) => Refresh();

    partial void OnIsConfirmingChanged(bool value) => Refresh();

    partial void OnIsLoadingProductsChanged(bool value) => OnPropertyChanged(nameof(HasNoPrices));

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void Refresh()
    {
        foreach (string name in new[]
        {
            nameof(IsSignedIn), nameof(ShowsSignInPrompt), nameof(ShowsBillingError), nameof(IsManagedElsewhere), nameof(ShowsPlans),
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
