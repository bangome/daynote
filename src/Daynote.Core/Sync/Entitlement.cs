namespace Daynote.Core.Sync;

/// <summary>
/// Whether this account is on the paid tier, and until when (docs/CLOUD_SYNC.md §14).
/// </summary>
/// <remarks>
/// Text sync (notes, to-dos, tags, favorites) is free for every signed-in account. The paid tier is
/// image and file sync. Nothing in this type gates local note-taking or text sync, and nothing
/// should ever be made to: a lapsed subscription stops file sync and keeps every copy — the notes
/// and files on this PC, which never needed an account, and everything already uploaded.
/// </remarks>
public enum EntitlementState
{
    /// <summary>No account, or no answer from the server yet. Sync is simply not running.</summary>
    Unknown,

    /// <summary>The free trial granted once at sign-up.</summary>
    Trial,

    Active,

    /// <summary>A payment is being retried. File sync keeps working so a dead card is not a cliff.</summary>
    Grace,

    /// <summary>File sync is off until there is a subscription. Text still syncs; nothing has been deleted.</summary>
    Expired,
}

/// <summary>
/// The billing state as the app sees it. <see cref="Until"/> is when the current state runs out, and
/// is what the trial countdown is built from — Store policy 10.8.4 requires warning people before a
/// trial takes functionality away, which cannot be done without the date.
/// </summary>
/// <remarks>
/// <see cref="Tier"/>, <see cref="Plan"/>, <see cref="QuotaBytes"/> and <see cref="UsedBytes"/> came
/// with the second paid tier. A server that predates it sends none of them, and they stay null: the
/// app then says "Pro" for any paid state and shows no storage line, which is what it did before.
/// </remarks>
public sealed record Entitlement(
    EntitlementState State,
    DateTimeOffset? Until,
    bool CanSyncFiles,
    bool HasSubscribed,
    BillingTier? Tier = null,
    BillingPlan? Plan = null,
    long? QuotaBytes = null,
    long? UsedBytes = null)
{
    public static Entitlement Unknown { get; } = new(EntitlementState.Unknown, null, false, false);

    /// <summary>
    /// The tier to call this account by while it is paid: the server's word when it gave one, and Pro
    /// otherwise — the only tier a server that does not report one ever sold.
    /// </summary>
    public BillingTier PaidTier => Tier ?? BillingTier.Pro;

    /// <summary>
    /// Whole days left, or null when the state has no end date. Floored on purpose: a countdown
    /// that rounds up would tell someone they have two days left on the last afternoon.
    /// </summary>
    public int? DaysRemaining(DateTimeOffset now) => Until is { } until
        ? Math.Max(0, (int)Math.Floor((until - now).TotalDays))
        : null;

    /// <summary>
    /// True while the trial is close enough to its end to say so without nagging. A countdown shown
    /// from day one would be a two-week advertisement; shown on the last day it is a surprise.
    /// </summary>
    public bool ShouldWarn(DateTimeOffset now) =>
        State is EntitlementState.Trial or EntitlementState.Grace
        && DaysRemaining(now) is { } days
        && days <= 5;
}

/// <summary>
/// Which billing pages are available to this account. Two flags rather than two URLs, because both
/// links are created at the moment they are clicked.
/// </summary>
/// <remarks>
/// Neither page is an in-app payment form. The provider is the merchant of record, so it owns the
/// card fields, the tax, and the receipts — and Store policy 10.8.2 wants the transaction to
/// identify its commerce provider, which a hosted page does by construction.
/// <para>
/// The checkout is created per click because only a server-created transaction can carry the
/// account id that ties the resulting subscription back to Daynote; the portal, because the
/// provider's portal links are single-use and expire. A URL cached here would go stale either way.
/// </para>
/// </remarks>
/// <remarks>
/// <see cref="Offers"/> is every tier at every interval the server has a price for, with the prices
/// to show. A server from before Premium sends only the Pro intervals, as <see cref="OffersMonthly"/>
/// and <see cref="OffersAnnual"/>, and no prices; <see cref="AvailableOffers"/> folds both shapes into
/// one list so nothing downstream has to know which server answered.
/// <para>
/// <see cref="CanChange"/> says the running subscription can move to another offer in place — the
/// only way an existing subscriber changes tier, because a second checkout would bill twice.
/// </para>
/// </remarks>
public sealed record BillingLinks(
    bool CanCheckout,
    bool CanManage,
    bool OffersMonthly = true,
    bool OffersAnnual = true,
    IReadOnlyList<BillingOffer>? Offers = null,
    bool CanChange = false)
{
    public static BillingLinks None { get; } = new(false, false, false, false);

    /// <summary>True when the monthly plan can be bought right now.</summary>
    public bool CanCheckoutMonthly => CanCheckout && OffersMonthly;

    /// <summary>True when the annual plan can be bought right now.</summary>
    public bool CanCheckoutAnnual => CanCheckout && OffersAnnual;

    /// <summary>What can be bought, in the server's display order. Empty when nothing is on sale.</summary>
    public IReadOnlyList<BillingOffer> AvailableOffers =>
        !CanCheckout ? []
        : Offers ?? LegacyOffers();

    /// <summary>The offer for this tier and interval, or null when it is not on sale.</summary>
    public BillingOffer? Find(BillingTier tier, BillingPlan plan) =>
        AvailableOffers.FirstOrDefault(offer => offer.Tier == tier && offer.Plan == plan);

    /// <summary>True when at least one interval of <paramref name="tier"/> is on sale.</summary>
    public bool Sells(BillingTier tier) => AvailableOffers.Any(offer => offer.Tier == tier);

    private BillingOffer[] LegacyOffers()
    {
        List<BillingOffer> offers = [];
        if (OffersMonthly)
        {
            offers.Add(new BillingOffer(BillingTier.Pro, BillingPlan.Monthly, []));
        }

        if (OffersAnnual)
        {
            offers.Add(new BillingOffer(BillingTier.Pro, BillingPlan.Annual, []));
        }

        return [.. offers];
    }
}

/// <summary>
/// The two paid tiers. Both sync images and files; Pro includes 2GB of storage, Premium is sold as
/// unlimited and held by the server to a fair-use ceiling. The 14-day trial is Pro-level.
/// </summary>
public enum BillingTier
{
    Pro,
    Premium,
}

/// <summary>
/// One price, as the server sends it: an ISO 4217 code and the amount in the currency's minor unit
/// (KRW has none, so ₩2,900 is 2900; USD has cents, so $2.49 is 249).
/// </summary>
public sealed record Money(string Currency, long MinorUnits)
{
    /// <summary>Currencies with no minor unit, among those the catalog could plausibly use.</summary>
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase) { "KRW", "JPY" };

    /// <summary>Digits after the decimal point for this currency.</summary>
    public int Decimals => ZeroDecimal.Contains(Currency) ? 0 : 2;

    /// <summary>The amount in whole units: 249 cents is 2.49.</summary>
    public decimal Amount => MinorUnits / (decimal)Math.Pow(10, Decimals);
}

/// <summary>One thing that can be bought: a tier at an interval, with its catalog prices.</summary>
/// <remarks>
/// <see cref="Prices"/> is empty when the server predates it. The checkout itself always charges what
/// the payment provider says, in the buyer's currency; this list only describes the catalog.
/// </remarks>
public sealed record BillingOffer(BillingTier Tier, BillingPlan Plan, IReadOnlyList<Money> Prices)
{
    /// <summary>The price in <paramref name="currency"/>, or null when the catalog lists none.</summary>
    public Money? PriceIn(string currency) =>
        Prices.FirstOrDefault(price => string.Equals(price.Currency, currency, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The billing intervals the subscription is sold at. Annual is the one the pricing page leads
/// with (31% under twelve monthly payments); monthly is there for people who want to try a month.
/// </summary>
public enum BillingPlan
{
    Monthly,
    Annual,
}

public static class BillingPlanExtensions
{
    /// <summary>The wire name the Worker expects in <c>POST /v1/billing/checkout</c>.</summary>
    public static string ToWire(this BillingPlan plan) => plan switch
    {
        BillingPlan.Monthly => "monthly",
        BillingPlan.Annual => "annual",
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null),
    };

    /// <summary>The wire name the Worker expects alongside the plan.</summary>
    public static string ToWire(this BillingTier tier) => tier switch
    {
        BillingTier.Pro => "pro",
        BillingTier.Premium => "premium",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
    };

    /// <summary>Reads a plan from the wire; null for anything this version does not know.</summary>
    public static BillingPlan? ParsePlan(string? value) => value switch
    {
        "monthly" => BillingPlan.Monthly,
        "annual" => BillingPlan.Annual,
        _ => null,
    };

    /// <summary>Reads a tier from the wire; null for anything this version does not know.</summary>
    public static BillingTier? ParseTier(string? value) => value switch
    {
        "pro" => BillingTier.Pro,
        "premium" => BillingTier.Premium,
        _ => null,
    };
}
