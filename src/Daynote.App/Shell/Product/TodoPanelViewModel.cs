using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Time;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The 할 일 tab: parses <c>-[]</c> checkbox lines from EVERY note across ALL dates
/// (<see cref="INoteRepository.GetAllNotesAsync"/>) via <see cref="TodoParsing"/>, unchecked-first then by
/// due. Toggling and jumping are delegated to the shell, which persists the body rewrite and re-navigates.
/// </summary>
public sealed partial class TodoPanelViewModel : ObservableObject, ILanguageAware
{
    private readonly INoteRepository _repository;
    private readonly IClock _clock;
    private readonly Func<TodoLine, Task> _onToggle;
    private readonly Func<TodoLine, Task> _onJump;

    public TodoPanelViewModel(
        INoteRepository repository,
        IClock clock,
        Func<TodoLine, Task> onToggle,
        Func<TodoLine, Task> onJump)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _onToggle = onToggle ?? throw new ArgumentNullException(nameof(onToggle));
        _onJump = onJump ?? throw new ArgumentNullException(nameof(onJump));
        LocalizationService.Instance.Observe(this);
    }


    /// <summary>Everything visible here is catalog-derived, so re-read every binding.</summary>
    void ILanguageAware.OnLanguageChanged() => OnPropertyChanged(string.Empty);

    /// <summary>Unchecked todos whose due date is today, grouped under the "오늘" heading on top.</summary>
    public ObservableCollection<TodoItemViewModel> TodayItems { get; } = [];

    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private int _openCount;

    [ObservableProperty]
    private bool _hasToday;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Tab header label "할 일 (N)"; recomputed whenever the open count changes.</summary>
    public string TabLabel => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, Localization.AppStrings.TabTodoFormat, OpenCount);

    partial void OnOpenCountChanged(int value) => OnPropertyChanged(nameof(TabLabel));

    /// <summary>Re-parses todos across all notes. Called on load and after any note-body change.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ClockSnapshot snapshot = _clock.Read();
        DateTimeOffset now = snapshot.UtcInstant.ToOffset(snapshot.LocalUtcOffset);
        IReadOnlyList<NoteSummary> notes = await _repository.GetAllNotesAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<TodoLine> lines = TodoParsing.Parse(notes, now);

        TodayItems.Clear();
        Items.Clear();
        int open = 0;
        DateTime today = now.Date;
        foreach (TodoLine line in lines)
        {
            var item = new TodoItemViewModel(line, _onToggle, _onJump);
            bool dueToday = !line.Checked && line.Due is { } due && due.Date == today;
            (dueToday ? TodayItems : Items).Add(item);
            if (!line.Checked)
            {
                open++;
            }
        }

        OpenCount = open;
        HasToday = TodayItems.Count > 0;
        IsEmpty = TodayItems.Count == 0 && Items.Count == 0;
    }
}
