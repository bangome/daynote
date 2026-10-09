using Daynote.App.Composition;
using Daynote.Core.Agenda;
using Daynote.Core.Settings;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Profiles;
using Daynote.Infrastructure.Settings;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Mobile.Platform;

/// <summary>
/// The active profile's store, for code that runs beside the app rather than as it: the Android
/// widgets and the reminder receiver. No network, no UI, and never a migration.
/// </summary>
/// <remarks>
/// <para>
/// <b>The running app's composition when there is one</b> on the same profile: its database is
/// already open, and a second open would only repeat work. Android composes the app in
/// <c>Application.OnCreate</c> even when the process was started for a broadcast, so this is the
/// usual case once the shell has loaded.
/// </para>
/// <para>
/// <b>Otherwise the profile's database is opened as it is</b> (<see cref="SqliteDatabase.TryOpenCurrent"/>):
/// no backup, no migration, no integrity check. After an update the app's own start may be
/// migrating the same file at that moment, and two migrations racing is how the loser throws. A
/// schema this build would still migrate reads as outdated, and the caller says
/// "open the app" instead of guessing.
/// </para>
/// <para>
/// <b>The profile is the one the app would open</b> (<c>profile.json</c>), read without running the
/// layout migration, which also belongs to the app's own start.
/// </para>
/// </remarks>
internal sealed class BackgroundStore : IAsyncDisposable
{
    private readonly SqliteDatabase? _database;

    private BackgroundStore(
        IAgendaRepository agenda,
        ISettingsStore settings,
        ISyncSessionStore? sessions,
        MobileShellViewModel? shell,
        SqliteDatabase? database)
    {
        Agenda = agenda;
        Settings = settings;
        Sessions = sessions;
        Shell = shell;
        _database = database;
    }

    public IAgendaRepository Agenda { get; }

    public ISettingsStore Settings { get; }

    public ISyncSessionStore? Sessions { get; }

    /// <summary>The running app's shell, when this is its composition.</summary>
    public MobileShellViewModel? Shell { get; }

    public bool IsLive => Shell is not null;

    /// <summary>
    /// The store, or null: when the profile has no database yet (the app has never run on it), or
    /// when it has one on a schema this build has not migrated it to yet (<paramref name="outdated"/>).
    /// </summary>
    public static BackgroundStore? Open(string baseRoot, ISecretProtector? protector, out bool outdated)
    {
        outdated = false;
        string folder = new ProfileStore(baseRoot).ResolveActiveFolder();

        if (App.Live is { } live
            && string.Equals(
                Path.GetFullPath(live.Services.GetRequiredService<DaynoteAppOptions>().DataRoot),
                Path.GetFullPath(folder),
                StringComparison.Ordinal))
        {
            return new BackgroundStore(
                live.Services.GetRequiredService<IAgendaRepository>(),
                live.Services.GetRequiredService<ISettingsStore>(),
                live.Services.GetService<ISyncSessionStore>(),
                live.Shell,
                database: null);
        }

        string path = Path.Combine(folder, ProfileStore.DatabaseFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var database = new SqliteDatabase(new SqliteDatabaseOptions(path));
        try
        {
            if (!database.TryOpenCurrent())
            {
                database.DisposeAsync().AsTask().GetAwaiter().GetResult();
                outdated = true;
                return null;
            }
        }
        catch
        {
            database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        return new BackgroundStore(
            new SqliteAgendaRepository(database),
            new SqliteSettingsStore(database, new SystemClock()),
            protector is null ? null : new ProtectedFileSyncSessionStore(folder, protector),
            shell: null,
            database);
    }

    /// <summary>
    /// Whether the account behind this profile has the lock on and this device has not been
    /// unlocked: the same test <see cref="AccountService.ResumeAsync"/> makes for
    /// <see cref="ResumeState.Locked"/>.
    /// </summary>
    public async Task<bool> IsLockedAsync(CancellationToken cancellationToken = default)
    {
        if (Sessions is null)
        {
            return false;
        }

        using SyncCredentials? credentials = await Sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        return credentials is { DataKey: null, Protection: KeyProtection.Passphrase };
    }

    public ValueTask DisposeAsync() => _database?.DisposeAsync() ?? ValueTask.CompletedTask;
}
