namespace Daynote.Core.Sync;

/// <summary>
/// Whether this account may sync, and until when (docs/CLOUD_SYNC.md §14).
/// </summary>
/// <remarks>
/// Cloud sync is the paid feature; the app is not. Nothing in this type gates local note-taking, and
/// nothing should ever be made to: a lapsed subscription stops sync and keeps both copies — the
/// notes on this PC, which never needed an account, and the ones already uploaded.
/// </remarks>
public enum EntitlementState
{
    /// <summary>No account, or no answer from the server yet. Sync is simply not running.</summary>
    Unknown,

    /// <summary>The free trial granted once at sign-up.</summary>
    Trial,

    Active,

    /// <summary>A payment is being retried. Sync keeps working so a dead card is not a cliff.</summary>
    Grace,

    /// <summary>Sync is off until there is a subscription. Nothing has been deleted.</summary>
    Expired,
}

/// <summary>
/// The billing state as the app sees it. <see cref="Until"/> is when the current state runs out, and
/// is what the trial countdown is built from — Store policy 10.8.4 requires warning people before a
/// trial takes functionality away, which cannot be done without the date.
/// </summary>
public sealed record Entitlement(
    EntitlementState State,
    DateTimeOffset? Until,
    bool CanSync,
    bool HasSubscribed)
{
    public static Entitlement Unknown { get; } = new(EntitlementState.Unknown, null, false, false);

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
public sealed record BillingLinks(
    bool CanCheckout,
    bool CanManage,
    bool OffersMonthly = true,
    bool OffersAnnual = true)
{
    public static BillingLinks None { get; } = new(false, false, false, false);

    /// <summary>True when the monthly plan can be bought right now.</summary>
    public bool CanCheckoutMonthly => CanCheckout && OffersMonthly;

    /// <summary>True when the annual plan can be bought right now.</summary>
    public bool CanCheckoutAnnual => CanCheckout && OffersAnnual;
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
}
