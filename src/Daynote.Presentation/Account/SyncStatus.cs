using Daynote.App.Localization;

namespace Daynote.App.Account;

/// <summary>What the command-row chip is showing. Ordered roughly by how much it wants attention.</summary>
public enum SyncStatusKind
{
    /// <summary>Signed out. The chip is hidden entirely rather than showing "off".</summary>
    Hidden,

    Synced,

    Syncing,

    /// <summary>Local changes are waiting for the next cycle.</summary>
    Pending,

    Offline,

    /// <summary>Password was reset; the cloud copy cannot be opened on this device yet.</summary>
    Locked,

    Error,

    /// <summary>
    /// The paid tier has lapsed, so images and files are not syncing. Its own state rather than an
    /// error: nothing is broken and nothing was lost, and notes keep syncing either way
    /// (docs/CLOUD_SYNC.md §14).
    /// </summary>
    /// <remarks>
    /// Unreachable today. It is set from a 402 on a sync call, and no endpoint returns one now that
    /// text sync is free — the file endpoints that will are Phase 7. Kept rather than deleted
    /// because the chip, its copy and its colour are already correct for the day they arrive.
    /// </remarks>
    Unpaid,
}

/// <summary>
/// The chip's text and severity. Kept separate from the view model so the chip and the settings
/// section read one source of truth instead of each deciding what "synced" means.
/// </summary>
public sealed record SyncStatusView(SyncStatusKind Kind)
{
    public static SyncStatusView Hidden { get; } = new(SyncStatusKind.Hidden);

    public bool IsVisible => Kind != SyncStatusKind.Hidden;

    /// <summary>True for the states the user should act on, which the chip styles differently.</summary>
    public bool NeedsAttention =>
        Kind is SyncStatusKind.Locked or SyncStatusKind.Error or SyncStatusKind.Unpaid;

    /// <summary>
    /// The dot colour as one bool per bucket, because Avalonia style classes take a bool and have no
    /// equivalent of WPF's value-matching <c>DataTrigger</c>. Attention wins, matching the order the
    /// WPF triggers are declared in — the kinds happen to be disjoint, but the precedence is the
    /// intent, not an accident of the enum.
    /// </summary>
    public bool IsSettled => !NeedsAttention && Kind is SyncStatusKind.Synced;

    /// <inheritdoc cref="IsSettled" />
    public bool IsWorking => !NeedsAttention && Kind is SyncStatusKind.Syncing or SyncStatusKind.Pending;

    public string Label => Kind switch
    {
        SyncStatusKind.Synced => AppStrings.SyncChipSynced,
        SyncStatusKind.Syncing => AppStrings.SyncChipSyncing,
        SyncStatusKind.Pending => AppStrings.SyncChipPending,
        SyncStatusKind.Offline => AppStrings.SyncChipOffline,
        SyncStatusKind.Locked => AppStrings.SyncChipLocked,
        SyncStatusKind.Error => AppStrings.SyncChipError,
        SyncStatusKind.Unpaid => AppStrings.SyncChipUnpaid,
        _ => string.Empty,
    };
}
