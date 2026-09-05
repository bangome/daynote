using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The 태그 tab: gathers inline <c>#tag</c> tokens from EVERY note body across ALL dates
/// (<see cref="INoteRepository.GetAllNotesAsync"/>) via <see cref="TagParsing"/>, listing each distinct tag
/// with its total occurrence count. This is a separate system from the per-note tag chips. Jumping to an
/// occurrence is delegated to the shell, which navigates and selects the tag in the editor.
/// </summary>
public sealed partial class TagPanelViewModel : ObservableObject, ILanguageAware
{
    private readonly INoteRepository _repository;
    private readonly Func<TagOccurrence, Task> _onJump;

    public TagPanelViewModel(INoteRepository repository, Func<TagOccurrence, Task> onJump)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onJump = onJump ?? throw new ArgumentNullException(nameof(onJump));
        LocalizationService.Instance.Observe(this);
    }

    /// <summary>Everything visible here is catalog-derived, so re-read every binding.</summary>
    void ILanguageAware.OnLanguageChanged() => OnPropertyChanged(string.Empty);

    public ObservableCollection<TagItemViewModel> Tags { get; } = [];

    [ObservableProperty]
    private int _tagCount;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Tab header label "태그 (N)"; recomputed whenever the distinct-tag count changes.</summary>
    public string TabLabel => string.Format(CultureInfo.CurrentCulture, AppStrings.TabTagsFormat, TagCount);

    partial void OnTagCountChanged(int value) => OnPropertyChanged(nameof(TabLabel));

    /// <summary>Re-parses inline tags across all notes. Called on load and after any note-body change.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NoteSummary> notes = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<TagSummary> summaries = TagParsing.Aggregate(TagParsing.Parse(notes));

        Tags.Clear();
        foreach (TagSummary summary in summaries)
        {
            Tags.Add(new TagItemViewModel(summary, _onJump));
        }

        TagCount = Tags.Count;
        IsEmpty = Tags.Count == 0;
    }
}
