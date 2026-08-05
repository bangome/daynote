using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daynote.App.Composition;
using Daynote.App.Notes;
using Daynote.App.Search;
using Daynote.App.Settings;
using Daynote.App.Sidebar;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Time;

namespace Daynote.App.Shell;

/// <summary>
/// Owns the shell: the single selected-date navigation source, the layout state, and the sidebar and
/// note regions. Date changes are autosave-guarded; a save failure cancels the transition and retains
/// dirty text (DESIGN Sections 1, 4, 5).
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDateNavigator, IAsyncDisposable
{
    private readonly IClock _clock;
    private readonly AppShellLayoutState _layout;
    private bool _disposed;

    public MainWindowViewModel(
        NoteWorkspaceViewModel notes,
        IClock clock,
        LayoutThresholds thresholds,
        SearchService searchService,
        ISearchScheduler? searchScheduler = null,
        TimeSpan? searchDebounce = null)
    {
        Notes = notes ?? throw new ArgumentNullException(nameof(notes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(searchService);
        _layout = new AppShellLayoutState(thresholds);
        Sidebar = new SidebarViewModel(this, notes, clock);
        Navigator = new WorkspaceNavigator(this, notes);
        Search = new SearchViewModel(searchService, Navigator, searchScheduler, searchDebounce);
        _selectedDate = LocalDates.Today(clock);
        Notes.PropertyChanged += OnNotesPropertyChanged;
    }

    /// <summary>
    /// Quiet bottom status line: selected date and save state, composed from existing strings and
    /// payload-free (DESIGN Section 5 Status feedback). Updates with its source bindings.
    /// </summary>
    public string StatusLine
    {
        get
        {
            var parts = new List<string>
            {
                LocalDates.DisplayShort(SelectedDate),
            };
            if (Notes.HasSaveStatus)
            {
                parts.Add(Notes.SaveStatusDisplay);
            }

            return string.Join(" · ", parts);
        }
    }

    private void OnNotesPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoteWorkspaceViewModel.SaveStatusDisplay)
            or nameof(NoteWorkspaceViewModel.HasSaveStatus)
            or nameof(NoteWorkspaceViewModel.DateDisplay))
        {
            OnPropertyChanged(nameof(StatusLine));
        }
    }

    partial void OnSelectedDateChanged(LocalDate value) => OnPropertyChanged(nameof(StatusLine));

    /// <summary>The settings surface view model; assigned during composition.</summary>
    [ObservableProperty]
    private SettingsViewModel? _settings;

    /// <summary>True while the settings overlay is open.</summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    public void OpenSettings() => IsSettingsOpen = true;

    public void CloseSettings() => IsSettingsOpen = false;

    public NoteWorkspaceViewModel Notes { get; }

    public SidebarViewModel Sidebar { get; }

    public SearchViewModel Search { get; }

    public WorkspaceNavigator Navigator { get; }

    [ObservableProperty]
    private LocalDate _selectedDate;

    [ObservableProperty]
    private AppLayoutState _layoutState = AppLayoutState.Regular;

    [ObservableProperty]
    private CompactWorkspaceView _selectedCompactView = CompactWorkspaceView.Notes;

    /// <summary>Loads today's workspace on startup.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        LocalDate today = LocalDates.Today(_clock);
        SelectedDate = today;
        await Notes.LoadAsync(today, cancellationToken).ConfigureAwait(true);
        Sidebar.Calendar.ShowSelected(today);
    }

    /// <inheritdoc />
    public async Task<bool> SelectDateAsync(LocalDate date, CancellationToken cancellationToken = default)
    {
        if (date == SelectedDate)
        {
            Sidebar.Calendar.SyncSelection(date);
            return true;
        }

        FlushResult flush = await Notes.FlushAsync(FlushReason.DateChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        SelectedDate = date;
        await Notes.LoadAsync(date, cancellationToken).ConfigureAwait(true);
        Sidebar.Calendar.ShowSelected(date);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Notes.PropertyChanged -= OnNotesPropertyChanged;
        Search.Dispose();
        await Notes.DisposeAsync().ConfigureAwait(false);
    }
}
