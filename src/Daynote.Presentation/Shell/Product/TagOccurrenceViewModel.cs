using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One note carrying the tag: its title, its date and the start of its body. Clicking it opens that
/// note, delegated to the shell through <c>_onJump</c>.
/// </summary>
public sealed partial class TagOccurrenceViewModel : ObservableObject
{
    private readonly TagOccurrence _occurrence;
    private readonly Func<TagOccurrence, Task> _onJump;

    public TagOccurrenceViewModel(TagOccurrence occurrence, Func<TagOccurrence, Task> onJump)
    {
        _occurrence = occurrence;
        _onJump = onJump;
    }

    public string NoteTitle => _occurrence.NoteTitle;

    /// <summary>Compact month/day heading for the origin note's date ("7월 27일 (일)" / "Sun, Jul 27").</summary>
    public string DateLabel => LocalDates.DisplayDayHeading(_occurrence.Date);

    /// <summary>The start of the note body, already trimmed and ellipsized by the index.</summary>
    public string Preview => _occurrence.Preview;

    [RelayCommand]
    private Task Jump() => _onJump(_occurrence);
}
