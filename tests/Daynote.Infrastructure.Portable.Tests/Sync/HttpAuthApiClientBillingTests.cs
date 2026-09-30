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
          "can_checkout": true, "can_manage": true, "can_change": true,
          "plans": ["monthly", "annual"],
          "offers": [
            { "tier": "pro", "plan": "monthly", "prices": [{ "currency": "KRW", "amount": "2900" }, { "currency": "USD", "amount": "249" }] },
            { "tier": "premium", "plan": "annual", "prices": [{ "currency": "KRW", "amount": "49000" }, { "currency": "USD", "amount": "not-a-number" }] },
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

        // The unknown tier is skipped, and so is the price that is not a number.
        Assert.AreEqual(2, links.AvailableOffers.Count);
        Assert.AreEqual(249, links.Find(BillingTier.Pro, BillingPlan.Monthly)?.PriceIn("USD")?.MinorUnits);
        BillingOffer premium = links.Find(BillingTier.Premium, BillingPlan.Annual)!;
        Assert.AreEqual(49000, premium.PriceIn("KRW")?.MinorUnits);
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

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        public string Response { get; set; } = response;

        public string? LastPath { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
