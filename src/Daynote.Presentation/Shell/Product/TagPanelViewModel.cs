using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The 태그 tab: every tag the user has put on a note, across all dates, with the notes that carry it.
/// </summary>
/// <remarks>
/// It used to scan note bodies for inline <c>#tag</c> tokens instead, which meant the app had two tag
/// systems and this panel showed the one the user could not see in the tag row — a note tagged with
/// the chips under its title appeared nowhere. There is one system now: <c>note_tags</c>, the chips.
/// Opening a row is delegated to the shell, which navigates to the note.
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

    /// <summary>Rebuilds the list. Called on load and whenever a note's tags change.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NoteSummary> notes = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<NoteTagLink> links = await _repository.GetAllNoteTagsAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<TagSummary> summaries = NoteTagIndex.Build(notes, links);

        Tags.Clear();
        foreach (TagSummary summary in summaries)
        {
            Tags.Add(new TagItemViewModel(summary, _onJump));
        }

        TagCount = Tags.Count;
        IsEmpty = Tags.Count == 0;
    }
}
