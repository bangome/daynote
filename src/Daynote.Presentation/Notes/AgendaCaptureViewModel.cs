using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;
using Daynote.Core.Agenda;

namespace Daynote.App.Notes;

/// <summary>
/// The <c>@</c> popup, as the editor binds to it (docs/TODOS.md §7,
/// docs/design-renewal/Daynote B Tasks - Events 01 - Command).
/// </summary>
/// <remarks>
/// A thin skin over <see cref="AgendaCapture"/> and <see cref="AgendaReadback"/>: everything that
/// decides anything is in those two, and what is here is only what a view can bind to. The phone's
/// keyboard bar will bind to the same thing at <see cref="ReadbackWidth.Compact"/>.
/// <para>
/// It is told where the caret is rather than watching the text itself, because only the view knows
/// that — WPF's caret moves for reasons no view model can see.
/// </para>
/// </remarks>
public sealed partial class AgendaCaptureViewModel(ReadbackWidth width = ReadbackWidth.Full)
    : ObservableObject
{
    private AgendaCaptureState state;

    /// <summary>True while the popup should be on screen.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>True in the state that offers examples because nothing has been typed yet.</summary>
    [ObservableProperty]
    public partial bool IsPrompting { get; private set; }

    /// <summary>The line to the left of the <c>@</c>, which becomes the item's title.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TaskLine { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EventLine { get; private set; } = string.Empty;

    /// <summary>The sentence under both lines, or empty. Never says anything uncontroversial.</summary>
    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>What the popup invites when nothing has been typed.</summary>
    public string Prompt => AgendaReadback.EmptyPrompt(width);

    /// <summary>The example chips beside that prompt.</summary>
    public IReadOnlyList<string> Examples => AgendaReadback.Examples;

    /// <summary>
    /// Which readback line is selected, and therefore what Enter makes. Kept across openings: a
    /// user who is booking a morning of meetings should not have to press Tab for each one.
    /// </summary>
    [ObservableProperty]
    public partial AgendaKind Kind { get; private set; } = AgendaKind.Task;

    public bool IsTaskSelected => Kind == AgendaKind.Task;

    public bool IsEventSelected => Kind == AgendaKind.Event;

    /// <summary>Where in the body the <c>@</c> is, for the view to hang the popup off.</summary>
    public int AtIndex => state.AtIndex;

    /// <summary>How much of the body the chip covers once the item is made.</summary>
    public int ChipLength => state.ChipLength;

    /// <summary>Hints shown in the footer, which name real keys and so are not decoration.</summary>
    public string KeyHints => AppStrings.AgendaCaptureKeyHints;

    /// <summary>
    /// Re-reads the body under the caret. Called on every keystroke and every caret move, because
    /// either can open or close the popup.
    /// </summary>
    public void Update(string? text, int caret, DateTime now)
    {
        if (AgendaCapture.Detect(text, caret, now) is not { } detected)
        {
            IsOpen = false;
            return;
        }

        state = detected;
        IsOpen = true;
        IsPrompting = detected.IsPrompting;
        Title = detected.Title;

        if (detected.Reading is { } reading)
        {
            AgendaReadbackLines lines = AgendaReadback.Describe(reading, width);
            TaskLine = lines.Task;
            EventLine = lines.Event;
            Note = lines.Note;
        }
        else
        {
            TaskLine = string.Empty;
            EventLine = string.Empty;
            Note = string.Empty;
        }
    }

    /// <summary>Tab. The two lines describe the same reading, so switching has to be cheap.</summary>
    public void ToggleKind()
    {
        Kind = Kind == AgendaKind.Task ? AgendaKind.Event : AgendaKind.Task;
        OnPropertyChanged(nameof(IsTaskSelected));
        OnPropertyChanged(nameof(IsEventSelected));
    }

    /// <summary>Esc. Creates nothing and leaves the text exactly as it was typed (§7).</summary>
    public void Dismiss() => IsOpen = false;

    /// <summary>
    /// Enter. Null while the popup is only prompting, which is also what stops Enter from
    /// swallowing a newline before anything has been read.
    /// </summary>
    public AgendaItem? Create(Guid noteId, DateTimeOffset now)
    {
        if (!IsOpen || state.Reading is null)
        {
            return null;
        }

        AgendaItem made = AgendaCapture.Compose(state, Kind, noteId, Guid.NewGuid(), now);
        IsOpen = false;
        return made;
    }
}
