using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Lifecycle;
using Daynote.App.Notes;
using Daynote.App.Settings;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Settings;
using Daynote.Core.Time;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Top-level view model for the Calendar Notes product shell. Owns the single selected date and drives the
/// calendar, note list/editor (reusing <see cref="NoteWorkspaceViewModel"/> and its autosave/flush
/// guards), the 할 일/파일 panel, and the unified search dropdown. Theme and panel-collapse state persist
/// through <see cref="ISettingsStore"/>. Settings and tray behavior are reused unchanged.
/// </summary>
public sealed partial class ProductShellViewModel : ObservableObject, IAsyncDisposable
{
    private const string ThemeKey = "product.theme";
    private const string LeftCollapsedKey = "product.left-collapsed";
    private const string RightCollapsedKey = "product.right-collapsed";

    private readonly IClock _clock;
    private readonly INoteRepository _repository;
    private readonly ISettingsStore _settings;
    private readonly IThemeApplier _themeApplier;
    private bool _loading;
    private bool _disposed;
    private CancellationTokenSource? _todoRefreshCts;

    public ProductShellViewModel(
        NoteWorkspaceViewModel notes,
        IClock clock,
        SearchService searchService,
        INoteRepository repository,
        AddDayFile addDayFile,
        ListDayFiles listDayFiles,
        DeleteDayFile deleteDayFile,
        IFileAssetStore fileAssetStore,
        IFilePicker filePicker,
        ISettingsStore settings,
        IThemeApplier themeApplier)
    {
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _themeApplier = themeApplier ?? throw new ArgumentNullException(nameof(themeApplier));
        ArgumentNullException.ThrowIfNull(searchService);

        Calendar = new CalendarMonthViewModel(clock, repository, SelectDateFromCalendarAsync);
        Todo = new TodoPanelViewModel(repository, clock, ToggleTodoAsync, JumpToTodoAsync);
        TagPanel = new TagPanelViewModel(repository, JumpToTagAsync);
        Files = new FilesPanelViewModel(addDayFile, listDayFiles, deleteDayFile, fileAssetStore, filePicker);
        Search = new SearchDropdownViewModel(searchService, repository, NavigateAsync);
        Timeline = new TimelineViewModel(repository, OpenFromTimelineAsync);

        _selectedDate = LocalDates.Today(clock);
        Notes.PropertyChanged += OnNotesPropertyChanged;
        Localization.LocalizationService.Instance.Observe(this);
    }

    public NoteWorkspaceViewModel Notes { get; }

    public CalendarMonthViewModel Calendar { get; }

    public TodoPanelViewModel Todo { get; }

    public TagPanelViewModel TagPanel { get; }

    public FilesPanelViewModel Files { get; }

    /// <summary>Raised to ask the editor view to select and scroll to a body span (start, length).</summary>
    public event Action<int, int>? EditorSelectRequested;

    public SearchDropdownViewModel Search { get; }

    public TimelineViewModel Timeline { get; }

    [ObservableProperty]
    private SettingsViewModel? _settingsViewModel;

    [ObservableProperty]
    private Onboarding.TutorialViewModel? _tutorial;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private LocalDate _selectedDate;

    [ObservableProperty]
    private string _dayLabel = string.Empty;

    [ObservableProperty]
    private string _noteCountText = string.Empty;

    /// <summary>Observable note-list emptiness for the "이 날짜에 노트가 없습니다" message (ProjectionOnly is not observable).</summary>
    [ObservableProperty]
    private bool _isDayEmpty = true;

    [ObservableProperty]
    private bool _isDark;

    [ObservableProperty]
    private RightTab _activeTab = RightTab.Todo;

    [ObservableProperty]
    private string _tagInput = string.Empty;

    /// <summary>True when the body shows the read-only cross-date timeline instead of the editor layout.</summary>
    [ObservableProperty]
    private bool _isTimelineMode;

    /// <summary>The normal editor layout is shown whenever the timeline is not.</summary>
    public bool IsEditorMode => !IsTimelineMode;

    partial void OnIsTimelineModeChanged(bool value) => OnPropertyChanged(nameof(IsEditorMode));

    /// <summary>True when a persisted note is open in the editor; a bare projection reads as "no note"
    /// so an empty date shows the editor empty-state and the note-list empty message (design fidelity).</summary>
    public bool HasOpenNote => Notes.SelectedTab is { IsProjection: false };

    public bool TabIsTodo => ActiveTab == RightTab.Todo;

    public bool TabIsTags => ActiveTab == RightTab.Tags;

    public bool TabIsFiles => ActiveTab == RightTab.Files;

    partial void OnActiveTabChanged(RightTab value)
    {
        OnPropertyChanged(nameof(TabIsTodo));
        OnPropertyChanged(nameof(TabIsTags));
        OnPropertyChanged(nameof(TabIsFiles));
    }

    partial void OnIsDarkChanged(bool value)
    {
        _themeApplier.Apply(value);
        if (!_loading)
        {
            _ = _settings.SetAsync(ThemeKey, value ? "dark" : "light");
        }
    }

    /// <summary>Loads persisted theme/collapse, then today's workspace across every surface.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;
        try
        {
            string? theme = await _settings.GetAsync(ThemeKey, cancellationToken).ConfigureAwait(true);
            IsDark = string.Equals(theme, "dark", StringComparison.Ordinal);
            _themeApplier.Apply(IsDark);
            LeftCollapsed = await _settings.GetBoolAsync(LeftCollapsedKey, false, cancellationToken).ConfigureAwait(true);
            RightCollapsed = await _settings.GetBoolAsync(RightCollapsedKey, false, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loading = false;
        }

        LocalDate today = LocalDates.Today(_clock);
        SelectedDate = today;
        await Notes.LoadAsync(today, cancellationToken).ConfigureAwait(true);
        await Files.LoadForDateAsync(today, cancellationToken).ConfigureAwait(true);
        await Calendar.ShowSelectedAsync(today, cancellationToken).ConfigureAwait(true);
        await Todo.RefreshAsync(cancellationToken).ConfigureAwait(true);
        await TagPanel.RefreshAsync(cancellationToken).ConfigureAwait(true);
        RefreshHeader();
    }

    /// <summary>Switches the selected date after an autosave-safe flush; cancels on save failure.</summary>
    public async Task<bool> SelectDateAsync(LocalDate date, CancellationToken cancellationToken = default)
    {
        if (date == SelectedDate)
        {
            Calendar.SyncSelection(date);
            return true;
        }

        FlushResult flush = await Notes.FlushAsync(FlushReason.DateChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        SelectedDate = date;
        await Notes.LoadAsync(date, cancellationToken).ConfigureAwait(true);
        await Files.LoadForDateAsync(date, cancellationToken).ConfigureAwait(true);
        if (date.Year == Calendar.CursorYear && date.Month == Calendar.CursorMonth)
        {
            Calendar.SyncSelection(date);
        }
        else
        {
            await Calendar.ShowSelectedAsync(date, cancellationToken).ConfigureAwait(true);
        }

        RefreshHeader();
        return true;
    }

    private Task SelectDateFromCalendarAsync(LocalDate date) => SelectDateAsync(date);

    [RelayCommand]
    private Task GoToToday() => SelectDateAsync(LocalDates.Today(_clock));

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    [RelayCommand]
    private void SelectTab(RightTab tab) => ActiveTab = tab;

    /// <summary>
    /// A body file-link click: expand the right panel if collapsed (through the same user-toggle event
    /// so the window resizes consistently), switch to the 파일 tab, and highlight the newest matching
    /// card. A dangling name still opens the tab — it just highlights nothing.
    /// </summary>
    public void RevealFile(string displayName)
    {
        EnsureRightExpanded();
        ActiveTab = RightTab.Files;
        Files.Highlight(displayName);
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    public void OpenSettings() => IsSettingsOpen = true;

    public void CloseSettings() => IsSettingsOpen = false;

    /// <summary>Creates a note on the selected date and refreshes the calendar count and todo panel.</summary>
    [RelayCommand]
    private async Task NewNote()
    {
        if (await Notes.AddNoteAsync().ConfigureAwait(true))
        {
            RefreshHeader();
            await Calendar.LoadAsync().ConfigureAwait(true);
            await Todo.RefreshAsync().ConfigureAwait(true);
            await TagPanel.RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Deletes a note, then refreshes the calendar count and todo panel.</summary>
    [RelayCommand]
    private async Task DeleteSelectedNote()
    {
        if (Notes.SelectedTab is { } tab && await Notes.DeleteNoteAsync(tab).ConfigureAwait(true))
        {
            RefreshHeader();
            await Calendar.LoadAsync().ConfigureAwait(true);
            await Todo.RefreshAsync().ConfigureAwait(true);
            await TagPanel.RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task ToggleFavorite() => Notes.ToggleFavoriteAsync(Notes.SelectedTab, CancellationToken.None);

    [RelayCommand]
    private async Task CommitTag()
    {
        string tag = TagInput;
        TagInput = string.Empty;
        if (Notes.SelectedTab is { } tab)
        {
            await Notes.AddTagAsync(tab, tag).ConfigureAwait(true);
        }
    }

    public Task RemoveTagAsync(string tag) =>
        Notes.SelectedTab is { } tab ? Notes.RemoveTagAsync(tab, tag) : Task.FromResult(false);

    [RelayCommand]
    private Task RemoveTag(string? tag) =>
        string.IsNullOrEmpty(tag) ? Task.CompletedTask : RemoveTagAsync(tag);

    private async Task ToggleTodoAsync(TodoLine line)
    {
        DomainResult<NoteId> id = NoteId.Create(line.NoteId);
        if (!id.IsSuccess)
        {
            return;
        }

        DayWorkspace workspace = await _repository.GetDayWorkspaceStateAsync(line.Date).ConfigureAwait(true);
        Note? note = workspace.Notes.Notes.FirstOrDefault(n => !n.IsProjection && n.Id == id.Value);
        if (note is null)
        {
            return;
        }

        string newBody = TodoParsing.ToggleLine(note.Body, line.LineIndex);
        if (string.Equals(newBody, note.Body, StringComparison.Ordinal))
        {
            return;
        }

        var request = new NoteSaveRequest(
            id.Value, line.Date, note.Title, newBody, workspace.RevisionOf(id.Value), IsNew: false, note.HasCustomTitle);
        try
        {
            await _repository.SaveNoteAsync(request).ConfigureAwait(true);
        }
        catch (RecoverableNoteException)
        {
            return;
        }

        if (line.Date == SelectedDate)
        {
            await Notes.LoadAsync(SelectedDate).ConfigureAwait(true);
        }

        await Todo.RefreshAsync().ConfigureAwait(true);
        await TagPanel.RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Enters the timeline (after an autosave-safe flush) or leaves it back to the editor.</summary>
    [RelayCommand]
    private async Task ToggleTimeline()
    {
        if (IsTimelineMode)
        {
            IsTimelineMode = false;
            return;
        }

        FlushResult flush = await Notes.FlushAsync(FlushReason.DateChange).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return;
        }

        await Timeline.LoadAsync().ConfigureAwait(true);
        IsTimelineMode = true;
    }

    /// <summary>Leaves timeline mode and opens the picked note in the editor on its own date.</summary>
    private async Task OpenFromTimelineAsync(Guid id, LocalDate date)
    {
        IsTimelineMode = false;
        if (await SelectDateAsync(date).ConfigureAwait(true))
        {
            DomainResult<NoteId> nid = NoteId.Create(id);
            if (nid.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(nid.Value).ConfigureAwait(true);
            }
        }
    }

    private async Task JumpToTodoAsync(TodoLine line)
    {
        if (await SelectDateAsync(line.Date).ConfigureAwait(true))
        {
            DomainResult<NoteId> id = NoteId.Create(line.NoteId);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
            }
        }
    }

    /// <summary>Navigates to the tag occurrence's note and selects the '#tag' span in the editor.</summary>
    private async Task JumpToTagAsync(TagOccurrence occ)
    {
        if (await SelectDateAsync(occ.Date).ConfigureAwait(true))
        {
            DomainResult<NoteId> id = NoteId.Create(occ.NoteId);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);

                // +1 covers the leading '#' that Tag omits.
                EditorSelectRequested?.Invoke(occ.CharIndex, occ.Tag.Length + 1);
            }
        }
    }

    private async Task NavigateAsync(SearchNavigation navigation)
    {
        if (!await SelectDateAsync(navigation.Date).ConfigureAwait(true))
        {
            return;
        }

        if (navigation.NoteId is { } noteId)
        {
            DomainResult<NoteId> id = NoteId.Create(noteId);
            if (id.IsSuccess)
            {
                await Notes.SelectNoteByIdAsync(id.Value).ConfigureAwait(true);
            }
        }

        if (navigation.Tab is { } tab)
        {
            ActiveTab = tab;
        }
    }

    private void OnNotesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteWorkspaceViewModel.SaveStatus) && Notes.SaveStatus == SaveStatusKind.Saved)
        {
            _ = Todo.RefreshAsync();
            _ = TagPanel.RefreshAsync();
        }
        else if (e.PropertyName == nameof(NoteWorkspaceViewModel.SelectedTab))
        {
            OnPropertyChanged(nameof(HasOpenNote));
        }
        else if (e.PropertyName == nameof(NoteWorkspaceViewModel.EditorText))
        {
            // Autosave persists the body without a "saved" signal, so debounce a todo re-parse just
            // past the autosave window; GetAllNotesAsync then reflects the latest checkbox lines.
            ScheduleTodoRefresh();
        }
    }

    private void ScheduleTodoRefresh()
    {
        _todoRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _todoRefreshCts = cts;
        _ = RefreshTodoAfterDelayAsync(cts.Token);
    }

    private async Task RefreshTodoAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken).ConfigureAwait(true);
            await Todo.RefreshAsync(cancellationToken).ConfigureAwait(true);
            await TagPanel.RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefreshHeader()
    {
        DayLabel = FormatDayLabel(SelectedDate);
        int count = Notes.ProjectionOnly ? 0 : Notes.Tabs.Count(t => !t.IsProjection);
        NoteCountText = string.Format(CultureInfo.CurrentCulture, Localization.AppStrings.NoteCountFormat, count);
        IsDayEmpty = count == 0;
    }

    private static string FormatDayLabel(LocalDate date) => LocalDates.DisplayDayHeading(date);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _todoRefreshCts?.Cancel();
        Notes.PropertyChanged -= OnNotesPropertyChanged;
        await Notes.DisposeAsync().ConfigureAwait(false);
    }
}
