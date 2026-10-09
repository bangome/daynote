using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Localization;
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

namespace Daynote.Mobile.Widgets;

/// <summary>
/// What the home-screen widgets read and write: the active profile's own store, with no network
/// and no UI.
/// </summary>
/// <remarks>
/// <para>
/// <b>The running app's composition when there is one.</b> A widget update usually happens because
/// the app just changed a to-do, and its database is already open; a second one in the same process
/// would run the integrity check again for every tick. Only when the app is not up (the process was
/// started for the broadcast), or is composed over another profile mid-switch, is the profile's
/// database opened here, read, and closed again.
/// </para>
/// <para>
/// <b>A tick goes through the same store the app ticks through</b>, so the outbox triggers queue it
/// for sync exactly as an in-app tick is queued. With the app running the shell does it, which also
/// redraws the app and asks for a sync soon.
/// </para>
/// <para>
/// <b>The profile is the one the app would open</b> (<c>profile.json</c>), read without running the
/// layout migration: that belongs to the app's own start, and a widget must not be what moves
/// folders around.
/// </para>
/// </remarks>
public static class WidgetData
{
    /// <summary>The snapshot for the active profile, as of <paramref name="now"/>.</summary>
    public static async Task<WidgetSnapshot> ReadAsync(
        string baseRoot,
        ISecretProtector? protector,
        DateTime now,
        IReadOnlyCollection<WidgetSettling> settling,
        CancellationToken cancellationToken = default)
    {
        await using Source? source = Open(baseRoot, protector);
        if (source is null)
        {
            // No database yet: the app has never run on this profile.
            return WidgetSnapshot.Build(now, [], [], AppLanguages.FromSystem());
        }

        AppLanguage language = source.IsLive
            ? LocalizationService.Instance.Language
            : await LanguageStartup.ResolveAsync(source.Settings, cancellationToken).ConfigureAwait(false);

        if (await IsLockedAsync(source.Sessions, cancellationToken).ConfigureAwait(false))
        {
            return WidgetSnapshot.Locked(now, language);
        }

        IReadOnlyList<AgendaItem> items = await source.Agenda.GetAllAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AgendaList> lists = await source.Agenda.GetListsAsync(cancellationToken).ConfigureAwait(false);
        return WidgetSnapshot.Build(now, items, lists, language, settling);
    }

    /// <summary>
    /// Ticks (or unticks) the row <paramref name="key"/> stands for on <paramref name="day"/>.
    /// False when it is not there any more, or the profile is locked.
    /// </summary>
    public static async Task<bool> ToggleAsync(
        string baseRoot,
        ISecretProtector? protector,
        WidgetRowKey key,
        DateOnly day,
        CancellationToken cancellationToken = default)
    {
        await using Source? source = Open(baseRoot, protector);
        if (source is null || await IsLockedAsync(source.Sessions, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        IReadOnlyList<AgendaItem> items = await source.Agenda.GetAllAsync(cancellationToken).ConfigureAwait(false);
        AgendaDayView view = AgendaDay.For(day, items);
        AgendaDayRow? found = view.Open.Concat(view.Done)
            .Cast<AgendaDayRow?>()
            .FirstOrDefault(row => row is { } r && WidgetRowKey.Of(r) == key);
        if (found is not { } row || row.Item.Kind != AgendaKind.Task)
        {
            return false;
        }

        if (source.Shell is { } shell)
        {
            await Dispatcher.UIThread.InvokeAsync(() => shell.ToggleFromWidgetAsync(row));
        }
        else
        {
            await new ToggleAgendaItem(source.Agenda).ToggleAsync(row, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Whether the account behind this profile has the lock on and this device has not been
    /// unlocked: the same test <see cref="AccountService.ResumeAsync"/> makes for
    /// <see cref="ResumeState.Locked"/>.
    /// </summary>
    private static async Task<bool> IsLockedAsync(ISyncSessionStore? sessions, CancellationToken cancellationToken)
    {
        if (sessions is null)
        {
            return false;
        }

        using SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        return credentials is { DataKey: null, Protection: KeyProtection.Passphrase };
    }

    private static Source? Open(string baseRoot, ISecretProtector? protector)
    {
        string folder = new ProfileStore(baseRoot).ResolveActiveFolder();

        if (App.Live is { } live
            && string.Equals(
                Path.GetFullPath(live.Services.GetRequiredService<DaynoteAppOptions>().DataRoot),
                Path.GetFullPath(folder),
                StringComparison.Ordinal))
        {
            return new Source(
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
            database.Initialize();
        }
        catch
        {
            database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        return new Source(
            new SqliteAgendaRepository(database),
            new SqliteSettingsStore(database, new SystemClock()),
            protector is null ? null : new ProtectedFileSyncSessionStore(folder, protector),
            shell: null,
            database);
    }

    /// <summary>The repositories to use, and the database to close afterwards when this opened one.</summary>
    private sealed class Source(
        IAgendaRepository agenda,
        ISettingsStore settings,
        ISyncSessionStore? sessions,
        MobileShellViewModel? shell,
        SqliteDatabase? database) : IAsyncDisposable
    {
        public IAgendaRepository Agenda { get; } = agenda;

        public ISettingsStore Settings { get; } = settings;

        public ISyncSessionStore? Sessions { get; } = sessions;

        public MobileShellViewModel? Shell { get; } = shell;

        public bool IsLive => database is null;

        public ValueTask DisposeAsync() => database?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
