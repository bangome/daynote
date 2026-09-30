using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Input;
using Daynote.App.Localization;
using Daynote.App.Notes;
using Daynote.Core.Notes;

namespace Daynote.App.Shell.Product;

/// <summary>
/// What the design-B layout reads that the panels do not already say: which view fills the middle
/// (the editor, the timeline, or one of the four lists the sidebar opens), the big date in the header,
/// the selected day's to-dos for the right panel, the timeline folded into days, and the shortcut
/// hints the tooltips and the palette print.
/// </summary>
/// <remarks>
/// The Avalonia shell says the same things in <c>DesktopShellViewModel.Views.cs</c>. The two shells
/// have always been deliberate mirrors of one another — both wire the same panel view models from
/// <c>Daynote.Presentation</c> — and this file keeps that arrangement rather than introducing a third
/// place for it. <c>DeskSurfaceParityTests</c> holds the two surfaces to the same member set, so one
/// gaining a property the other lacks fails rather than showing up as a hole in one window.
/// <para>
/// The lists used to be tabs in a right rail. They are the same panel view models; only where they are
/// shown moved, so <see cref="ActiveTab"/> still says which one and <see cref="IsListMode"/> says
/// whether it is on screen.
/// </para>
/// </remarks>
public sealed partial class ProductShellViewModel
{
    /// <summary>The list view (to-dos, favourites, tags or files) is filling the middle.</summary>
    [ObservableProperty]
    private bool _isListMode;

    /// <summary>The Ctrl K palette is open over the window.</summary>
    [ObservableProperty]
    private bool _isPaletteOpen;

    [ObservableProperty]
    private string _bigDay = string.Empty;

    [ObservableProperty]
    private string _bigMeta = string.Empty;

    [ObservableProperty]
    private string _bigSub = string.Empty;

    [ObservableProperty]
    private bool _isTodaySelected;

    [ObservableProperty]
    private string _dayTodoCountText = "0 / 0";

    [ObservableProperty]
    private bool _isDayTodoEmpty = true;

    /// <summary>The week of the selected date, under the header.</summary>
    public WeekStripViewModel Week { get; private set; } = null!;

    /// <summary>The selected day's to-dos, for the right panel: the full list filtered to one date.</summary>
    public ObservableCollection<TodoItemViewModel> DayTodos { get; } = [];

    /// <summary>The timeline's rows folded into days, for the date-column layout.</summary>
    public ObservableCollection<TimelineDayGroup> TimelineGroups { get; } = [];

    /// <summary>The configurable shortcuts, for the hints. Set by composition; null in bare fixtures.</summary>
    public ConfigurableShortcuts? Shortcuts
    {
        get => _shortcutSource;
        set
        {
            if (_shortcutSource is not null)
            {
                _shortcutSource.Changed -= OnShortcutsChanged;
            }

            _shortcutSource = value;
            if (_shortcutSource is not null)
            {
                _shortcutSource.Changed += OnShortcutsChanged;
            }

            RefreshShortcutHints();
        }
    }

    private ConfigurableShortcuts? _shortcutSource;

    public bool IsNavEditor => IsEditorMode;

    public bool IsNavTimeline => IsTimelineMode;

    public bool IsNavTodo => IsListMode && ActiveTab == RightTab.Todo;

    public bool IsNavFavorites => IsListMode && ActiveTab == RightTab.Favorites;

    public bool IsNavTags => IsListMode && ActiveTab == RightTab.Tags;

    public bool IsNavFiles => IsListMode && ActiveTab == RightTab.Files;

    /// <summary>The list view's heading: the list's name.</summary>
    public string ListTitle => ActiveTab switch
    {
        RightTab.Favorites => AppStrings.DeskNavFavorites,
        RightTab.Tags => AppStrings.DeskNavTags,
        RightTab.Files => AppStrings.DeskNavFiles,
        _ => AppStrings.DeskNavTodo,
    };

    /// <summary>The line beside the heading: how many are open, how many there are, or which day.</summary>
    public string ListSubtitle => ActiveTab switch
    {
        RightTab.Favorites => Format(AppStrings.DeskCountFormat, Favorites.Items.Count),
        RightTab.Tags => Format(AppStrings.DeskCountFormat, TagPanel.Tags.Count),
        RightTab.Files => LocalDates.DisplayDayHeading(SelectedDate),
        _ => Format(AppStrings.DeskTodoOpenFormat, Todo.OpenCount),
    };

    /// <summary>The drop zone's sentence in the files list, naming the day the files will land on.</summary>
    public string FilesDropText => Format(AppStrings.DeskFilesDropFormat, LocalDates.DisplayDayHeading(SelectedDate));

    /// <summary>"12자 · 3줄" under the editor, counted the way the design counts (newline-split lines).</summary>
    public string CharLineText
    {
        get
        {
            string text = Notes.EditorText ?? string.Empty;
            return string.Format(
                CultureInfo.CurrentCulture, AppStrings.DeskCharLineFormat, text.Length, text.Split('\n').Length);
        }
    }

    /// <summary>
    /// The footer's save cue. The design always says "저장됨"; a note that has not been touched since
    /// it was opened is saved too, so the quiet state reads the same rather than showing nothing.
    /// </summary>
    public string SaveFooterText => Notes.HasSaveStatus ? Notes.SaveStatusDisplay : AppStrings.SaveSaved;

    /// <summary>Green dot: nothing waiting. Otherwise the dot greys while a save is due or running.</summary>
    public bool IsSaveSettled => Notes.SaveStatus is SaveStatusKind.None or SaveStatusKind.Saved;

    public bool IsSaveFailed => Notes.HasSaveError;

    /// <summary>The note's date in full, where the design shows when it was last edited.</summary>
    public string NoteDateText => LocalDates.DisplayLong(SelectedDate);

    public string NewNoteHint => Hint(AppShortcuts.NewNote);

    public string TodayHint => Hint(AppShortcuts.GoToday);

    public string SettingsHint => Hint(AppShortcuts.Settings);

    public string ThemeHint => Hint(AppShortcuts.ToggleTheme);

    public string LeftPanelHint => Hint(AppShortcuts.ToggleLeft);

    public string RightPanelHint => Hint(AppShortcuts.ToggleRight);

    public string StickyHint => Hint(AppShortcuts.OpenSticky);

    /// <summary>The palette's key, as the sidebar's search box prints it.</summary>
    public static string PaletteHint => OperatingSystem.IsMacOS() ? "⌘K" : "Ctrl K";

    public string NewNoteTip => Format(AppStrings.DeskNewNoteTipFormat, NewNoteHint);

    public string TodayTip => Format(AppStrings.DeskGoTodayTipFormat, TodayHint);

    public string ThemeTip => Format(AppStrings.DeskThemeTipFormat, ThemeHint);

    public string DayPanelTip => Format(AppStrings.DeskDayPanelTipFormat, RightPanelHint);

    public string SidebarCollapseTip => Format(AppStrings.DeskSidebarCollapseTipFormat, LeftPanelHint);

    public string StickyTip => Format(AppStrings.DeskPostItTipFormat, StickyHint);

    public string SettingsTip => string.Create(CultureInfo.CurrentCulture, $"{AppStrings.Settings} ({SettingsHint})");

    public string EmptyDayButtonText => Format(AppStrings.DeskEmptyDayButtonFormat, NewNoteHint);

    public string ThemeSwitchText => Format(AppStrings.DeskThemeSwitchFormat, ThemeHint);

    public string ThemeName => IsDark ? AppStrings.DeskThemeDark : AppStrings.DeskThemeLight;

    /// <summary>The account row's second line: the sync state and when it last ran, or where notes live.</summary>
    public string AccountRowSubtitle => Account switch
    {
        { IsSignedIn: true, Status.IsVisible: true } account when account.LastSyncText is { Length: > 0 } last =>
            string.Create(CultureInfo.CurrentCulture, $"{account.Status.Label} · {last}"),
        { IsSignedIn: true, Status.IsVisible: true } account => account.Status.Label,
        { IsSignedIn: true } => string.Empty,
        not null => AppStrings.AccountBarLocal,
        null => string.Empty,
    };

    /// <summary>The letter on the avatar disc: the account's, or the product's while there is none.</summary>
    public string AccountInitial => Account is { IsSignedIn: true } account ? account.AvatarInitial : "D";

    /// <summary>The settings card's headline: "who · plan", the way to sign in, or where the notes live.</summary>
    public string AccountCardTitle => Account switch
    {
        { IsSignedIn: true } account => string.Create(CultureInfo.CurrentCulture, $"{account.DisplayName} · {account.PlanBadge}"),
        not null => AppStrings.AccountBarSignIn,
        null => AppStrings.AccountBarLocal,
    };

    public string AccountCardSubtitle => Account switch
    {
        { IsSignedIn: true } => AccountRowSubtitle,
        not null => AppStrings.AccountSignInLead,
        null => string.Empty,
    };

    /// <summary>The card's button: manage the account, or sign in to one.</summary>
    public string AccountCardAction =>
        Account is { IsSignedIn: true } ? AppStrings.DeskAccountManageShort : AppStrings.AccountBarSignIn;

    /// <summary>The palette's quick actions, shown while nothing has been typed.</summary>
    public IReadOnlyList<PaletteActionViewModel> QuickActions => _quickActions ??=
    [
        new(() => AppStrings.DeskPaletteNewNote, () => NewNoteHint, () => FromPalette(NewNoteCommand.ExecuteAsync)),
        new(() => AppStrings.GoToToday, () => TodayHint, () => FromPalette(GoToTodayCommand.ExecuteAsync)),
        new(() => AppStrings.TimelineToggle, () => string.Empty, () => FromPalette(ShowTimelineCommand.ExecuteAsync)),
        new(() => AppStrings.OpenSettings, () => SettingsHint, () =>
        {
            IsPaletteOpen = false;
            OpenSettings();
            return Task.CompletedTask;
        }),
    ];

    private IReadOnlyList<PaletteActionViewModel>? _quickActions;

    /// <summary>Wires the new surfaces to the panels they read. Called once from the constructor.</summary>
    private void AttachViews(Core.Time.IClock clock, Core.Notes.INoteRepository repository)
    {
        // The thread this shell was built on owns the collections the window binds. A language
        // switch or a sync callback can arrive on another one, and WPF refuses a change to a bound
        // collection from off it, so the two rebuilds below come back here first.
        _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Week = new WeekStripViewModel(clock, repository, date => SelectDateAsync(date));
        Todo.Refreshed += (_, _) => RefreshDayTodos();
        Timeline.Rows.CollectionChanged += OnTimelineRowsChanged;
        Favorites.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ListSubtitle));
        TagPanel.Tags.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ListSubtitle));
        Todo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TodoPanelViewModel.OpenCount))
            {
                OnPropertyChanged(nameof(ListSubtitle));
            }
        };
    }

    partial void OnIsListModeChanged(bool value) => RaiseViewState();

    private void RaiseSaveFooter()
    {
        OnPropertyChanged(nameof(SaveFooterText));
        OnPropertyChanged(nameof(IsSaveSettled));
        OnPropertyChanged(nameof(IsSaveFailed));
    }

    /// <summary>Everything that says which view is up, after any of its inputs changed.</summary>
    private void RaiseViewState()
    {
        OnPropertyChanged(nameof(IsEditorMode));
        OnPropertyChanged(nameof(IsNavEditor));
        OnPropertyChanged(nameof(IsNavTimeline));
        OnPropertyChanged(nameof(IsNavTodo));
        OnPropertyChanged(nameof(IsNavFavorites));
        OnPropertyChanged(nameof(IsNavTags));
        OnPropertyChanged(nameof(IsNavFiles));
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(ListSubtitle));
    }

    /// <summary>"오늘의 노트": back to the editor on whatever day is selected.</summary>
    [RelayCommand]
    private void ShowEditor()
    {
        IsTimelineMode = false;
        IsListMode = false;
    }

    /// <summary>Opens the timeline (the sidebar's and the palette's way in; the header button toggles).</summary>
    [RelayCommand]
    private async Task ShowTimeline()
    {
        IsPaletteOpen = false;
        if (!IsTimelineMode)
        {
            await ToggleTimelineCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    /// <summary>Opens one of the four lists in the middle of the window.</summary>
    [RelayCommand]
    private async Task ShowList(RightTab tab)
    {
        // Leaving the editor is a navigation like any other: what was typed is saved first, and a
        // failed save keeps the editor (with its retry) on screen.
        if (IsEditorMode)
        {
            FlushResult flush = await Notes.FlushAsync(FlushReason.DateChange).ConfigureAwait(true);
            if (!flush.CanProceed)
            {
                return;
            }
        }

        ActiveTab = tab;
        IsTimelineMode = false;
        IsListMode = true;
    }

    [RelayCommand]
    private void OpenPalette()
    {
        IsAccountOpen = false;
        IsPaletteOpen = true;
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    /// <summary>A quick action or a result was picked, or the palette was dismissed: the query goes too.</summary>
    partial void OnIsPaletteOpenChanged(bool value)
    {
        if (!value)
        {
            Search.Query = string.Empty;
        }
    }

    /// <summary>A day's heading in the timeline opens that day in the editor.</summary>
    [RelayCommand]
    private async Task OpenTimelineDay(TimelineDayGroup? group)
    {
        if (group is null)
        {
            return;
        }

        IsListMode = false;
        await SelectDateAsync(group.Date).ConfigureAwait(true);
    }

    /// <summary>Re-derives the header's big date and the per-day lists for <see cref="SelectedDate"/>.</summary>
    private void RefreshDayViews()
    {
        CultureInfo culture = LocalizationService.Instance.Culture;
        DateOnly day = LocalDates.ToDateOnly(SelectedDate);
        BigDay = day.Day.ToString(culture);
        BigMeta = day.ToString(AppStrings.DeskHeaderMonthWeekdayFormat, culture);
        int count = Notes.ProjectionOnly ? 0 : Notes.Tabs.Count(t => !t.IsProjection);
        BigSub = string.Create(culture, $"{day.ToString(AppStrings.DeskHeaderYearFormat, culture)} · {AppStrings.NoteCount(count)}");
        IsTodaySelected = SelectedDate == LocalDates.Today(_clock);
        OnPropertyChanged(nameof(NoteDateText));
        OnPropertyChanged(nameof(FilesDropText));
        OnPropertyChanged(nameof(ListSubtitle));
        RefreshDayTodos();
    }

    private System.Windows.Threading.Dispatcher? _dispatcher;

    private void RefreshDayTodos()
    {
        if (_dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshDayTodos);
            return;
        }

        DayTodos.Clear();
        int open = 0;
        foreach (TodoItemViewModel item in Todo.TodayItems.Concat(Todo.Items)
            .Where(item => item.Date == SelectedDate)
            .OrderBy(item => item.Checked))
        {
            DayTodos.Add(item);
            if (!item.Checked)
            {
                open++;
            }
        }

        DayTodoCountText = string.Create(CultureInfo.InvariantCulture, $"{open} / {DayTodos.Count}");
        IsDayTodoEmpty = DayTodos.Count == 0;
    }

    private void OnTimelineRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A page arrives as a run of single adds; regrouping per row would rebuild the groups twenty
        // times for one scroll. Once the run has settled is enough.
        if (_timelineRegroupPending)
        {
            return;
        }

        _timelineRegroupPending = true;
        _ = (_dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher).BeginInvoke(() =>
        {
            _timelineRegroupPending = false;
            TimelineGroups.Clear();

            // Over a snapshot: the fold is posted, so a page still arriving can add to Rows while
            // it walks them, and the walk throws. Caught by the test that folds a loaded timeline.
            foreach (TimelineDayGroup group in TimelineGrouping.Group([.. Timeline.Rows]))
            {
                TimelineGroups.Add(group);
            }
        });
    }

    private bool _timelineRegroupPending;

    private void OnShortcutsChanged(object? sender, EventArgs e) => RefreshShortcutHints();

    private void RefreshShortcutHints()
    {
        foreach (string name in new[]
        {
            nameof(NewNoteHint), nameof(TodayHint), nameof(SettingsHint), nameof(ThemeHint), nameof(LeftPanelHint),
            nameof(RightPanelHint), nameof(StickyHint), nameof(NewNoteTip), nameof(TodayTip), nameof(ThemeTip),
            nameof(DayPanelTip), nameof(SidebarCollapseTip), nameof(StickyTip), nameof(SettingsTip),
            nameof(EmptyDayButtonText), nameof(ThemeSwitchText),
        })
        {
            OnPropertyChanged(name);
        }

        if (_quickActions is not null)
        {
            foreach (PaletteActionViewModel action in _quickActions)
            {
                action.Refresh();
            }
        }
    }

    private string Hint(string actionId) =>
        _shortcutSource?.Get(actionId).ToDisplayString() ?? AppShortcuts.DefaultFor(actionId).ToDisplayString();

    /// <summary>Closes the palette first, so what the action opens is what is left on screen.</summary>
    private Task FromPalette(Func<object?, Task> run)
    {
        IsPaletteOpen = false;
        return run(null);
    }

    private static string Format(string format, object value) =>
        string.Format(CultureInfo.CurrentCulture, format, value);
}

/// <summary>One quick action in the palette: a label, the chord that does the same, and what it runs.</summary>
public sealed partial class PaletteActionViewModel : ObservableObject
{
    private readonly Func<string> _label;
    private readonly Func<string> _combo;
    private readonly Func<Task> _run;

    public PaletteActionViewModel(Func<string> label, Func<string> combo, Func<Task> run)
    {
        _label = label ?? throw new ArgumentNullException(nameof(label));
        _combo = combo ?? throw new ArgumentNullException(nameof(combo));
        _run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public string Label => _label();

    public string Combo => _combo();

    public bool HasCombo => Combo.Length > 0;

    [RelayCommand]
    private Task Run() => _run();

    /// <summary>After a language switch or a reassigned chord.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Combo));
        OnPropertyChanged(nameof(HasCombo));
    }
}
