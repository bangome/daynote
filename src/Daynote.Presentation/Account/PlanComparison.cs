using Daynote.App.Localization;

namespace Daynote.App.Account;

/// <summary>One line of the plan table: what it is, and whether the free plan has it.</summary>
/// <remarks>
/// Pro has every row — that is what makes it the paid plan — so only the free column varies.
/// </remarks>
public sealed class PlanComparisonRow
{
    private readonly Func<string> _label;

    internal PlanComparisonRow(Func<string> label, bool inFree)
    {
        _label = label;
        InFree = inFree;
    }

    public string Label => _label();

    /// <summary>The free plan includes this.</summary>
    public bool InFree { get; }

    /// <summary>It does not, so the table draws a dash rather than a tick.</summary>
    public bool NotInFree => !InFree;
}

/// <summary>
/// What the two plans are, side by side.
/// </summary>
/// <remarks>
/// Every row here is something the app already states elsewhere — the sync blurb, the account's list
/// of what signing in buys, the server's own 2GB attachment quota. Nothing is invented for the
/// table: a comparison that promises something the product does not do is worse than no comparison.
/// <para>
/// Only one row differs, and that is the honest shape of this product: signing in syncs notes for
/// nothing, and the subscription is for images and files. The table exists to make that plain rather
/// than to make the paid column look long.
/// </para>
/// </remarks>
public static class PlanComparison
{
    /// <summary>The rows, in the order the table shows them; the one Pro adds is last.</summary>
    public static IReadOnlyList<PlanComparisonRow> Rows { get; } =
    [
        new(() => AppStrings.PlanRowNotes, inFree: true),
        new(() => AppStrings.PlanRowDevices, inFree: true),
        new(() => AppStrings.PlanRowLock, inFree: true),
        new(() => AppStrings.PlanRowExport, inFree: true),
        new(() => AppStrings.PlanRowFiles, inFree: false),
    ];
}
