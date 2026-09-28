using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Settings;
using Daynote.Core.Time;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The phone shell: the same presentation view models the desktop uses, arranged for one screen at a
/// time.
/// </summary>
/// <remarks>
/// <para>
/// Written rather than shared with <c>DesktopShellViewModel</c> on purpose. The two agree on
/// everything below them — the workspace, the calendar, search, the to-do/tag/favourite panels are
/// the very same classes out of <c>Daynote.Presentation</c>, so a fix there fixes both — but the
/// shell itself is navigation, and the navigation models genuinely differ: the desktop has a left
/// rail, a right rail and a collapse state for each; the phone has four tabs and a note editor that
/// opens over the day. Sharing would have meant carrying three columns' worth of state onto a
/// screen that cannot show them.
/// </para>
/// <para>
/// Nothing here may reference Avalonia. The desktop shell does (its brand lockup is a
/// <c>Bitmap</c>), which is the other reason this is a separate class.
/// </para>
/// </remarks>
public sealed partial class MobileShellViewModel : ObservableObject, ILanguageAware, IAsyncDisposable
{
    /// <summary>The same settings key the desktop writes, so a synced device keeps one preference.</summary>
    private const string ThemeKey = "product.theme";

    private readonly IClock _clock;
    private readonly INoteRepository _repository;
    private readonly ISettingsStore _settings;
    private readonly IThemeApplier _themeApplier;
    private bool _loading;
    private bool _disposed;

    public MobileShellViewModel(
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

        _selectedDate = LocalDates.Today(clock);
        Notes.PropertyChanged += OnNotesPropertyChanged;
        Notes.Tabs.CollectionChanged += (_, _) => RefreshHeader();
        LocalizationService.Instance.Observe(this);
    }

    // ── Shared surfaces, straight out of Daynote.Presentation ────────────────────────────────────

    public NoteWorkspaceViewModel Notes { get; }

    public CalendarMonthViewModel Calendar { get; }

    public TodoPanelViewModel Todo { get; }

    public FavoritesPanelViewModel Favorites { get; }

    public TagPanelViewModel TagPanel { get; }

    public FilesPanelViewModel Files { get; }

    public SearchDropdownViewModel Search { get; }

    /// <summary>The cloud account, or null in a build with no sync endpoint. Set by composition.</summary>
    [ObservableProperty]
    private Daynote.App.Account.AccountViewModel? _account;

    public bool HasAccount => Account is not null;

    partial void OnAccountChanged(Daynote.App.Account.AccountViewModel? value)
    {
        OnPropertyChanged(nameof(HasAccount));
        RefreshAccountBar();
        if (value is not null)
        {
            value.PropertyChanged += (_, _) => RefreshAccountBar();
        }
    }

    private void RefreshAccountBar()
    {
        OnPropertyChanged(nameof(AccountBarTitle));
        OnPropertyChanged(nameof(AccountBarSubtitle));
        OnPropertyChanged(nameof(IsSignedIn));
    }

    public bool IsSignedIn => Account is { IsSignedIn: true };

    public string AccountBarTitle => Account switch
    {
        { IsSignedIn: true } account => account.DisplayName,
        not null => AppStrings.AccountBarSignIn,
        null => AppStrings.AccountBarLocal,
    };

    public string AccountBarSubtitle => Account is { IsSignedIn: true, Status.IsVisible: true } account
        // "Subscription needed" is a sales line the phone cannot follow up; say what is true instead.
        ? account.Status.Kind == Daynote.App.Account.SyncStatusKind.Unpaid ? AppStrings.SyncChipFilesOff : account.Status.Label
        : string.Empty;

    /// <summary>Catalog strings the views bind to; refreshed wholesale on a language switch.</summary>
    public MobileStrings Strings => MobileStrings.Instance;

    // ── Navigation ───────────────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    private MobilePage _page = MobilePage.Day;

    /// <summary>Whether the note editor is on screen, over whichever tab is selected.</summary>
    [ObservableProperty]
    private bool _isEditorOpen;

    public bool IsDayPage => Page == MobilePage.Day;

    public bool IsSearchPage => Page == MobilePage.Search;

    public bool IsListsPage => Page == MobilePage.Lists;

    public bool IsSettingsPage => Page == MobilePage.Settings;

    partial void OnPageChanged(MobilePage value)
    {
        OnPropertyChanged(nameof(IsDayPage));
        OnPropertyChanged(nameof(IsSearchPage));
        OnPropertyChanged(nameof(IsListsPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    [RelayCommand]
    private void GoToPage(MobilePage page)
    {
        // Leaving for another tab closes the editor: the phone's back gesture is the only thing that
        // should put the user back on the day, and a hidden editor holding an unsaved draft under a
        // tab they cannot see is exactly the state autosave exists to avoid.
        if (IsEditorOpen && page != Page)
        {
            _ = CloseEditorAsync();
        }

        Page = page;

        // The settings page carries the account card, and the subscription rows on it are read from
        // the service rather than stored. The desktop refreshes them when the account window opens;
        // the phone has no such window, so arriving on the tab is the equivalent moment. Not awaited:
        // it is one HTTP call whose only job is to replace a row of text.
        if (page == MobilePage.Settings && Account is { IsSignedIn: true } account)
        {
            _ = account.RefreshBillingCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Opens a note full screen. Everything that navigates to a note ends here.</summary>
    [RelayCommand]
    private async Task OpenNote(NoteTabViewModel? tab)
    {
        if (tab is null || !await Notes.SelectNoteAsync(tab).ConfigureAwait(true))
        {
            return;
        }

        Page = MobilePage.Day;
        IsEditorOpen = true;
    }

    /// <summary>The system back gesture and the editor's own back arrow, which must flush first.</summary>
    [RelayCommand]
    public async Task<bool> CloseEditorAsync()
    {
        if (!IsEditorOpen)
        {
            return false;
        }

        FlushResult flush = await Notes.FlushAsync(FlushReason.NoteChange).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            // The editor stays up showing the failure and its retry, which is what the user needs.
            return true;
        }

        IsRenamingTitle = false;
        IsEditorOpen = false;
        await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        return true;
    }

    // ── The selected day ─────────────────────────────────────────────────────────────────────────

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
    private string _tagInput = string.Empty;

    /// <summary>
    /// Which of the three lists the Lists tab is showing. Reuses <see cref="RightTab"/> so the phone
    /// and the desktop name the same three things the same way; the phone has no Files entry,
    /// because attachments are read on the note rather than browsed as a set.
    /// </summary>
    [ObservableProperty]
    private RightTab _activeList = RightTab.Todo;

    public bool IsTodoList => ActiveList == RightTab.Todo;

    public bool IsFavoritesList => ActiveList == RightTab.Favorites;

    public bool IsTagsList => ActiveList == RightTab.Tags;

    partial void OnActiveListChanged(RightTab value)
    {
        OnPropertyChanged(nameof(IsTodoList));
        OnPropertyChanged(nameof(IsFavoritesList));
        OnPropertyChanged(nameof(IsTagsList));
    }

    [RelayCommand]
    private void SelectList(RightTab tab) => ActiveList = tab;

    public bool HasOpenNote => Notes.SelectedTab is { IsProjection: false };

    partial void OnIsDarkChanged(bool value)
    {
        _themeApplier.Apply(value);
        if (!_loading)
        {
            _ = _settings.SetAsync(ThemeKey, value ? "dark" : "light");
        }
    }

    /// <summary>Loads the persisted theme, then today across every surface.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;
        try
        {
            string? theme = await _settings.GetAsync(ThemeKey, cancellationToken).ConfigureAwait(true);
            IsDark = string.Equals(theme, "dark", StringComparison.Ordinal);
            _themeApplier.Apply(IsDark);
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

    // ── Year and month picker ────────────────────────────────────────────────────────────────────
    // Tapping the month header opens it. Reaching March of last year by the arrows either side of
    // that header is eighteen taps; this is two.

    [ObservableProperty]
    private bool _isMonthPickerOpen;

    /// <summary>The year the picker is showing, which is not the calendar's until a month is chosen.</summary>
    [ObservableProperty]
    private int _pickerYear;

    /// <summary>The twelve months, named by the active culture.</summary>
    public ObservableCollection<MonthOption> PickerMonths { get; } = [];

    [RelayCommand]
    private void OpenMonthPicker()
    {
        PickerYear = Calendar.CursorYear;
        BuildPickerMonths();
        IsMonthPickerOpen = true;
    }

    [RelayCommand]
    private void CloseMonthPicker() => IsMonthPickerOpen = false;

    [RelayCommand]
    private void PickerPreviousYear()
    {
        PickerYear--;
        MarkCurrentMonth();
    }

    [RelayCommand]
    private void PickerNextYear()
    {
        PickerYear++;
        MarkCurrentMonth();
    }

    /// <summary>
    /// Moves the calendar to the chosen month. The selected day is left alone, exactly as the
    /// arrows either side of the header leave it: this is a way of looking, not of choosing.
    /// </summary>
    [RelayCommand]
    private async Task PickMonth(int month)
    {
        IsMonthPickerOpen = false;
        Calendar.CursorYear = PickerYear;
        Calendar.CursorMonth = month;
        await Calendar.LoadAsync().ConfigureAwait(true);
        Calendar.SyncSelection(SelectedDate);
    }

    private void BuildPickerMonths()
    {
        if (PickerMonths.Count == 0)
        {
            for (int month = 1; month <= 12; month++)
            {
                PickerMonths.Add(new MonthOption(month, MonthLabel(month)));
            }
        }

        MarkCurrentMonth();
    }

    private void MarkCurrentMonth()
    {
        foreach (MonthOption option in PickerMonths)
        {
            option.IsCurrent = PickerYear == Calendar.CursorYear && option.Number == Calendar.CursorMonth;
        }
    }

    private static string MonthLabel(int month) =>
        new DateOnly(2000, month, 1).ToString("MMM", LocalizationService.Instance.Culture);

    [RelayCommand]
    private Task GoToToday() => SelectDateAsync(LocalDates.Today(_clock));

    /// <summary>
    /// The first tab: show the day screen, on today.
    /// </summary>
    /// <remarks>
    /// One command rather than a tab plus a button, because a tab that only sometimes moves the
    /// date would be the worst of both. Tapping it from anywhere lands on today; tapping it while
    /// already there is still a way back from a month spent browsing.
    /// </remarks>
    [RelayCommand]
    private async Task GoToTodayPage()
    {
        GoToPage(MobilePage.Day);
        await SelectDateAsync(LocalDates.Today(_clock)).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task PreviousDay() => SelectDateAsync(LocalDates.AddDays(SelectedDate, -1));

    [RelayCommand]
    private Task NextDay() => SelectDateAsync(LocalDates.AddDays(SelectedDate, 1));

    [RelayCommand]
    private void ToggleTheme() => IsDark = !IsDark;

    /// <summary>Creates a note on the selected date and opens it, which is the phone's whole point.</summary>
    [RelayCommand]
    private async Task NewNote()
    {
        if (await Notes.AddNoteAsync().ConfigureAwait(true))
        {
            await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
            Page = MobilePage.Day;
            IsEditorOpen = true;
        }
    }

    [RelayCommand]
    private async Task DeleteNote(NoteTabViewModel? tab)
    {
        if (tab is not null && await Notes.DeleteNoteAsync(tab).ConfigureAwait(true))
        {
            IsEditorOpen = false;
            await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        }
    }

    // ── Renaming the open note ───────────────────────────────────────────────────────────────────
    // The title is a label until it is tapped, then a text box holding a draft. The draft is kept
    // apart from the tab's own title so backing out really does cancel, and so a half-typed name is
    // never what the day list shows.

    [ObservableProperty]
    private bool _isRenamingTitle;

    [ObservableProperty]
    private string _titleDraft = string.Empty;

    /// <summary>Raised when the title box has appeared and wants the caret and the keyboard.</summary>
    public event EventHandler? TitleRenameStarted;

    [RelayCommand]
    private void BeginRenameTitle()
    {
        if (Notes.SelectedTab is { IsProjection: false } tab)
        {
            TitleDraft = tab.Title;
            IsRenamingTitle = true;
            TitleRenameStarted?.Invoke(this, EventArgs.Empty);
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

        // An empty box is a cancel, not a request for a nameless note: the note keeps the title it
        // had, which for an untitled one is the date-and-number the workspace gives it.
        if (Notes.SelectedTab is { } tab && draft.Length > 0
            && !string.Equals(draft, tab.Title, StringComparison.Ordinal)
            && await Notes.RenameAsync(tab, draft).ConfigureAwait(true))
        {
            RefreshHeader();
            await Todo.RefreshAsync().ConfigureAwait(true);
            await Favorites.RefreshAsync().ConfigureAwait(true);
            await TagPanel.RefreshAsync().ConfigureAwait(true);
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
            && await Notes.AddTagAsync(tab, tag, CancellationToken.None).ConfigureAwait(true))
        {
            await TagPanel.RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RemoveTag(string? tag)
    {
        if (Notes.SelectedTab is { } tab && !string.IsNullOrWhiteSpace(tag)
            && await Notes.RemoveTagAsync(tab, tag, CancellationToken.None).ConfigureAwait(true))
        {
            await TagPanel.RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Flushes the open note. The head calls this when the OS suspends the app.</summary>
    public Task<FlushResult> FlushAsync(FlushReason reason) => Notes.FlushAsync(reason);

    private async Task RefreshAfterStructureChangeAsync()
    {
        RefreshHeader();
        await Calendar.LoadAsync().ConfigureAwait(true);
        await Todo.RefreshAsync().ConfigureAwait(true);
        await Favorites.RefreshAsync().ConfigureAwait(true);
        await TagPanel.RefreshAsync().ConfigureAwait(true);
    }

    private void OnNotesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoteWorkspaceViewModel.SelectedTab) or nameof(NoteWorkspaceViewModel.ProjectionOnly))
        {
            RefreshHeader();
        }
    }

    private void RefreshHeader()
    {
        DayLabel = LocalDates.DisplayDayHeading(SelectedDate);
        int count = Notes.ProjectionOnly ? 0 : Notes.Tabs.Count(t => !t.IsProjection);
        NoteCountText = AppStrings.NoteCount(count);
        IsDayEmpty = count == 0;
        OnPropertyChanged(nameof(HasOpenNote));
    }

    void ILanguageAware.OnLanguageChanged()
    {
        RefreshHeader();
        RefreshAccountBar();

        // The month names came from the culture, so they are wrong the moment it changes.
        PickerMonths.Clear();
        if (IsMonthPickerOpen)
        {
            BuildPickerMonths();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _syncScheduler?.Dispose();
        Notes.PropertyChanged -= OnNotesPropertyChanged;
        await Notes.DisposeAsync().ConfigureAwait(false);
    }
}
