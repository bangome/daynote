using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The 즐겨찾기 tab (Daynote v3 design): every starred note across ALL dates
/// (<see cref="INoteRepository.GetAllNotesAsync"/>), newest date first. Opening a row is delegated to the
/// shell, which navigates to the note's date and selects it in the editor.
/// </summary>
public sealed partial class FavoritesPanelViewModel : ObservableObject, ILanguageAware
{
    private readonly INoteRepository _repository;
    private readonly Func<NoteSummary, Task> _onOpen;

    public FavoritesPanelViewModel(INoteRepository repository, Func<NoteSummary, Task> onOpen)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
        LocalizationService.Instance.Observe(this);
    }

    /// <summary>Everything visible here is catalog-derived, so re-read every binding.</summary>
    void ILanguageAware.OnLanguageChanged() => OnPropertyChanged(string.Empty);

    public ObservableCollection<FavoriteItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Re-reads starred notes. Called on load, after favorite toggles, and after note changes.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NoteSummary> notes = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);

        Items.Clear();
        // LocalDate is not IComparable, so sort on a comparable (Y, M, D) tuple projection.
        foreach (NoteSummary note in notes
            .Where(n => n.IsFavorite)
            .OrderByDescending(n => (n.LocalDate.Year, n.LocalDate.Month, n.LocalDate.Day))
            .ThenBy(n => n.SortOrder))
        {
            Items.Add(new FavoriteItemViewModel(note, _onOpen));
        }

        IsEmpty = Items.Count == 0;
    }
}

/// <summary>One starred note row: title, date heading, and a first-line preview; click to open.</summary>
public sealed partial class FavoriteItemViewModel : ObservableObject
{
    private const int PreviewMaxLength = 80;

    private readonly NoteSummary _note;
    private readonly Func<NoteSummary, Task> _onOpen;

    public FavoriteItemViewModel(NoteSummary note, Func<NoteSummary, Task> onOpen)
    {
        _note = note;
        _onOpen = onOpen;
    }

    public string Title => _note.Title;

    /// <summary>Compact month/day heading for the note's date ("7월 27일 (일)" / "Sun, Jul 27").</summary>
    public string DateLabel => LocalDates.DisplayDayHeading(_note.LocalDate);

    /// <summary>First non-empty body line, ellipsized; falls back to the localized "no content" text.</summary>
    public string Preview
    {
        get
        {
            string? line = _note.Body
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);
            if (string.IsNullOrEmpty(line))
            {
                return AppStrings.FavoritesPreviewEmpty;
            }

            return line.Length > PreviewMaxLength ? string.Concat(line.AsSpan(0, PreviewMaxLength), "…") : line;
        }
    }

    [RelayCommand]
    private Task Open() => _onOpen(_note);
}
