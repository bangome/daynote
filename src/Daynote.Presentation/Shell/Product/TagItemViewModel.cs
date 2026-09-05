using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One row in the 태그 panel: a distinct inline tag with its total occurrence count. Expanding the row
/// reveals every location the tag was found (<see cref="Occurrences"/>), each clickable to jump into the
/// editor at that position.
/// </summary>
public sealed partial class TagItemViewModel : ObservableObject
{
    public TagItemViewModel(TagSummary summary, Func<TagOccurrence, Task> onJump)
    {
        Tag = string.Concat("#", summary.Tag);
        Count = summary.Count;
        CountText = summary.Count.ToString(CultureInfo.CurrentCulture);
        foreach (TagOccurrence occurrence in summary.Occurrences)
        {
            Occurrences.Add(new TagOccurrenceViewModel(occurrence, onJump));
        }
    }

    /// <summary>The tag rendered with its leading '#', e.g. "#프로젝트".</summary>
    public string Tag { get; }

    public int Count { get; }

    public string CountText { get; }

    public ObservableCollection<TagOccurrenceViewModel> Occurrences { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}
