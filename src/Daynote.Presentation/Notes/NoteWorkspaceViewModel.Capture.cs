using Daynote.Core.Agenda;

namespace Daynote.App.Notes;

/// <summary>
/// The <c>@</c> command, as the note workspace owns it (docs/TODOS.md §7).
/// </summary>
/// <remarks>
/// The popup lives with the editor because that is the only place that knows where the caret is,
/// and the item it makes is written straight through <see cref="IAgendaRepository"/> rather than
/// handed back to the shell: §7 asks for the object to appear, and the sooner it is a row the
/// sooner every reader of that row agrees about it.
/// </remarks>
public sealed partial class NoteWorkspaceViewModel
{
    /// <summary>The popup's state. Always present; <c>IsOpen</c> says whether it is on screen.</summary>
    public AgendaCaptureViewModel Capture { get; } = new();

    /// <summary>
    /// Re-reads the body under the caret. The view calls this on every keystroke and every caret
    /// move, because either can open or close the popup.
    /// </summary>
    public void UpdateCapture(int caret) =>
        Capture.Update(EditorText, caret, DateTime.Now);

    /// <summary>
    /// Enter: writes the item and answers with it, or null when there was nothing to make.
    /// </summary>
    /// <remarks>
    /// The body is not touched. §7 is explicit that what stays in the note is exactly what was
    /// typed — the chip the design draws is a rendering of that span, not a replacement for it,
    /// so creating an item is a pure addition and Esc genuinely costs nothing.
    /// </remarks>
    public async Task<AgendaItem?> CommitCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (agenda is null || SelectedTab is not { IsProjection: false, Id: { } noteId })
        {
            Capture.Dismiss();
            return null;
        }

        if (Capture.Create(noteId.Value, DateTimeOffset.UtcNow) is not { } made)
        {
            return null;
        }

        await agenda.SaveAsync(made, cancellationToken).ConfigureAwait(true);
        return made;
    }
}
