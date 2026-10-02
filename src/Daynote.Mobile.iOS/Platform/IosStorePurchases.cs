using Avalonia.Threading;
using Daynote.Mobile.Platform;
using Foundation;
using StoreKit;
using UIKit;

namespace Daynote.Mobile.iOS.Platform;

// StoreKit 1 is marked obsoleted from iOS 18 in favour of StoreKit 2, which has no C# binding (see
// the remarks below). It is deprecated, not removed: iOS 18 and 26 still run it, and it is the only
// purchase API this app can call.
#pragma warning disable CA1422

/// <summary>
/// In-App Purchase over StoreKit (docs/CLOUD_SYNC.md §14.8).
/// </summary>
/// <remarks>
/// <para>
/// StoreKit 1 — <see cref="SKProductsRequest"/>, <see cref="SKPaymentQueue"/> and
/// <see cref="SKPaymentTransaction"/> — because StoreKit 2 is a Swift-only API with no binding a C#
/// app can call. Apple has deprecated the original API in favour of it but still runs it, and the
/// server side is StoreKit 2's: the App Store Server API and its signed transactions do not care
/// which client bought.
/// </para>
/// <para>
/// One observer, added to the default queue when this is constructed, which is at launch
/// (<see cref="IosPlatformServices.Create"/>). Purchased and restored transactions go to
/// <see cref="TransactionHandler"/> and are finished only when it answers true; until it is set
/// they wait in <see cref="held"/>. A failed transaction is finished at once — there is nothing for
/// the server to record and StoreKit would otherwise hand it back on every launch.
/// </para>
/// </remarks>
public sealed class IosStorePurchases : IStorePurchases
{
    /// <summary>The App Store's subscriptions page for this Apple ID.</summary>
    private const string ManageUrl = "https://apps.apple.com/account/subscriptions";

    private readonly Observer observer;
    private readonly Dictionary<string, SKProduct> products = new(StringComparer.Ordinal);
    private readonly List<SKPaymentTransaction> held = [];

    /// <summary>The purchase sheet that is up, by product id: one at a time per product.</summary>
    private readonly Dictionary<string, TaskCompletionSource<StorePurchaseResult>> purchases = new(StringComparer.Ordinal);

    /// <summary>
    /// The restore in progress. StoreKit 1 hands back one restored transaction per renewal ever paid,
    /// so they are grouped by subscription (the original transaction): one server call each, made one
    /// after another, and the rest of a group finished with the first.
    /// </summary>
    private TaskCompletionSource<int>? restore;
    private readonly Dictionary<string, Task<bool>> restoreGroups = new(StringComparer.Ordinal);
    private readonly List<Task> restoreWork = [];
    private Task restoreChain = Task.CompletedTask;

    /// <summary>Transactions being sent right now, by StoreKit's id, so a retry does not send one twice.</summary>
    private readonly HashSet<string> settling = new(StringComparer.Ordinal);

    private Func<StoreTransaction, Task<bool>>? handler;

    /// <summary>The products request in flight, held because StoreKit keeps only a weak delegate.</summary>
    private readonly HashSet<ProductsRequest> requests = [];

    public IosStorePurchases()
    {
        observer = new Observer(this);
        SKPaymentQueue.DefaultQueue.AddTransactionObserver(observer);
    }

    public bool CanMakePayments => SKPaymentQueue.CanMakePayments;

    public event EventHandler? StorefrontChanged;

    public Func<StoreTransaction, Task<bool>>? TransactionHandler
    {
        get => handler;
        set
        {
            handler = value;
            RetryHeld();
        }
    }

    public void RetryHeld()
    {
        if (handler is null || held.Count == 0)
        {
            return;
        }

        SKPaymentTransaction[] waiting = [.. held];
        held.Clear();
        foreach (SKPaymentTransaction transaction in waiting)
        {
            _ = SettleAsync(transaction);
        }
    }

    /// <summary>
    /// Asks StoreKit for the products, and asks once more if the prices came back in another
    /// storefront's locale than the one the payment queue will charge in — the TestFlight build
    /// showed $2.49 on the card while the sheet charged ₩2,900. The two are logged (country codes and
    /// a locale identifier, nothing about the person) so a mismatch that persists can be diagnosed.
    /// </summary>
    public async Task<IReadOnlyList<StoreProduct>> LoadProductsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken)
    {
        SKProduct[] found = await RequestAsync(productIds, cancellationToken).ConfigureAwait(true);
        if (Mismatched(found))
        {
            found = await RequestAsync(productIds, cancellationToken).ConfigureAwait(true);
            Mismatched(found);
        }

        foreach (SKProduct product in found)
        {
            products[product.ProductIdentifier] = product;
        }

        return [.. found.Select(Describe)];
    }

    /// <summary>
    /// True when the products' price locale is not the payment queue's storefront. The storefront
    /// names its country in ISO 3166 alpha-3 ("KOR"), the locale in alpha-2 ("KR").
    /// </summary>
    private static bool Mismatched(SKProduct[] found)
    {
        string? storefront = SKPaymentQueue.DefaultQueue.Storefront?.CountryCode;
        string? localeRegion = found.FirstOrDefault()?.PriceLocale?.CountryCode;
        string? locale = found.FirstOrDefault()?.PriceLocale?.Identifier;
        Console.WriteLine($"[Daynote IAP] storefront={storefront ?? "?"} priceLocale={locale ?? "?"} products={found.Length}");
        if (storefront is not { Length: 3 } || localeRegion is not { Length: 2 })
        {
            return false;
        }

        try
        {
            string alpha3 = new System.Globalization.RegionInfo(localeRegion).ThreeLetterISORegionName;
            return !string.Equals(alpha3, storefront, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<SKProduct[]> RequestAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken)
    {
        var request = new ProductsRequest(productIds);
        requests.Add(request);
        try
        {
            return await request.RunAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            requests.Remove(request);
        }
    }

    /// <summary>The localized title, and the price formatted in the storefront's own currency and locale.</summary>
    private static StoreProduct Describe(SKProduct product)
    {
        using var formatter = new NSNumberFormatter
        {
            NumberStyle = NSNumberFormatterStyle.Currency,
            Locale = product.PriceLocale,
        };
        return new StoreProduct(
            product.ProductIdentifier,
            product.LocalizedTitle ?? string.Empty,
            formatter.StringFromNumber(product.Price) ?? product.Price.ToString());
    }

    public async Task<StorePurchaseResult> PurchaseAsync(string productId, string accountToken, CancellationToken cancellationToken)
    {
        if (!products.TryGetValue(productId, out SKProduct? product))
        {
            await LoadProductsAsync([productId], cancellationToken).ConfigureAwait(true);
            if (!products.TryGetValue(productId, out product))
            {
                return new StorePurchaseResult(StorePurchaseStatus.Failed);
            }
        }

        if (purchases.ContainsKey(productId))
        {
            return new StorePurchaseResult(StorePurchaseStatus.Pending);
        }

        var completion = new TaskCompletionSource<StorePurchaseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        purchases[productId] = completion;

        // The account id, a UUID, as the application username: StoreKit copies a UUID there into the
        // transaction's appAccountToken, which is what the server matches the purchase to.
        SKMutablePayment payment = SKMutablePayment.PaymentWithProduct(product);
        payment.ApplicationUsername = accountToken;
        SKPaymentQueue.DefaultQueue.AddPayment(payment);

        using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
        {
            try
            {
                return await completion.Task.ConfigureAwait(true);
            }
            finally
            {
                purchases.Remove(productId);
            }
        }
    }

    public async Task<int> RestoreAsync(CancellationToken cancellationToken)
    {
        restore = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        restoreGroups.Clear();
        restoreWork.Clear();
        restoreChain = Task.CompletedTask;
        SKPaymentQueue.DefaultQueue.RestoreCompletedTransactions();
        using (cancellationToken.Register(() => restore.TrySetCanceled(cancellationToken)))
        {
            return await restore.Task.ConfigureAwait(true);
        }
    }

    public void OpenSubscriptionManagement()
    {
        if (NSUrl.FromString(ManageUrl) is { } url)
        {
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);
        }
    }

    /// <summary>On the UI thread: what each changed transaction means.</summary>
    private void OnUpdated(SKPaymentTransaction[] transactions)
    {
        foreach (SKPaymentTransaction transaction in transactions)
        {
            string productId = transaction.Payment?.ProductIdentifier ?? string.Empty;
            switch (transaction.TransactionState)
            {
                case SKPaymentTransactionState.Purchased:
                    _ = SettleAsync(transaction);
                    break;

                case SKPaymentTransactionState.Restored:
                    Restored(transaction);
                    break;

                case SKPaymentTransactionState.Failed:
                    SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
                    bool cancelled = transaction.Error?.Code == (nint)(long)SKError.PaymentCancelled;
                    Complete(productId, cancelled
                        ? new StorePurchaseResult(StorePurchaseStatus.Cancelled)
                        : new StorePurchaseResult(StorePurchaseStatus.Failed, transaction.Error?.LocalizedDescription));
                    break;

                case SKPaymentTransactionState.Deferred:
                    // Ask to Buy: someone else approves it later, and the approval arrives as an
                    // ordinary purchase through this observer, whenever the app next runs.
                    Complete(productId, new StorePurchaseResult(StorePurchaseStatus.Pending));
                    break;

                default:
                    // Purchasing: the sheet is up.
                    break;
            }
        }
    }

    /// <summary>
    /// Hands a purchased or restored transaction to the server, through the handler, and finishes it
    /// once that has recorded it. The id sent for a restore is the original purchase's: a restored
    /// transaction is StoreKit's copy, with an id of its own that the App Store Server API need not know.
    /// </summary>
    private async Task SettleAsync(SKPaymentTransaction transaction)
    {
        await SettleOneAsync(transaction).ConfigureAwait(true);
        if (transaction.TransactionState != SKPaymentTransactionState.Restored)
        {
            Complete(
                transaction.Payment?.ProductIdentifier ?? string.Empty,
                new StorePurchaseResult(StorePurchaseStatus.Purchased, TransactionId: transaction.TransactionIdentifier));
        }
    }

    /// <summary>
    /// One call to the handler. True finishes the transaction; false keeps it in <see cref="held"/>,
    /// sent again by <see cref="RetryHeld"/> (signing in) or, failing that, by StoreKit on the next launch.
    /// </summary>
    private async Task<bool> SettleOneAsync(SKPaymentTransaction transaction)
    {
        string productId = transaction.Payment?.ProductIdentifier ?? string.Empty;
        bool isRestore = transaction.TransactionState == SKPaymentTransactionState.Restored;
        string? id = isRestore
            ? transaction.OriginalTransaction?.TransactionIdentifier ?? transaction.TransactionIdentifier
            : transaction.TransactionIdentifier;
        string key = transaction.TransactionIdentifier ?? id ?? string.Empty;

        if (handler is not { } settle || id is not { Length: > 0 } || !settling.Add(key))
        {
            Hold(transaction);
            return false;
        }

        bool finish = false;
        try
        {
            finish = await settle(new StoreTransaction(id, productId, isRestore)).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError($"Recording an App Store transaction failed: {exception}");
        }
        finally
        {
            settling.Remove(key);
        }

        if (finish)
        {
            SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
        }
        else
        {
            Hold(transaction);
        }

        return finish;
    }

    private void Hold(SKPaymentTransaction transaction)
    {
        if (!held.Contains(transaction))
        {
            held.Add(transaction);
        }
    }

    /// <summary>
    /// A restored transaction: the first of its subscription is sent, after the one before it; the
    /// rest of that subscription wait for its answer and are finished, or held, with it.
    /// </summary>
    private void Restored(SKPaymentTransaction transaction)
    {
        string group = transaction.OriginalTransaction?.TransactionIdentifier ?? transaction.TransactionIdentifier ?? string.Empty;
        if (restoreGroups.TryGetValue(group, out Task<bool>? first))
        {
            restoreWork.Add(FollowAsync(first, transaction));
            return;
        }

        Task<bool> sent = SendAfterAsync(restoreChain, transaction);
        restoreChain = sent;
        restoreGroups[group] = sent;
        restoreWork.Add(sent);
    }

    private async Task<bool> SendAfterAsync(Task before, SKPaymentTransaction transaction)
    {
        await before.ConfigureAwait(true);
        return await SettleOneAsync(transaction).ConfigureAwait(true);
    }

    private static async Task FollowAsync(Task<bool> first, SKPaymentTransaction transaction)
    {
        if (await first.ConfigureAwait(true))
        {
            SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
        }
    }

    private void Complete(string productId, StorePurchaseResult result)
    {
        if (purchases.TryGetValue(productId, out TaskCompletionSource<StorePurchaseResult>? completion))
        {
            completion.TrySetResult(result);
        }
    }

    private async void OnRestoreFinished(NSError? error)
    {
        if (restore is not { } completion)
        {
            return;
        }

        restore = null;
        if (error is not null)
        {
            completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
            return;
        }

        // Every restored transaction has been delivered by now; wait for the server to have each
        // subscription, and answer how many subscriptions there were.
        await Task.WhenAll([.. restoreWork]).ConfigureAwait(true);
        completion.TrySetResult(restoreGroups.Count);
    }

    /// <summary>
    /// The payment-queue observer. StoreKit may call it on any thread; everything it reports is
    /// handled on the UI thread, where the view models it updates live.
    /// </summary>
    private sealed class Observer(IosStorePurchases owner) : SKPaymentTransactionObserver
    {
        public override void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions) =>
            Dispatcher.UIThread.Post(() => owner.OnUpdated(transactions));

        public override void RestoreCompletedTransactionsFinished(SKPaymentQueue queue) =>
            Dispatcher.UIThread.Post(() => owner.OnRestoreFinished(null));

        public override void RestoreCompletedTransactionsFailedWithError(SKPaymentQueue queue, NSError error) =>
            Dispatcher.UIThread.Post(() => owner.OnRestoreFinished(error));

        public override void DidChangeStorefront(SKPaymentQueue queue) =>
            Dispatcher.UIThread.Post(() => owner.StorefrontChanged?.Invoke(owner, EventArgs.Empty));
    }

    /// <summary>One products request and its delegate, answered once.</summary>
    private sealed class ProductsRequest : SKProductsRequestDelegate
    {
        private readonly SKProductsRequest request;
        private readonly TaskCompletionSource<SKProduct[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProductsRequest(IReadOnlyCollection<string> productIds)
        {
            request = new SKProductsRequest(new NSSet<NSString>(productIds.Select(id => new NSString(id)).ToArray()))
            {
                Delegate = this,
            };
        }

        public async Task<SKProduct[]> RunAsync(CancellationToken cancellationToken)
        {
            request.Start();
            using (cancellationToken.Register(() =>
            {
                request.Cancel();
                completion.TrySetCanceled(cancellationToken);
            }))
            {
                return await completion.Task.ConfigureAwait(true);
            }
        }

        public override void ReceivedResponse(SKProductsRequest request, SKProductsResponse response) =>
            completion.TrySetResult(response.Products ?? []);

        public override void RequestFailed(SKRequest request, NSError error) =>
            completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
    }
}
#pragma warning restore CA1422
