using Daynote.Core.Sync;

namespace Daynote.Core.Tests;

/// <summary>
/// The billing model the app reads from the server (docs/CLOUD_SYNC.md §14): two paid tiers, each at
/// two intervals, with prices — and the older server that sent only the Pro intervals and no prices.
/// </summary>
[TestClass]
public sealed class BillingModelTests
{
    private static readonly BillingOffer[] FourOffers =
    [
        new(BillingTier.Pro, BillingPlan.Monthly, [new Money("KRW", 2900), new Money("USD", 249)]),
        new(BillingTier.Pro, BillingPlan.Annual, [new Money("KRW", 24000), new Money("USD", 1999)]),
        new(BillingTier.Premium, BillingPlan.Monthly, [new Money("KRW", 5900), new Money("USD", 499)]),
        new(BillingTier.Premium, BillingPlan.Annual, [new Money("KRW", 48000), new Money("USD", 3999)]),
    ];

    [TestMethod]
    public void Money_reads_minor_units_per_currency()
    {
        Assert.AreEqual(24000m, new Money("KRW", 24000).Amount);
        Assert.AreEqual(0, new Money("KRW", 24000).Decimals);
        Assert.AreEqual(19.99m, new Money("USD", 1999).Amount);
        Assert.AreEqual(2, new Money("usd", 1999).Decimals);
    }

    [TestMethod]
    public void Offers_are_found_by_tier_and_plan_and_priced_by_currency()
    {
        var links = new BillingLinks(true, false, Offers: FourOffers);

        Assert.AreEqual(4, links.AvailableOffers.Count);
        Assert.AreEqual(48000, links.Find(BillingTier.Premium, BillingPlan.Annual)?.PriceIn("KRW")?.MinorUnits);
        Assert.AreEqual(499, links.Find(BillingTier.Premium, BillingPlan.Monthly)?.PriceIn("usd")?.MinorUnits);
        Assert.IsNull(links.Find(BillingTier.Premium, BillingPlan.Monthly)?.PriceIn("EUR"));
        Assert.IsTrue(links.Sells(BillingTier.Premium));
    }

    [TestMethod]
    public void An_older_server_sells_only_the_Pro_intervals_it_listed_and_names_no_price()
    {
        var links = new BillingLinks(true, true, OffersMonthly: false, OffersAnnual: true);

        BillingOffer only = links.AvailableOffers.Single();
        Assert.AreEqual(BillingTier.Pro, only.Tier);
        Assert.AreEqual(BillingPlan.Annual, only.Plan);
        Assert.AreEqual(0, only.Prices.Count);
        Assert.IsFalse(links.Sells(BillingTier.Premium));
        Assert.IsFalse(links.CanChange);
    }

    [TestMethod]
    public void Nothing_is_on_sale_without_a_checkout_whatever_the_list_says()
    {
        Assert.AreEqual(0, new BillingLinks(false, true, Offers: FourOffers).AvailableOffers.Count);
        Assert.AreEqual(0, BillingLinks.None.AvailableOffers.Count);
    }

    [TestMethod]
    public void A_paid_state_without_a_tier_is_Pro()
    {
        var old = new Entitlement(EntitlementState.Active, null, true, true);
        var premium = old with { Tier = BillingTier.Premium, QuotaBytes = 200L << 30, UsedBytes = 5 };

        Assert.AreEqual(BillingTier.Pro, old.PaidTier);
        Assert.IsNull(old.QuotaBytes);
        Assert.AreEqual(BillingTier.Premium, premium.PaidTier);
    }

    [TestMethod]
    public void Wire_names_round_trip_and_unknown_ones_read_as_null()
    {
        foreach (BillingTier tier in Enum.GetValues<BillingTier>())
        {
            Assert.AreEqual(tier, BillingPlanExtensions.ParseTier(tier.ToWire()));
        }

        foreach (BillingPlan plan in Enum.GetValues<BillingPlan>())
        {
            Assert.AreEqual(plan, BillingPlanExtensions.ParsePlan(plan.ToWire()));
        }

        Assert.IsNull(BillingPlanExtensions.ParseTier("gold"));
        Assert.IsNull(BillingPlanExtensions.ParseTier(null));
        Assert.IsNull(BillingPlanExtensions.ParsePlan("lifetime"));
    }
}
