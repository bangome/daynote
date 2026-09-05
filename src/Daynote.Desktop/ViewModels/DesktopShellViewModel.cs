using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Settings;
using Daynote.Core.Time;

namespace Daynote.Desktop.ViewModels;

/// <summary>
/// The Avalonia shell's top-level view model: one selected date driving the calendar and the note
/// workspace, the right-rail panels (todo, favorites, tags, files), the unified search dropdown, the
/// timeline, and the persisted theme. Every panel view model is the shared one from
/// <c>Daynote.Presentation</c>, so autosave, flush guards, parsing and search behave exactly as in the
/// WPF shell; this class only wires them together (mirroring <c>ProductShellViewModel</c>).
/// </summary>
public sealed partial class DesktopShellViewModel : ObservableObject, ILanguageAware, IAsyncDisposable
{
    /// <summary>Same keys as the WPF shell, so a shared database keeps one preference.</summary>
    private const string ThemeKey = "product.theme";
    private const string RightCollapsedKey = "product.right-collapsed";
    private const string LeftCollapsedKey = "product.left-collapsed";

    private readonly IClock _clock;
    private readonly INoteRepository _repository;
    private readonly ISettingsStore _settings;
    private readonly IThemeApplier _themeApplier;
    private bool _loading;
    private bool _disposed;
    private CancellationTokenSource? _todoRefreshCts;

    public DesktopShellViewModel(
        NoteWorkspaceViewModel notes,
        IClock clock,
        SearchService searchService,
        INoteRepository repository,
        AddDayFile addDayFile,
        ListDayFiles listDayFiles,
        DeleteDayFile deleteDayFile,
        IFileAssetStore fileAssetStore,
        IFilePicker filePicker,
        IThumbnailLoader thumbnails,
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
        Favorites = new FavoritesPanelViewModel(repository, OpenFavoriteAsync);
        TagPanel = new TagPanelViewModel(repository, JumpToTagAsync);
        Files = new FilesPanelViewModel(addDayFile, listDayFiles, deleteDayFile, fileAssetStore, filePicker, thumbnails);
        Search = new SearchDropdownViewModel(searchService, repository, NavigateAsync);
        Timeline = new TimelineViewModel(repository, OpenFromTimelineAsync);

        _selectedDate = LocalDates.Today(clock);
        Notes.PropertyChanged += OnNotesPropertyChanged;
        Notes.Tabs.CollectionChanged += (_, _) => RefreshHeader();
        LocalizationService.Instance.Observe(this);
    }

    public NoteWorkspaceViewModel Notes { get; }

    public CalendarMonthViewModel Calendar { get; }

    public TodoPanelViewModel Todo { get; }

    public FavoritesPanelViewModel Favorites { get; }

    public TagPanelViewModel TagPanel { get; }

    public FilesPanelViewModel Files { get; }

    public SearchDropdownViewModel Search { get; }

    public TimelineViewModel Timeline { get; }

    /// <summary>Raised to ask the editor view to select a body span (start, length), e.g. a tag hit.</summary>
    public event Action<int, int>? EditorSelectRequested;

    [ObservableProperty]
    private LocalDate _selectedDate;

    [ObservableProperty]
    private string _dayLabel = string.Empty;

    [ObservableProperty]
    private string _noteCountText = string.Empty;

    [ObservableProperty]
    private bool _isDayEmpty = true;

    [ObservableProperty]
    private bool _isDark;

    [ObservableProperty]
    private bool _rightCollapsed;

    [ObservableProperty]
    private bool _leftCollapsed;

    [ObservableProperty]
    private RightTab _activeTab = RightTab.Todo;

    [ObservableProperty]
    private string _tagInput = string.Empty;

    [ObservableProperty]
    private bool _isTimelineMode;

    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <summary>Set by composition after construction; null until then so the window binds safely.</summary>
    [ObservableProperty]
    private DesktopSettingsViewModel? _settingsViewModel;

    /// <summary>The cloud account, or null when this build has no sync endpoint (section hidden). Set by composition.</summary>
    [ObservableProperty]
    private Daynote.App.Account.AccountViewModel? _account;

    public bool HasAccount => Account is not null;

    partial void OnAccountChanged(Daynote.App.Account.AccountViewModel? value) => OnPropertyChanged(nameof(HasAccount));

    [ObservableProperty]
    private bool _isAccountOpen;

    [RelayCommand]
    private void OpenAccount()
    {
        if (Account is not null)
        {
            IsSettingsOpen = false;
            IsAccountOpen = true;
            _ = Account.RefreshBillingCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void CloseAccount() => IsAccountOpen = false;

    /// <summary>First-run onboarding (auto-shown once; re-openable from Settings). Set by composition.</summary>
    [ObservableProperty]
    private Daynote.App.Onboarding.TutorialViewModel? _tutorial;

    public bool IsEditorMode => !IsTimelineMode;

    public bool HasOpenNote => Notes.SelectedTab is { IsProjection: false };

    public bool TabIsTodo => ActiveTab == RightTab.Todo;

    public bool TabIsFavorites => ActiveTab == RightTab.Favorites;

    public bool TabIsTags => ActiveTab == RightTab.Tags;

    public bool TabIsFiles => ActiveTab == RightTab.Files;

    public string ThemeGlyph => IsDark ? "☀" : "☾";

    /// <summary>Catalog strings the window binds to; refreshed wholesale on a language switch.</summary>
    public AppStringsProxy Strings => AppStringsProxy.Instance;

    partial void OnActiveTabChanged(RightTab value)
    {
        OnPropertyChanged(nameof(TabIsTodo));
        OnPropertyChanged(nameof(TabIsFavorites));
        OnPropertyChanged(nameof(TabIsTags));
        OnPropertyChanged(nameof(TabIsFiles));
    }

    partial void OnIsTimelineModeChanged(bool value) => OnPropertyChanged(nameof(IsEditorMode));

    partial void OnIsDarkChanged(bool value)
    {
        _themeApplier.Apply(value);
        OnPropertyChanged(nameof(ThemeGlyph));
        if (!_loading)
        {
            _ = _settings.SetAsync(ThemeKey, value ? "dark" : "light");
        }
    }

    partial void OnRightCollapsedChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.SetBoolAsync(RightCollapsedKey, value);
        }
    }

    partial void OnLeftCollapsedChanged(bool value)
    {
        if (!_loading)
        {
            _ = _settings.SetBoolAsync(LeftCollapsedKey, value);
        }
    }

    /// <summary>Raised when a sticky-note window should open over the current note (window-level concern).</summary>
    public event EventHandler? StickyNoteRequested;

    [RelayCommand]
    private void OpenSticky()
    {
        if (HasOpenNote)
        {
            StickyNoteRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The quick-note chord: jump to today, create a note, and open it as a post-it.</summary>
    public async Task OpenQuickStickyNoteAsync()
    {
        await GoToTodayCommand.ExecuteAsync(null).ConfigureAwait(true);
        await NewNoteCommand.ExecuteAsync(null).ConfigureAwait(true);
        StickyNoteRequested?.Invoke(this, EventArgs.Empty);
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
            RightCollapsed = await _settings.GetBoolAsync(RightCollapsedKey, false, cancellationToken).ConfigureAwait(true);
            LeftCollapsed = await _settings.GetBoolAsync(LeftCollapsedKey, false, cancellationToken).ConfigureAwait(true);
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
        await Favorites.RefreshAsync(cancellationToken).ConfigureAwait(true);
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
    private void ToggleRight() => RightCollapsed = !RightCollapsed;

    [RelayCommand]
    private void ToggleLeft() => LeftCollapsed = !LeftCollapsed;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void OpenTutorial()
    {
        IsSettingsOpen = false;
        Tutorial?.Open();
    }

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    /// <summary>Every way of opening the panel (button, shortcut, toggle) re-reads the OS-owned state.</summary>
    partial void OnIsSettingsOpenChanged(bool value)
    {
        if (value)
        {
            _ = SettingsViewModel?.RefreshAsync();
        }
    }

    [RelayCommand]
    private void SelectTab(RightTab tab) => ActiveTab = tab;

    /// <summary>Creates a note on the selected date and refreshes the calendar count and panels.</summary>
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

    [RelayCommand]
    private async Task DeleteSelectedNote()
    {
        if (Notes.SelectedTab is { } tab && await Notes.DeleteNoteAsync(tab).ConfigureAwait(true))
        {
            RefreshHeader();
            await Calendar.LoadAsync().ConfigureAwait(true);
            await Todo.RefreshAsync().ConfigureAwait(true);
            await Favorites.RefreshAsync().ConfigureAwait(true);
            await TagPanel.RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ToggleFavorite()
    {
        if (await Notes.ToggleFavoriteAsync(Notes.SelectedTab, CancellationToken.None).ConfigureAwait(true))
        {
            await Favorites.RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task CommitTag()
    {
        string tag = TagInput;
        TagInput = string.Empty;
        if (Notes.SelectedTab is { } tab && !string.IsNullOrWhiteSpace(tag))
        {
            await Notes.AddTagAsync(tab, tag).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task RemoveTag(string? tag) =>
        string.IsNullOrEmpty(tag) || Notes.SelectedTab is not { } tab ? Task.CompletedTask : Notes.RemoveTagAsync(tab, tag);

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

    private void OnNotesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteWorkspaceViewModel.SaveStatus) && Notes.SaveStatus == SaveStatusKind.Saved)
        {
            _ = Todo.RefreshAsync();
            _ = Favorites.RefreshAsync();
            _ = TagPanel.RefreshAsync();
        }
        else if (e.PropertyName == nameof(NoteWorkspaceViewModel.SelectedTab))
        {
            OnPropertyChanged(nameof(HasOpenNote));
            RefreshHeader();
        }
        else if (e.PropertyName == nameof(NoteWorkspaceViewModel.EditorText))
        {
            ScheduleTodoRefresh();
        }
    }

    /// <summary>Autosave persists the body silently, so re-parse todos just past its window.</summary>
    private void ScheduleTodoRefresh()
    {
        _todoRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _todoRefreshCts = cts;
        _ = RefreshPanelsAfterDelayAsync(cts.Token);
    }

    private async Task RefreshPanelsAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken).ConfigureAwait(true);
            await Todo.RefreshAsync(cancellationToken).ConfigureAwait(true);
            await Favorites.RefreshAsync(cancellationToken).ConfigureAwait(true);
            await TagPanel.RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefreshHeader()
    {
        DayLabel = LocalDates.DisplayDayHeading(SelectedDate);
        int count = Notes.ProjectionOnly ? 0 : Notes.Tabs.Count(t => !t.IsProjection);
        NoteCountText = string.Format(CultureInfo.CurrentCulture, AppStrings.NoteCountFormat, count);
        IsDayEmpty = count == 0;
        OnPropertyChanged(nameof(HasOpenNote));
    }

    void ILanguageAware.OnLanguageChanged() => RefreshHeader();

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
