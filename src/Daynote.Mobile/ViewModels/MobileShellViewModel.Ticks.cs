using Daynote.Motion;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The to-do ticks drawn but not yet written (motion spec M3), held here rather than by the
/// checkboxes, which a rebuild of the lists replaces mid-wait. <see cref="FlushAsync"/> writes them.
/// </summary>
public sealed partial class MobileShellViewModel : IPendingTickOwner
{
    public PendingTicks Ticks { get; } = new();
}
