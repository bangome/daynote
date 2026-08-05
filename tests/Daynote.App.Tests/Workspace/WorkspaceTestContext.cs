using Daynote.App.Notes;
using Daynote.App.Search;
using Daynote.App.Shell;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Infrastructure.Assets;
using Daynote.Infrastructure.Files;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Daynote.Infrastructure.Settings;

namespace Daynote.App.Tests.Workspace;

/// <summary>
/// Integration harness: real SQLite database and repositories behind the product view models, with a
/// deterministic clock and an infinite autosave scheduler so persistence is driven by explicit flush.
/// </summary>
internal sealed class WorkspaceTestContext : IAsyncDisposable
{
    private readonly string _root;
    private readonly SqliteDatabase _database;

    private WorkspaceTestContext(
        string root,
        SqliteDatabase database,
        FailingNoteRepository noteRepository)
    {
        _root = root;
        _database = database;
        NoteRepository = noteRepository;
        Clock = new MutableClock(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), TimeSpan.Zero);
        SearchService = new SearchService(new SqliteSearchRepository(database));

        var dependencies = new NoteWorkspaceDependencies(
            noteRepository,
            new GetDayWorkspace(noteRepository),
            new CreateNote(noteRepository, NextId),
            new ReorderNotes(noteRepository),
            new DeleteNote(noteRepository),
            NextId,
            new InfiniteScheduler());
        Notes = new NoteWorkspaceViewModel(dependencies);
        Main = new MainWindowViewModel(
            Notes,
            Clock,
            new LayoutThresholds(819, 820, 1199, 1200, 8),
            SearchService,
            new ImmediateSearchScheduler(),
            TimeSpan.Zero);
    }

    public SqliteDatabase Database => _database;

    public MutableClock Clock { get; }

    public FailingNoteRepository NoteRepository { get; }

    public SearchService SearchService { get; }

    public NoteWorkspaceViewModel Notes { get; }

    public MainWindowViewModel Main { get; }

    public static WorkspaceTestContext Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-task8-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = new SqliteDatabase(new SqliteDatabaseOptions(Path.Combine(root, "daynote.db")));
        database.Initialize();
        var noteRepository = new FailingNoteRepository(new SqliteNoteRepository(database));
        return new WorkspaceTestContext(root, database, noteRepository);
    }

    public static LocalDate Date(string iso) => LocalDate.Parse(iso).Value;

    /// <summary>Persists a note (with a searchable body) directly through the repository seam.</summary>
    public async Task<NoteId> StoreNoteAsync(LocalDate date, string title, string body)
    {
        NoteId id = NextId();
        await NoteRepository.SaveNoteAsync(
            new NoteSaveRequest(id, date, title, body, Revision: 0, IsNew: true, HasCustomTitle: true));
        return id;
    }

    /// <summary>Persists an ordered set of notes on one date (first materializes, the rest append).</summary>
    public async Task<IReadOnlyList<NoteId>> StoreNotesOnDateAsync(LocalDate date, params (string Title, string Body)[] notes)
    {
        var ids = new List<NoteId>();
        for (int index = 0; index < notes.Length; index++)
        {
            NoteId id = NextId();
            if (index == 0)
            {
                await NoteRepository.SaveNoteAsync(
                    new NoteSaveRequest(id, date, notes[index].Title, notes[index].Body, 0, IsNew: true, HasCustomTitle: true));
            }
            else
            {
                await NoteRepository.CreateNoteAsync(date, NextId(), id);
                await NoteRepository.SaveNoteAsync(
                    new NoteSaveRequest(id, date, notes[index].Title, notes[index].Body, 0, IsNew: false, HasCustomTitle: true));
            }

            ids.Add(id);
        }

        return ids;
    }

    /// <summary>Builds a fresh shell over the same database, modeling an app restart.</summary>
    public FreshShell NewShell()
    {
        var dependencies = new NoteWorkspaceDependencies(
            NoteRepository,
            new GetDayWorkspace(NoteRepository),
            new CreateNote(NoteRepository, NextId),
            new ReorderNotes(NoteRepository),
            new DeleteNote(NoteRepository),
            NextId,
            new InfiniteScheduler());
        var notes = new NoteWorkspaceViewModel(dependencies);
        var main = new MainWindowViewModel(
            notes, Clock, new LayoutThresholds(819, 820, 1199, 1200, 8),
            SearchService, new ImmediateSearchScheduler(), TimeSpan.Zero);
        return new FreshShell(main, notes);
    }

    /// <summary>Builds a product shell over the same database with test doubles for theme and picker.</summary>
    public ProductShellHarness BuildProductShell()
    {
        var dependencies = new NoteWorkspaceDependencies(
            NoteRepository,
            new GetDayWorkspace(NoteRepository),
            new CreateNote(NoteRepository, NextId),
            new ReorderNotes(NoteRepository),
            new DeleteNote(NoteRepository),
            NextId,
            new InfiniteScheduler(),
            toggleFavorite: new ToggleNoteFavorite(NoteRepository),
            setTags: new SetNoteTags(NoteRepository));
        var notes = new NoteWorkspaceViewModel(dependencies);
        var fileStore = new ContentAddressedFileStore(_root);
        var fileRepository = new SqliteDayFileRepository(_database);
        var picker = new FakeFilePicker();
        var theme = new NoOpThemeApplier();
        var settings = new SqliteSettingsStore(_database, Clock);
        var shell = new ProductShellViewModel(
            notes,
            Clock,
            SearchService,
            NoteRepository,
            new AddDayFile(fileRepository, fileStore),
            new ListDayFiles(fileRepository, fileStore),
            new DeleteDayFile(fileRepository, fileStore),
            fileStore,
            picker,
            settings,
            theme);
        return new ProductShellHarness(shell, notes, theme, picker, settings);
    }

    /// <summary>A product shell built over the same database, plus its test doubles.</summary>
    internal sealed record ProductShellHarness(
        ProductShellViewModel Shell,
        NoteWorkspaceViewModel Notes,
        NoOpThemeApplier Theme,
        FakeFilePicker Picker,
        SqliteSettingsStore Settings) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Shell.DisposeAsync();
    }

    private static NoteId NextId() => NoteId.Create(Guid.NewGuid()).Value;

    /// <summary>A shell built over the same database (restart model). Disposes its own view models.</summary>
    internal sealed record FreshShell(
        MainWindowViewModel Main,
        NoteWorkspaceViewModel Notes) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Main.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Main.DisposeAsync();
        await _database.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
