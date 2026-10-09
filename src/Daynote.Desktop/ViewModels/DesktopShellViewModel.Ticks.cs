using Daynote.Motion;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The to-do ticks drawn but not yet written (motion spec M3), held here rather than by the
/// checkboxes, which a rebuild of the day panel replaces mid-wait. Quitting writes them first
/// (App.axaml.cs).
/// </summary>
public sealed partial class DesktopShellViewModel : IPendingTickOwner
{
    public PendingTicks Ticks { get; } = new();
}
