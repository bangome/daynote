using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One location where an inline tag occurs: the origin note and a line-context preview. Clicking it jumps
/// to that note and selects the tag in the editor, delegated to the shell through <c>_onJump</c>.
/// </summary>
public sealed partial class TagOccurrenceViewModel : ObservableObject
{
    private const int PreviewMaxLength = 80;

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

    /// <summary>The trimmed source line, ellipsized so long lines stay one row.</summary>
    public string Preview => _occurrence.LineText.Length > PreviewMaxLength
        ? string.Concat(_occurrence.LineText.AsSpan(0, PreviewMaxLength), "…")
        : _occurrence.LineText;

    [RelayCommand]
    private Task Jump() => _onJump(_occurrence);
}
