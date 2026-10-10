namespace Daynote.Mobile.Platform;

/// <summary>
/// The App Store's purchase sheet and payment queue, behind the seam the shared phone code can see
/// (docs/CLOUD_SYNC.md §14.8). Implemented by the iOS head over StoreKit; Android supplies none, and
/// the phone then shows nothing to buy, because Google allows an app to honour what was bought
/// elsewhere and the iPhone is the only head that has to sell.
/// </summary>
/// <remarks>
/// The rules the implementation keeps, because StoreKit punishes breaking them:
/// <list type="bullet">
/// <item>One payment-queue observer, attached at launch, so a purchase interrupted by a crash, an
/// approval that arrives later (Ask to Buy), and every renewal reach the app whenever it next runs.</item>
/// <item>A purchased or restored transaction is handed to <see cref="TransactionHandler"/> and finished
/// only when that answers true — when the server has recorded it. An unfinished one comes back on the
/// next launch, which is the retry.</item>
/// <item>The purchase carries the account id as its application username, which StoreKit turns into the
/// transaction's <c>appAccountToken</c>; that is how the server knows whose purchase it is.</item>
/// </list>
/// </remarks>
public interface IStorePurchases
{
    /// <summary>
    /// The App Store storefront changed — another App Store account, or another country. Prices read
    /// before it are the old storefront's.
    /// </summary>
    event EventHandler? StorefrontChanged;

    /// <summary>False when Screen Time or a profile forbids purchases on this device.</summary>
    bool CanMakePayments { get; }

    /// <summary>
    /// Records a transaction with the server; answers true to finish it, false to leave it for StoreKit
    /// to deliver again. Transactions that arrive before a handler is set are held until one is.
    /// Called on the UI thread.
    /// </summary>
    Func<StoreTransaction, Task<bool>>? TransactionHandler { get; set; }

    /// <summary>
    /// Sends again the transactions the handler declined — signed out at launch, offline — now
    /// that it might accept them. StoreKit 1 itself only hands them back on the next launch.
    /// </summary>
    void RetryHeld();

    /// <summary>The products StoreKit knows, with prices localized for this storefront.</summary>
    Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken);

    /// <summary>
    /// Shows the App Store's purchase sheet and waits for the outcome — for a purchase, until the
    /// handler has dealt with the transaction, so the billing state is already up to date.
    /// </summary>
    Task<StorePurchaseResult> PurchaseAsync(string productId, string accountToken, CancellationToken cancellationToken);

    /// <summary>
    /// Asks StoreKit for this Apple ID's earlier purchases and waits until the handler has dealt with
    /// each. Answers how many subscriptions there were — not transactions: StoreKit returns one per
    /// renewal, and those are sent once per subscription.
    /// </summary>
    Task<int> RestoreAsync(CancellationToken cancellationToken);

    /// <summary>The App Store's own page for this Apple ID's subscriptions, where they are changed and cancelled.</summary>
    void OpenSubscriptionManagement();
}

/// <summary>A product as StoreKit describes it: the localized title and the price for this storefront.</summary>
public sealed record StoreProduct(string ProductId, string Title, string PriceText);

/// <summary>
/// A purchased or restored transaction, as the server is told about it. <paramref name="OriginalTransactionId"/>
/// names the subscription it belongs to, where StoreKit says; renewals of one subscription share it.
/// </summary>
public sealed record StoreTransaction(string TransactionId, string ProductId, bool IsRestore, string? OriginalTransactionId = null);

public enum StorePurchaseStatus
{
    Purchased,

    /// <summary>The person closed the sheet. Not an error, and not worth a message.</summary>
    Cancelled,

    /// <summary>Waiting on someone else's approval (Ask to Buy). It completes on its own, later.</summary>
    Pending,

    Failed,
}

/// <summary>
/// The outcome of one purchase sheet. <paramref name="TransactionId"/> is the id the handler was given
/// for this purchase, so the caller can tell whether this transaction — not another one delivered
/// meanwhile — reached the server.
/// </summary>
public sealed record StorePurchaseResult(StorePurchaseStatus Status, string? Message = null, string? TransactionId = null);
