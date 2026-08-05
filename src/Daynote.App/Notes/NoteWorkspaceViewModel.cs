using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;

namespace Daynote.App.Notes;

public enum SaveStatusKind
{
    None,
    Dirty,
    Saving,
    Saved,
    Error,
}

/// <summary>Use cases and collaborators the note workspace composes (Todo 4 contracts).</summary>
public sealed class NoteWorkspaceDependencies
{
    public NoteWorkspaceDependencies(
        INoteRepository repository,
        GetDayWorkspace getDayWorkspace,
        CreateNote createNote,
        ReorderNotes reorderNotes,
        DeleteNote deleteNote,
        Func<NoteId> nextId,
        IAutosaveScheduler? scheduler = null,
        TimeSpan? debounce = null,
        ToggleNoteFavorite? toggleFavorite = null,
        SetNoteTags? setTags = null)
    {
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        GetDayWorkspace = getDayWorkspace ?? throw new ArgumentNullException(nameof(getDayWorkspace));
        CreateNote = createNote ?? throw new ArgumentNullException(nameof(createNote));
        ReorderNotes = reorderNotes ?? throw new ArgumentNullException(nameof(reorderNotes));
        DeleteNote = deleteNote ?? throw new ArgumentNullException(nameof(deleteNote));
        NextId = nextId ?? throw new ArgumentNullException(nameof(nextId));
        Scheduler = scheduler;
        Debounce = debounce;
        ToggleFavorite = toggleFavorite;
        SetTags = setTags;
    }

    public INoteRepository Repository { get; }

    public GetDayWorkspace GetDayWorkspace { get; }

    public CreateNote CreateNote { get; }

    public ReorderNotes ReorderNotes { get; }

    public DeleteNote DeleteNote { get; }

    public Func<NoteId> NextId { get; }

    public IAutosaveScheduler? Scheduler { get; }

    public TimeSpan? Debounce { get; }

    /// <summary>Favorite toggle use case (redesign); null in fixtures that do not exercise favorites.</summary>
    public ToggleNoteFavorite? ToggleFavorite { get; }

    /// <summary>Tag replace-set use case (redesign); null in fixtures that do not exercise tags.</summary>
    public SetNoteTags? SetTags { get; }
}

/// <summary>
/// The dominant note region: date header, ordered note tabs, plain Markdown editor, and save status.
/// Owns the selected date's note set, its autosave coordinator, and the guarded transitions that
/// never discard dirty text (DESIGN Sections 1, 4, 5).
/// </summary>
public sealed partial class NoteWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly NoteWorkspaceDependencies _dependencies;
    private readonly AutosaveCoordinator _autosave;
    private readonly System.Threading.SynchronizationContext? _sync;
    private bool _suppressEditorSync;
    private bool _projectionOnly = true;
    private bool _disposed;

    public NoteWorkspaceViewModel(NoteWorkspaceDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _autosave = new AutosaveCoordinator(dependencies.Repository, dependencies.Scheduler, dependencies.Debounce);
        _autosave.RecoverableError += OnRecoverableError;
        _sync = System.Threading.SynchronizationContext.Current;
        SelectedDate = default;
        DateDisplay = string.Empty;
    }

    public ObservableCollection<NoteTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private NoteTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _editorText = string.Empty;

    [ObservableProperty]
    private LocalDate _selectedDate;

    [ObservableProperty]
    private string _dateDisplay;

    [ObservableProperty]
    private SaveStatusKind _saveStatus;

    [ObservableProperty]
    private string? _saveErrorMessage;

    [ObservableProperty]
    private bool _isGuarded;

    public bool ProjectionOnly => _projectionOnly;

    /// <summary>True when the editor buffer is empty; drives the quiet empty-state hint (DESIGN Section 5).</summary>
    public bool IsEditorEmpty => string.IsNullOrEmpty(EditorText);

    public bool HasSaveError => SaveStatus == SaveStatusKind.Error;

    /// <summary>True while a save-state cue (dirty/saving/saved/error) should be shown; None is silent.</summary>
    public bool HasSaveStatus => SaveStatus != SaveStatusKind.None;

    /// <summary>Korean save-state label mirroring the DESIGN semantic-icon mapping.</summary>
    public string SaveStatusDisplay => SaveStatus switch
    {
        SaveStatusKind.Dirty => Localization.AppStrings.SaveDirty,
        SaveStatusKind.Saving => Localization.AppStrings.SaveSaving,
        SaveStatusKind.Saved => Localization.AppStrings.SaveSaved,
        SaveStatusKind.Error => Localization.AppStrings.SaveFailed,
        _ => string.Empty,
    };

    partial void OnSaveStatusChanged(SaveStatusKind value)
    {
        OnPropertyChanged(nameof(HasSaveError));
        OnPropertyChanged(nameof(HasSaveStatus));
        OnPropertyChanged(nameof(SaveStatusDisplay));
    }

    internal AutosaveCoordinator Autosave => _autosave;

    /// <summary>Loads a date's note set, replacing the tab strip and editor buffer.</summary>
    public async Task LoadAsync(LocalDate date, CancellationToken cancellationToken = default)
    {
        SelectedDate = date;
        DateDisplay = Composition.LocalDates.DisplayLong(date);
        DayWorkspace workspace = await _dependencies.GetDayWorkspace
            .ExecuteAsync(date, cancellationToken).ConfigureAwait(true);
        RebuildTabs(workspace, selectId: null);
        ClearSaveError();
    }

    private void RebuildTabs(DayWorkspace workspace, NoteId? selectId)
    {
        _projectionOnly = workspace.Notes.IsProjectionOnly;
        NoteId? previous = selectId ?? SelectedTab?.Id;
        Tabs.Clear();
        foreach (Note note in workspace.Notes.Notes)
        {
            NoteTabViewModel tab;
            if (note.IsProjection)
            {
                tab = new NoteTabViewModel(
                    _dependencies.NextId(), note.LocalDate, note.SortOrder,
                    note.Title, note.Body, hasCustomTitle: false, isProjection: true, revision: 0);
            }
            else
            {
                NoteId id = note.Id!.Value;
                tab = NoteTabViewModel.FromNote(note, workspace.RevisionOf(id), default);
            }

            Tabs.Add(tab);
        }

        NoteTabViewModel? target = Tabs.FirstOrDefault(t => previous is { } id && t.Id == id) ?? Tabs.FirstOrDefault();
        SetSelectedTab(target);
    }

    private void SetSelectedTab(NoteTabViewModel? tab)
    {
        foreach (NoteTabViewModel candidate in Tabs)
        {
            candidate.IsSelected = ReferenceEquals(candidate, tab);
        }

        SelectedTab = tab;
        _suppressEditorSync = true;
        EditorText = tab?.Body ?? string.Empty;
        _suppressEditorSync = false;
    }

    /// <summary>Switches the selected note after an autosave-safe flush; cancels on save failure.</summary>
    public async Task<bool> SelectNoteAsync(NoteTabViewModel? tab, CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(tab, SelectedTab))
        {
            return true;
        }

        FlushResult flush = await FlushAsync(FlushReason.NoteChange, cancellationToken).ConfigureAwait(true);
        if (!flush.CanProceed)
        {
            return false;
        }

        SetSelectedTab(tab);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _autosave.RecoverableError -= OnRecoverableError;
        await _autosave.DisposeAsync().ConfigureAwait(false);
    }

    private void Post(Action action)
    {
        if (_sync is null)
        {
            action();
        }
        else
        {
            _sync.Post(_ => action(), null);
        }
    }
}
