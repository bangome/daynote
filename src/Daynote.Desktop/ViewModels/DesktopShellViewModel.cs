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

    partial void OnAccountChanged(Daynote.App.Account.AccountViewModel? value)
    {
        OnPropertyChanged(nameof(HasAccount));
        RefreshAccountBar();

        // The strip mirrors the account's own state, so it has to follow the account's notifications
        // as well as its arrival. Without this the headline stays on "Sign in" after a sign-in.
        if (value is not null)
        {
            value.PropertyChanged += (_, _) => RefreshAccountBar();
        }
    }

    private void RefreshAccountBar()
    {
        OnPropertyChanged(nameof(AccountBarTitle));
        OnPropertyChanged(nameof(AccountBarShowsAvatar));
        OnPropertyChanged(nameof(AccountBarSubtitle));
        OnPropertyChanged(nameof(AccountBarMenuLabel));
    }

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

    /// <summary>
    /// The account strip's headline: who is signed in, or the way to sign in, or — in a build with
    /// no sync endpoint — what is actually true about where the notes live.
    /// </summary>
    public string AccountBarTitle => Account switch
    {
        { IsSignedIn: true } account => account.DisplayName,
        not null => AppStrings.AccountBarSignIn,
        null => AppStrings.AccountBarLocal,
    };

    /// <summary>The sync state under the headline, or empty when there is nothing to report.</summary>
    public string AccountBarSubtitle =>
        Account is { IsSignedIn: true, Status.IsVisible: true } account ? account.Status.Label : string.Empty;

    /// <summary>
    /// Whether the strip has an identity to draw. It gates the parts that reach into the account, so
    /// those bindings are never evaluated against a null one — a <c>FallbackValue</c> supplies a value
    /// but still logs a binding error, and in a build with no sync endpoint that is every launch.
    /// </summary>
    public bool AccountBarShowsAvatar => Account is { IsSignedIn: true };

    /// <summary>The account row in the strip's menu: manage it, or sign in.</summary>
    public string AccountBarMenuLabel =>
        Account is { IsSignedIn: true } ? AppStrings.AccountManage : AppStrings.AccountBarSignIn;

    /// <summary>
    /// The titlebar wordmark, which depends on both the theme (the ink has to contrast with the
    /// ground) and the language (the lockup carries the product name).
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap BrandLogo =>
        Views.BrandLogos.For(Daynote.App.Localization.LocalizationService.Instance.Language, IsDark);

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
        OnPropertyChanged(nameof(BrandLogo));
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
        // Any navigation leaves the timeline. It is a view *of* the notes, not a place to be: once
        // the user has picked a day or a note from either panel, the thing they picked is what they
        // want to see. Every jump funnels through here, so this one line covers the calendar, the
        // todo / favourites / tag rows, search, and Today.
        //
        // Before the same-date early return on purpose: clicking the day you are already on is still
        // a request to look at it. Before the flush too, because a flush that fails leaves the editor
        // showing the note and its retry button, which is exactly what the user needs to see.
        IsTimelineMode = false;

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

    /// <summary>
    /// Picking a note from the day list, which is the one navigation that does not change the date
    /// and so does not pass through <see cref="SelectDateAsync"/>. The list stays on screen in the
    /// timeline, so clicking a row there has to open it.
    /// </summary>
    [RelayCommand]
    private Task SelectDayNote(NoteTabViewModel? tab)
    {
        IsTimelineMode = false;
        return Notes.SelectNoteAsync(tab);
    }

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
    private Task DeleteSelectedNote() => DeleteDayNote(Notes.SelectedTab);

    /// <summary>
    /// Deletes a specific row — the context menu's and the Delete key's version of
    /// <see cref="DeleteSelectedNote"/>, which act on the row under the pointer rather than the open note.
    /// </summary>
    [RelayCommand]
    private async Task DeleteDayNote(NoteTabViewModel? tab)
    {
        if (tab is not null && await Notes.DeleteNoteAsync(tab).ConfigureAwait(true))
        {
            await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DuplicateDayNote(NoteTabViewModel? tab)
    {
        IsTimelineMode = false;
        if (await Notes.DuplicateNoteAsync(tab).ConfigureAwait(true))
        {
            await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Everything that counts or lists notes, after one was added, copied or removed.</summary>
    private async Task RefreshAfterStructureChangeAsync()
    {
        RefreshHeader();
        await Calendar.LoadAsync().ConfigureAwait(true);
        await Todo.RefreshAsync().ConfigureAwait(true);
        await Favorites.RefreshAsync().ConfigureAwait(true);
        await TagPanel.RefreshAsync().ConfigureAwait(true);
    }

    // ── Title rename ─────────────────────────────────────────────────────────────────────────────
    // The title is a label until the user asks to change it: double-click on the heading or "이름
    // 변경" on a row. Then it is a text box holding a draft, committed on Enter or focus loss and
    // dropped on Escape. The draft is separate from the tab's title so Escape really does cancel.

    [ObservableProperty]
    private bool _isRenamingTitle;

    [ObservableProperty]
    private string _titleDraft = string.Empty;

    /// <summary>Selects the row, then opens the title for editing.</summary>
    [RelayCommand]
    private async Task RenameDayNote(NoteTabViewModel? tab)
    {
        IsTimelineMode = false;
        if (tab is not null && await Notes.SelectNoteAsync(tab).ConfigureAwait(true))
        {
            BeginRenameTitle();
        }
    }

    public void BeginRenameTitle()
    {
        if (Notes.SelectedTab is { } tab)
        {
            TitleDraft = tab.Title;
            IsRenamingTitle = true;
        }
    }

    [RelayCommand]
    private async Task CommitRenameTitle()
    {
        if (!IsRenamingTitle)
        {
            return;
        }

        IsRenamingTitle = false;
        string draft = TitleDraft.Trim();
        if (Notes.SelectedTab is { } tab && draft.Length > 0 && !string.Equals(draft, tab.Title, StringComparison.Ordinal))
        {
            await Notes.RenameAsync(tab, draft).ConfigureAwait(true);
            RefreshHeader();
        }
    }

    [RelayCommand]
    private void CancelRenameTitle() => IsRenamingTitle = false;

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
        if (Notes.SelectedTab is { } tab && !string.IsNullOrWhiteSpace(tag)
            && await Notes.AddTagAsync(tab, tag).ConfigureAwait(true))
        {
            // Refreshing here, not on the save that ReplaceTagsAsync flushes first: that flush
            // raises Saved, the panel refresh hung off it runs before the tags are written, and the
            // list comes back showing the state from a moment ago. Which is why a new tag only
            // appeared after something else happened to refresh the panel.
            await TagPanel.RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RemoveTag(string? tag)
    {
        if (!string.IsNullOrEmpty(tag) && Notes.SelectedTab is { } tab
            && await Notes.RemoveTagAsync(tab, tag).ConfigureAwait(true))
        {
            await TagPanel.RefreshAsync().ConfigureAwait(true);
        }
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

    void ILanguageAware.OnLanguageChanged()
    {
        RefreshHeader();
        RefreshAccountBar();
        OnPropertyChanged(nameof(BrandLogo));
    }

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
