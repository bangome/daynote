using CommunityToolkit.Mvvm.Input;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The open note, in a window of its own. Split from the main partial to keep it reviewable; the
/// window listens for the event because only it can open another window.
/// </summary>
public sealed partial class ProductShellViewModel
{
    /// <summary>Raised when the post-it chord or button asks for the open note in its own window.</summary>
    public event EventHandler? StickyNoteRequested;

    [RelayCommand]
    private void OpenSticky()
    {
        if (HasOpenNote)
        {
            StickyNoteRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
