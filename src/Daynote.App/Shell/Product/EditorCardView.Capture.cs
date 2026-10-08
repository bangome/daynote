using System.Windows;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The editor's half of the <c>@</c> command (docs/TODOS.md §7, design §01).
/// </summary>
/// <remarks>
/// Here and not in the view model because all three things it does need the view: where the caret
/// is, what rectangle the character at an index occupies, and which keys arrived before the
/// TextBox saw them. Everything that decides anything is one call away in
/// <c>AgendaCaptureViewModel</c>.
/// </remarks>
public partial class EditorCardView
{
    /// <summary>
    /// A caret move can open or close the popup just as a keystroke can — clicking away from a
    /// half-typed "@내일" has to dismiss it — so both events ask the same question.
    /// </summary>
    private void OnBodySelectionChanged(object sender, RoutedEventArgs e) => RefreshCapture();

    private void RefreshCapture()
    {
        if (Shell?.Notes is not { } notes)
        {
            return;
        }

        notes.UpdateCapture(BodyBox.SelectionStart);
        if (notes.Capture.IsOpen)
        {
            PlaceCapturePopup();
        }
    }

    /// <summary>
    /// Hangs the popup off the @ itself rather than the caret, so it stays put while the phrase
    /// after it is typed; a popup that crept rightwards with every character would be read as a
    /// different popup. It flips above the line when there is no room below, which near the
    /// bottom of the window is the common case rather than the exception.
    /// </summary>
    private void PlaceCapturePopup()
    {
        if (Shell?.Notes is not { } notes)
        {
            return;
        }

        int anchor = Math.Clamp(notes.Capture.AtIndex, 0, Math.Max(BodyBox.Text.Length - 1, 0));
        Rect caret = BodyBox.GetRectFromCharacterIndex(anchor);
        if (caret.IsEmpty)
        {
            return;
        }

        const double PopupHeight = 150;
        double below = BodyBox.ActualHeight - caret.Bottom;

        CapturePopup.HorizontalOffset = caret.Left;
        CapturePopup.VerticalOffset = below >= PopupHeight
            ? caret.Bottom
            : caret.Top - PopupHeight;
    }

    /// <summary>
    /// The three keys the popup claims while it is open, and only while it is open: Tab switches
    /// the kind, Enter creates, Esc dismisses. Each is a key the editor would otherwise use, so
    /// none of them may be swallowed when there is no popup.
    /// </summary>
    private async void OnBodyPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (Shell?.Notes is not { Capture.IsOpen: true } notes)
        {
            return;
        }

        switch (e.Key)
        {
            case WpfKey.Tab:
                notes.Capture.ToggleKind();
                e.Handled = true;
                break;

            case WpfKey.Escape:
                // §7: creates nothing and leaves the text. @ was an ordinary character all along.
                notes.Capture.Dismiss();
                e.Handled = true;
                break;

            case WpfKey.Enter:
                if (notes.Capture.IsPrompting)
                {
                    // Nothing has been read yet, so Enter is still a newline. Swallowing it here
                    // would make the editor feel stuck the moment an @ was typed.
                    return;
                }

                e.Handled = true;
                await notes.CommitCaptureAsync().ConfigureAwait(true);
                break;

            default:
                break;
        }
    }

}
