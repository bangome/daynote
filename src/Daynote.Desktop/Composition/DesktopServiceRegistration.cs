using Avalonia;
using Avalonia.Controls;
using Daynote.App.Composition;
using Daynote.App.Input;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Settings;
using Daynote.Core.Startup;
using Daynote.Core.Sync;
using Daynote.Core.Time;
using Daynote.Desktop.Platform;
using Daynote.Desktop.ViewModels;
using Daynote.Infrastructure.Mcp;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Daynote.Infrastructure.Settings;
using Daynote.Infrastructure.Startup;
using Daynote.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop.Composition;

/// <summary>
/// The Avalonia composition root. Mirrors the WPF <c>ServiceRegistration</c> for everything below the
/// view layer, and swaps in the platform services this OS has: the LaunchAgent login item and the
/// Keychain-sealed session store on macOS, DPAPI on Windows.
/// </summary>
public static class DesktopServiceRegistration
{
    /// <summary>launchd label for the macOS login item.</summary>
    public const string LaunchAgentLabel = "cc.arachat.daynote";

    /// <param name="topLevel">Resolves the window that owns dialogs (null until the shell is shown).</param>
    /// <param name="requestRestartForRestore">Quits (flushing) and relaunches so a staged restore applies.</param>
    public static IServiceCollection AddDaynoteDesktop(
        this IServiceCollection services,
        DaynoteAppOptions options,
        Application application,
        Func<TopLevel?> topLevel,
        Action requestRestartForRestore)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(requestRestartForRestore);

        services.AddSingleton(options);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<Func<NoteId>>(static () => NoteId.Create(Guid.NewGuid()).Value);

        services.AddSingleton(_ =>
        {
            var database = new SqliteDatabase(new SqliteDatabaseOptions(options.DatabasePath));
            database.Initialize();
            return database;
        });
        services.AddSingleton<INoteRepository>(sp => new SqliteNoteRepository(sp.GetRequiredService<SqliteDatabase>()));

        // Day files: content-addressed store, repository, use cases (same wiring as the WPF app).
        services.AddSingleton<IFileAssetStore>(_ => new Infrastructure.Assets.ContentAddressedFileStore(options.DataRoot));
        services.AddSingleton<IDayFileRepository>(sp =>
            new Infrastructure.Files.SqliteDayFileRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton(sp => new AddDayFile(
            sp.GetRequiredService<IDayFileRepository>(), sp.GetRequiredService<IFileAssetStore>()));
        services.AddSingleton(sp => new ListDayFiles(
            sp.GetRequiredService<IDayFileRepository>(), sp.GetRequiredService<IFileAssetStore>()));
        services.AddSingleton(sp => new DeleteDayFile(
            sp.GetRequiredService<IDayFileRepository>(), sp.GetRequiredService<IFileAssetStore>()));
        services.AddSingleton<ISearchRepository>(sp => new SqliteSearchRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton(sp => new SearchService(sp.GetRequiredService<ISearchRepository>()));
        services.AddSingleton<ISettingsStore>(sp => new SqliteSettingsStore(
            sp.GetRequiredService<SqliteDatabase>(), sp.GetRequiredService<IClock>()));

        services.AddSingleton<IStartupTaskService>(_ => new MsixStartupTaskService(CreateStartupGateway()));
        services.AddSingleton<IGlobalHotkeyService>(_ => CreateHotkeyService());
        services.AddSingleton(sp => new ConfigurableShortcuts(sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<Core.Backup.IBackupService>(
            new Infrastructure.Backup.BackupService(options.DataRoot, options.DatabasePath));
        services.AddSingleton<Core.Mcp.IMcpRegistrationService>(_ =>
            new ClaudeDesktopMcpRegistration(ClaudeDesktopMcpRegistration.DefaultConfigPath, McpServerCommand.Current));

        // The session store is platform-sealed. Registered regardless of the sync endpoint so the
        // account feature can be switched on later without touching composition.
        services.AddSingleton<ISyncSessionStore>(_ => CreateSessionStore(options.DataRoot));

        services.AddSingleton(sp => new GetDayWorkspace(sp.GetRequiredService<INoteRepository>()));
        services.AddSingleton(sp => new CreateNote(sp.GetRequiredService<INoteRepository>(), sp.GetRequiredService<Func<NoteId>>()));
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

        services.AddSingleton<IThemeApplier>(_ => new AvaloniaThemeApplier(application));
        services.AddSingleton<IFilePicker>(_ => new AvaloniaFilePicker(topLevel));
        services.AddSingleton<Daynote.App.Settings.IBackupArchivePicker>(_ => new AvaloniaBackupArchivePicker(topLevel));
        services.AddSingleton<IThumbnailLoader, AvaloniaThumbnailLoader>();
        services.AddSingleton(sp => new DesktopShellViewModel(
            sp.GetRequiredService<NoteWorkspaceViewModel>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<SearchService>(),
            sp.GetRequiredService<INoteRepository>(),
            sp.GetRequiredService<AddDayFile>(),
            sp.GetRequiredService<ListDayFiles>(),
            sp.GetRequiredService<DeleteDayFile>(),
            sp.GetRequiredService<IFileAssetStore>(),
            sp.GetRequiredService<IFilePicker>(),
            sp.GetRequiredService<IThumbnailLoader>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IThemeApplier>())
        {
            SettingsViewModel = new DesktopSettingsViewModel(
                sp.GetRequiredService<ISettingsStore>(),
                sp.GetRequiredService<IStartupTaskService>(),
                sp.GetRequiredService<Core.Mcp.IMcpRegistrationService>(),
                sp.GetRequiredService<IGlobalHotkeyService>(),
                sp.GetRequiredService<ConfigurableShortcuts>(),
                sp.GetRequiredService<Core.Backup.IBackupService>(),
                sp.GetRequiredService<Daynote.App.Settings.IBackupArchivePicker>(),
                async () => (await sp.GetRequiredService<NoteWorkspaceViewModel>()
                    .FlushAsync(FlushReason.Quit).ConfigureAwait(true)).CanProceed,
                requestRestartForRestore,
                options.DataRoot,
                OpenExternal),
            Account = sp.GetService<Daynote.App.Account.AccountViewModel>(),
            Tutorial = new Daynote.App.Onboarding.TutorialViewModel(
                sp.GetRequiredService<ISettingsStore>(),
                sp.GetRequiredService<ConfigurableShortcuts>(),
                sp.GetRequiredService<IStartupTaskService>()),
        });

        // Optional cloud sync (null endpoint = nothing registered, no network).
        services.AddDaynoteDesktopCloudSync(options, topLevel, OpenExternal);

        return services;
    }

    /// <summary>Opens a folder or URL with the OS (Finder / default browser). Failures are not worth a dialog.</summary>
    private static void OpenExternal(string target)
    {
        try
        {
            if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(target);
            }

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
        }
    }

    private static IGlobalHotkeyService CreateHotkeyService()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacGlobalHotkeyService();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsGlobalHotkeyService();
        }

        return new NullGlobalHotkeyService();
    }

    private static IStartupTaskGateway CreateStartupGateway()
    {
        if (Environment.ProcessPath is not { Length: > 0 } executable)
        {
            return new UnavailableStartupTaskGateway();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new LaunchAgentStartupTaskGateway(LaunchAgentLabel, executable);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsRunKeyStartupTaskGateway("Daynote", executable);
        }

        return new UnavailableStartupTaskGateway();
    }

    private static ISyncSessionStore CreateSessionStore(string dataRoot)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new ProtectedFileSyncSessionStore(dataRoot, new MacKeychainSecretProtector());
        }

        if (OperatingSystem.IsWindows())
        {
            return new ProtectedFileSyncSessionStore(dataRoot, new DpapiSecretProtector());
        }

        // No sealed store on this OS yet: behave as permanently signed out rather than write plaintext.
        return new NullSyncSessionStore();
    }

    private sealed class NullSyncSessionStore : ISyncSessionStore
    {
        public ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SyncCredentials?>(null);

        public ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default) =>
            throw new PlatformNotSupportedException("No secure credential store is available on this operating system.");

        public ValueTask UpdateTokensAsync(string accessToken, DateTimeOffset accessExpiresUtc, string refreshToken, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
