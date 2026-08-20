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
    public bool NeedsAttention => Kind is SyncStatusKind.Locked or SyncStatusKind.Error;

    public string Label => Kind switch
    {
        SyncStatusKind.Synced => AppStrings.SyncChipSynced,
        SyncStatusKind.Syncing => AppStrings.SyncChipSyncing,
        SyncStatusKind.Pending => AppStrings.SyncChipPending,
        SyncStatusKind.Offline => AppStrings.SyncChipOffline,
        SyncStatusKind.Locked => AppStrings.SyncChipLocked,
        SyncStatusKind.Error => AppStrings.SyncChipError,
        _ => string.Empty,
    };
}
