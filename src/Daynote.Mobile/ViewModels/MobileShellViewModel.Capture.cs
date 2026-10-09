using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Notes;
using Daynote.Core.Agenda;
using Daynote.Core.Time;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The phone's half of the <c>@</c> command: the bar that takes the keyboard accessory slot while
/// a phrase is being read (phone §01).
/// </summary>
/// <remarks>
/// A bar, not the desktop's card. The caret on a phone is almost always just above the keyboard,
/// so a popup that follows it would cover the words it is reading back. The accessory slot is
/// already where the editor's helpers live, it never moves, and it is under the thumb.
/// <para>
/// Everything that decides anything — whether a bar is open, what the two lines say, what Create
/// makes — is <see cref="AgendaCapture"/> and <see cref="AgendaReadback"/>, the same two the
/// desktop popup asks. Only the width differs, and that is a composition setting.
/// </para>
/// </remarks>
public sealed partial class MobileShellViewModel
{
    /// <summary>The bar's state, which is the editor's: one reading, drawn two ways.</summary>
    public AgendaCaptureViewModel Capture => Notes.Capture;

    /// <summary>
    /// True when the bar has to fold to one line: a small screen, or a keyboard that leaves less
    /// than the design's 230 points above it. Set by the view, which is the only thing that knows
    /// where the keyboard ended up.
    /// </summary>
    [ObservableProperty]
    private bool _isCaptureBarCompact;

    /// <summary>A tap on one of the two readback lines, which is what the phone has instead of Tab.</summary>
    [RelayCommand]
    private void SelectCaptureKind(AgendaKind kind) => Capture.SelectKind(kind);

    /// <summary>The ×. Creates nothing and leaves the text as typed (§7).</summary>
    [RelayCommand]
    private void DismissCapture() => Capture.Dismiss();

    /// <summary>
    /// 만들기, from the button on the selected line or from the keyboard's return key.
    /// </summary>
    /// <remarks>
    /// The body is left exactly as it was typed. Nothing is cut out of the note, so dismissing the
    /// bar afterwards costs nothing and an item made by mistake is a row to delete rather than an
    /// edit to undo.
    /// </remarks>
    [RelayCommand]
    private async Task CommitCapture()
    {
        if (await Notes.CommitCaptureAsync().ConfigureAwait(true) is not { } made)
        {
            return;
        }

        await RefreshTodosAsync().ConfigureAwait(true);

        // §02: the confirmation is the thing itself turning up in the note's own collection.
        ClockSnapshot snapshot = _clock.Read();
        await FlashJustMadeAsync(made, snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset).DateTime)
            .ConfigureAwait(true);
    }
}
