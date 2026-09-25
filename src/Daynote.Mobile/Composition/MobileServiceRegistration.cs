using Avalonia;
using Avalonia.Controls;
using Daynote.App.Composition;
using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Core.Settings;
using Daynote.Core.Sync;
using Daynote.Core.Time;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Daynote.Infrastructure.Settings;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Mobile.Composition;

/// <summary>
/// The phone composition root. Mirrors <c>DesktopServiceRegistration</c> for everything below the
/// view layer — the same database, repositories, use cases and search — and leaves out what a phone
/// has no version of.
/// </summary>
/// <remarks>
/// <para>
/// Left out on purpose, each because the platform has no such thing rather than because it is not
/// built yet: the login item (apps do not launch at boot), the global hotkey, the MCP registration
/// (there is no Claude Desktop to register with), the updater (the store updates the app), and the
/// backup archive picker (a phone has no user-visible data folder to export to). The settings view
/// model that binds all five is therefore not constructed here; the phone's settings page binds the
/// handful of preferences that do apply.
/// </para>
/// <para>
/// Everything platform-shaped that IS needed arrives as <see cref="MobilePlatformServices"/> from
/// the head, so this file never references Java or UIKit and stays buildable on any machine.
/// </para>
/// </remarks>
public static class MobileServiceRegistration
{
    public static IServiceCollection AddDaynoteMobile(
        this IServiceCollection services,
        DaynoteAppOptions options,
        Application application,
        Func<TopLevel?> topLevel,
        MobilePlatformServices platform)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(platform);

        services.AddSingleton(options);
        services.AddSingleton(platform);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<Func<NoteId>>(static () => NoteId.Create(Guid.NewGuid()).Value);

        services.AddSingleton(_ =>
        {
            var database = new SqliteDatabase(new SqliteDatabaseOptions(options.DatabasePath));
            database.Initialize();
            return database;
        });
        services.AddSingleton<INoteRepository>(sp => new SqliteNoteRepository(sp.GetRequiredService<SqliteDatabase>()));

        // Day files: the same content-addressed store the desktop uses, inside the app sandbox.
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

        // The session store is sealed by the platform's keystore. Without one the app behaves as
        // permanently signed out rather than writing a refresh token in the clear.
        services.AddSingleton<ISyncSessionStore>(_ => platform.SecretProtector is { } protector
            ? new ProtectedFileSyncSessionStore(options.DataRoot, protector)
            : new NullSyncSessionStore());

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

        services.AddSingleton<IThemeApplier>(_ => new MobileThemeApplier(application));
        services.AddSingleton<IFilePicker>(_ => new MobileFilePicker(topLevel));
        services.AddSingleton<IThumbnailLoader, MobileThumbnailLoader>();

        services.AddSingleton(sp => new MobileShellViewModel(
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
            Account = sp.GetService<Daynote.App.Account.AccountViewModel>(),
        });

        services.AddDaynoteMobileCloudSync(options, platform);

        return services;
    }

    /// <summary>Stands in for a sealed store the platform has not supplied: always signed out.</summary>
    private sealed class NullSyncSessionStore : ISyncSessionStore
    {
        public ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SyncCredentials?>(null);

        public ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default) =>
            throw new PlatformNotSupportedException("No secure credential store is available on this platform.");

        public ValueTask UpdateTokensAsync(string accessToken, DateTimeOffset accessExpiresUtc, string refreshToken, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask ClearAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
