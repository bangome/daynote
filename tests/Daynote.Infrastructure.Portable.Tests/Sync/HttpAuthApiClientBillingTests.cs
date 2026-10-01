using System.Net;
using System.Text;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Sync;

/// <summary>
/// The billing half of the HTTP client against the Worker's two shapes: the one with tiers, offers,
/// prices and storage, and the one installed copies were written against, which has none of them.
/// </summary>
[TestClass]
public sealed class HttpAuthApiClientBillingTests
{
    private const string TieredStatus = """
        {
          "state": "active", "until": "2027-09-30T00:00:00.0000000Z", "can_sync_files": true,
          "has_subscribed": true, "tier": "premium", "plan": "annual",
          "quota_bytes": 214748364800, "used_bytes": 1288490188,
          "can_checkout": true, "can_manage": true, "can_change": true, "duplicate_subscription": true,
          "plans": ["monthly", "annual"],
          "offers": [
            { "tier": "pro", "plan": "monthly", "prices": [{ "currency": "KRW", "amount": "2900" }, { "currency": "USD", "amount": "249" }] },
            { "tier": "premium", "plan": "annual", "prices": [{ "currency": "KRW", "amount": "48000" }, { "currency": "USD", "amount": "not-a-number" }] },
            { "tier": "gold", "plan": "annual", "prices": [] }
          ],
          "server_utc": "2026-09-30T00:00:00.0000000Z"
        }
        """;

    private const string OldStatus = """
        {
          "state": "trial", "until": "2026-10-10T00:00:00.0000000Z", "can_sync_files": true,
          "has_subscribed": false, "can_checkout": true, "plans": ["annual"], "can_manage": false,
          "server_utc": "2026-09-30T00:00:00.0000000Z"
        }
        """;

    [TestMethod]
    public async Task Reads_the_tier_the_storage_and_the_priced_offers()
    {
        var handler = new StubHandler(TieredStatus);
        var client = new HttpAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://daynote.test/") });

        (Entitlement entitlement, BillingLinks links) = await client.GetBillingAsync("token");

        Assert.AreEqual(BillingTier.Premium, entitlement.Tier);
        Assert.AreEqual(BillingPlan.Annual, entitlement.Plan);
        Assert.AreEqual(214748364800L, entitlement.QuotaBytes);
        Assert.AreEqual(1288490188L, entitlement.UsedBytes);
        Assert.IsTrue(links.CanChange);
        Assert.IsTrue(links.DuplicateSubscription);

        // The unknown tier is skipped, and so is the price that is not a number.
        Assert.AreEqual(2, links.AvailableOffers.Count);
        Assert.AreEqual(249, links.Find(BillingTier.Pro, BillingPlan.Monthly)?.PriceIn("USD")?.MinorUnits);
        BillingOffer premium = links.Find(BillingTier.Premium, BillingPlan.Annual)!;
        Assert.AreEqual(48000, premium.PriceIn("KRW")?.MinorUnits);
        Assert.IsNull(premium.PriceIn("USD"));
    }

    [TestMethod]
    public async Task Tolerates_the_server_from_before_Premium()
    {
        var client = new HttpAuthApiClient(new HttpClient(new StubHandler(OldStatus)) { BaseAddress = new Uri("https://daynote.test/") });

        (Entitlement entitlement, BillingLinks links) = await client.GetBillingAsync("token");

        Assert.AreEqual(EntitlementState.Trial, entitlement.State);
        Assert.IsNull(entitlement.Tier);
        Assert.IsNull(entitlement.QuotaBytes);
        Assert.IsFalse(links.CanChange);
        Assert.IsFalse(links.DuplicateSubscription);
        Assert.IsNull(links.Offers);
        Assert.AreEqual(BillingPlan.Annual, links.AvailableOffers.Single().Plan);
        Assert.AreEqual(BillingTier.Pro, links.AvailableOffers.Single().Tier);
    }

    [TestMethod]
    public async Task Sends_the_tier_and_plan_to_the_checkout_and_the_change()
    {
        var handler = new StubHandler("""{ "url": "https://pay.test/one", "server_utc": "2026-09-30T00:00:00.0000000Z" }""");
        var client = new HttpAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://daynote.test/") });

        await client.CreateCheckoutSessionAsync("token", BillingTier.Premium, BillingPlan.Monthly);

        Assert.AreEqual("/v1/billing/checkout", handler.LastPath);
        Assert.AreEqual("""{"tier":"premium","plan":"monthly"}""", handler.LastBody);

        handler.Response = TieredStatus;
        (Entitlement changed, _) = await client.ChangePlanAsync("token", BillingTier.Premium, BillingPlan.Annual);

        Assert.AreEqual("/v1/billing/change", handler.LastPath);
        Assert.AreEqual("""{"tier":"premium","plan":"annual"}""", handler.LastBody);
        Assert.AreEqual(BillingTier.Premium, changed.Tier);
    }

    private const string AppStoreStatus = """
        {
          "state": "active", "until": "2026-11-01T00:00:00.0000000Z", "can_sync_files": true,
          "has_subscribed": true, "tier": "pro", "plan": "monthly",
          "can_checkout": true, "can_manage": false, "can_change": false, "duplicate_subscription": true,
          "duplicate_provider": "paddle", "provider": "apple",
          "apple_products": [
            { "tier": "pro", "plan": "monthly", "product_id": "cc.arachat.daynote.pro.monthly" },
            { "tier": "premium", "plan": "annual", "product_id": "cc.arachat.daynote.premium.annual" },
            { "tier": "gold", "plan": "annual", "product_id": "cc.arachat.daynote.gold" }
          ],
          "apple_can_purchase": true, "apple_product_id": "cc.arachat.daynote.pro.monthly",
          "server_utc": "2026-10-02T00:00:00.0000000Z"
        }
        """;

    [TestMethod]
    public async Task Reads_the_App_Store_fields_and_does_without_them()
    {
        var handler = new StubHandler(AppStoreStatus);
        var client = new HttpAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://daynote.test/") });

        (_, BillingLinks links) = await client.SubmitAppStoreTransactionAsync("token", "2000000000000001");

        Assert.AreEqual("/v1/billing/apple/transaction", handler.LastPath);
        Assert.AreEqual("""{"transaction_id":"2000000000000001"}""", handler.LastBody);
        Assert.AreEqual(BillingProvider.Apple, links.Provider);
        Assert.AreEqual(BillingProvider.Paddle, links.DuplicateProvider);
        Assert.IsTrue(links.AppleCanPurchase);
        Assert.AreEqual("cc.arachat.daynote.pro.monthly", links.AppleProductId);
        Assert.AreEqual(2, links.AppleProducts!.Count, "The product of an unknown tier was kept.");
        Assert.AreEqual("cc.arachat.daynote.premium.annual", links.FindAppleProduct(BillingTier.Premium, BillingPlan.Annual)?.ProductId);

        handler.Response = OldStatus;
        (_, BillingLinks old) = await client.GetBillingAsync("token");
        Assert.IsNull(old.Provider);
        Assert.IsFalse(old.AppleCanPurchase);
        Assert.AreEqual(0, old.AppleProducts!.Count);
    }

    [TestMethod]
    public async Task Another_accounts_App_Store_purchase_is_its_own_failure()
    {
        var handler = new StubHandler("""{ "error": "forbidden", "message": "This App Store subscription belongs to a different Daynote account." }""")
        {
            Status = HttpStatusCode.Forbidden,
        };
        var client = new HttpAuthApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://daynote.test/") });

        AccountException failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await client.SubmitAppStoreTransactionAsync("token", "2000000000000001"));

        Assert.AreEqual(AccountFailure.PurchaseBelongsToAnotherAccount, failure.Failure);
        StringAssert.Contains(failure.Message, "different Daynote account");
    }

    [TestMethod]
    public async Task A_refused_App_Store_purchase_is_final_and_an_unconfigured_server_is_not()
    {
        var refused = new StubHandler("""{ "error": "bad_request", "message": "That purchase is not a Daynote subscription." }""")
        {
            Status = HttpStatusCode.BadRequest,
        };
        var client = new HttpAuthApiClient(new HttpClient(refused) { BaseAddress = new Uri("https://daynote.test/") });
        AccountException final = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await client.SubmitAppStoreTransactionAsync("token", "2000000000000001"));
        Assert.AreEqual(AccountFailure.PurchaseRefused, final.Failure);

        var unconfigured = new StubHandler("""{ "error": "unavailable", "message": "not configured" }""")
        {
            Status = HttpStatusCode.ServiceUnavailable,
        };
        client = new HttpAuthApiClient(new HttpClient(unconfigured) { BaseAddress = new Uri("https://daynote.test/") });
        AccountException later = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await client.SubmitAppStoreTransactionAsync("token", "2000000000000001"));
        Assert.AreEqual(AccountFailure.ServerError, later.Failure, "A server not set up yet must leave the purchase to be sent again.");
    }

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        public string Response { get; set; } = response;

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public string? LastPath { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
