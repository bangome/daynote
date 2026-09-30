using Daynote.App.Localization;

namespace Daynote.App.Account;

/// <summary>One cell of the plan table: a tick, a dash, or a short piece of text.</summary>
public sealed class PlanCell
{
    private readonly Func<string>? _text;

    private PlanCell(bool isCheck, Func<string>? text)
    {
        IsCheck = isCheck;
        _text = text;
    }

    internal static PlanCell Check { get; } = new(true, null);

    internal static PlanCell Dash { get; } = new(false, null);

    internal static PlanCell Of(Func<string> text) => new(false, text);

    public bool IsCheck { get; }

    public bool IsText => _text is not null;

    public bool IsDash => !IsCheck && !IsText;

    public string Text => _text?.Invoke() ?? string.Empty;
}

/// <summary>One line of the plan table: what it is, and what each of the three plans has.</summary>
public sealed class PlanComparisonRow
{
    private readonly Func<string> _label;

    internal PlanComparisonRow(Func<string> label, PlanCell free, PlanCell pro, PlanCell premium)
    {
        _label = label;
        Free = free;
        Pro = pro;
        Premium = premium;
    }

    public string Label => _label();

    public PlanCell Free { get; }

    public PlanCell Pro { get; }

    public PlanCell Premium { get; }

    /// <summary>The free plan includes this.</summary>
    public bool InFree => Free.IsCheck;

    /// <summary>It does not, so the table draws a dash rather than a tick.</summary>
    public bool NotInFree => !InFree;
}

/// <summary>
/// What the three plans are, side by side: 무료, Pro and Premium.
/// </summary>
/// <remarks>
/// Every row here is something the app already states elsewhere — the sync blurb, the account's list
/// of what signing in buys, the server's own attachment quota. Nothing is invented for the table: a
/// comparison that promises something the product does not do is worse than no comparison.
/// <para>
/// Two rows differ, and that is the honest shape of this product: signing in syncs notes for
/// nothing, both paid plans add images and files, and the paid plans differ only in how much of them
/// may be stored. Premium's "unlimited" is held to a fair-use ceiling on the server, which is why the
/// table carries <see cref="AppStrings.PlanFairUseNote"/> under it.
/// </para>
/// </remarks>
public static class PlanComparison
{
    /// <summary>The rows, in the order the table shows them; the ones the paid plans add are last.</summary>
    public static IReadOnlyList<PlanComparisonRow> Rows { get; } =
    [
        new(() => AppStrings.PlanRowNotes, PlanCell.Check, PlanCell.Check, PlanCell.Check),
        new(() => AppStrings.PlanRowDevices, PlanCell.Check, PlanCell.Check, PlanCell.Check),
        new(() => AppStrings.PlanRowLock, PlanCell.Check, PlanCell.Check, PlanCell.Check),
        new(() => AppStrings.PlanRowExport, PlanCell.Check, PlanCell.Check, PlanCell.Check),
        new(() => AppStrings.PlanRowFiles, PlanCell.Dash, PlanCell.Check, PlanCell.Check),
        new(
            () => AppStrings.PlanRowStorage,
            PlanCell.Dash,
            PlanCell.Of(() => AppStrings.PlanStoragePro),
            PlanCell.Of(() => AppStrings.PlanStorageUnlimited)),
    ];
}
