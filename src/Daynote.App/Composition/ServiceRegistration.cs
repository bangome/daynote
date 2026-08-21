using Daynote.App.Lifecycle;
using Daynote.App.Notes;
using Daynote.App.Shell;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Settings;
using Daynote.Core.Startup;
using Daynote.Core.Time;
using Daynote.Infrastructure.Assets;
using Daynote.Infrastructure.Files;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Daynote.Infrastructure.Settings;
using Daynote.Infrastructure.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.App.Composition;

/// <summary>
/// Composition root wiring. The SQLite database, repositories, use cases, services, and view models
/// are registered here; the data root is injectable so tests use a disposable directory.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddDaynote(this IServiceCollection services, DaynoteAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<Func<NoteId>>(static () => NoteId.Create(Guid.NewGuid()).Value);

        services.AddSingleton(sp =>
        {
            var database = new SqliteDatabase(new SqliteDatabaseOptions(options.DatabasePath));
            database.Initialize();
            return database;
        });
        services.AddSingleton<INoteRepository>(sp => new SqliteNoteRepository(sp.GetRequiredService<SqliteDatabase>()));

        // Day files (redesign): content-addressed store, repository, use cases, and reconciler.
        services.AddSingleton<IFileAssetStore>(_ => new ContentAddressedFileStore(options.DataRoot));
        services.AddSingleton<IDayFileRepository>(sp =>
            new SqliteDayFileRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton(sp => new AddDayFile(
            sp.GetRequiredService<IDayFileRepository>(), sp.GetRequiredService<IFileAssetStore>()));
        services.AddSingleton(sp => new ListDayFiles(
            sp.GetRequiredService<IDayFileRepository>(), sp.GetRequiredService<IFileAssetStore>()));
        services.AddSingleton(sp => new DeleteDayFile(
            sp.GetRequiredService<IDayFileRepository>(), sp.GetRequiredService<IFileAssetStore>()));
        services.AddSingleton(sp => new FileAssetReconciler(
            sp.GetRequiredService<IDayFileRepository>(), sp.GetRequiredService<IFileAssetStore>()));

        services.AddSingleton<ISearchRepository>(sp =>
            new SqliteSearchRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton(sp => new SearchService(sp.GetRequiredService<ISearchRepository>()));

        // Lifecycle (Todo 10): settings and startup task.
        services.AddSingleton<ISettingsStore>(sp => new SqliteSettingsStore(
            sp.GetRequiredService<SqliteDatabase>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton<IStartupTaskService>(_ =>
            new MsixStartupTaskService(new WindowsStartupTaskGateway(StartupTaskId)));
        services.AddSingleton<Daynote.Core.Backup.IBackupService>(
            new Daynote.Infrastructure.Backup.BackupService(options.DataRoot, options.DatabasePath));
        services.AddSingleton<Settings.IBackupFilePicker, Settings.Win32BackupFilePicker>();

        // AI integration: registers the MCP server that ships inside the package (docs/MCP.md). The
        // command is the package's app execution alias, which is what makes the server see the same
        // virtualized database as the app.
        services.AddSingleton<Daynote.Core.Mcp.IMcpRegistrationService>(_ =>
            new Daynote.Infrastructure.Mcp.ClaudeDesktopMcpRegistration(
                Daynote.Infrastructure.Mcp.ClaudeDesktopMcpRegistration.DefaultConfigPath,
                Daynote.Infrastructure.Mcp.McpServerCommand.Current));
        services.AddSingleton(sp => new Input.ConfigurableShortcuts(sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton(sp => new Onboarding.TutorialViewModel(
            sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<Input.ConfigurableShortcuts>(),
            sp.GetRequiredService<IStartupTaskService>()));
        services.AddSingleton(sp => new GetDayWorkspace(sp.GetRequiredService<INoteRepository>()));
        services.AddSingleton(sp => new CreateNote(
            sp.GetRequiredService<INoteRepository>(), sp.GetRequiredService<Func<NoteId>>()));
        services.AddSingleton(sp => new ReorderNotes(sp.GetRequiredService<INoteRepository>()));
        services.AddSingleton(sp => new DeleteNote(sp.GetRequiredService<INoteRepository>()));
        services.AddSingleton(sp => new ToggleNoteFavorite(sp.GetRequiredService<INoteRepository>()));
        services.AddSingleton(sp => new SetNoteTags(sp.GetRequiredService<INoteRepository>()));

        services.AddSingleton(sp => new NoteWorkspaceDependencies(
            sp.GetRequiredService<INoteRepository>(),
            sp.GetRequiredService<GetDayWorkspace>(),
            sp.GetRequiredService<CreateNote>(),
            sp.GetRequiredService<ReorderNotes>(),
            sp.GetRequiredService<DeleteNote>(),
            sp.GetRequiredService<Func<NoteId>>(),
            toggleFavorite: sp.GetRequiredService<ToggleNoteFavorite>(),
            setTags: sp.GetRequiredService<SetNoteTags>()));
        services.AddSingleton(sp => new NoteWorkspaceViewModel(sp.GetRequiredService<NoteWorkspaceDependencies>()));
        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<NoteWorkspaceViewModel>(),
            sp.GetRequiredService<IClock>(),
            LayoutThresholds.FromApplicationResources(),
            sp.GetRequiredService<SearchService>()));
        services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<MainWindowViewModel>()));

        // Calendar Notes product shell (redesign). Reuses the note engine and use cases; adds the
        // calendar/todo/files/search surfaces, theme swapping, and file picking.
        services.AddSingleton<Shell.Product.IFilePicker, Shell.Product.Win32FilePicker>();
        services.AddSingleton<Shell.Product.IThemeApplier>(_ =>
            new Shell.Product.WpfProductThemeApplier(System.Windows.Application.Current));
        services.AddSingleton(sp => new Shell.Product.ProductShellViewModel(
            sp.GetRequiredService<NoteWorkspaceViewModel>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<SearchService>(),
            sp.GetRequiredService<INoteRepository>(),
            sp.GetRequiredService<AddDayFile>(),
            sp.GetRequiredService<ListDayFiles>(),
            sp.GetRequiredService<DeleteDayFile>(),
            sp.GetRequiredService<IFileAssetStore>(),
            sp.GetRequiredService<Shell.Product.IFilePicker>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<Shell.Product.IThemeApplier>()));
        services.AddSingleton(sp => new Shell.Product.ProductWindow(
            sp.GetRequiredService<Shell.Product.ProductShellViewModel>()));

        // Optional cloud sync. Registers nothing when no endpoint is configured, so a build without
        // one has no HttpClient and makes no network calls.
        services.AddDaynoteCloudSync(options);

        return services;
    }

    /// <summary>The MSIX StartupTask id declared in the package manifest (Todo 11).</summary>
    public const string StartupTaskId = "DaynoteStartupTask";
}
