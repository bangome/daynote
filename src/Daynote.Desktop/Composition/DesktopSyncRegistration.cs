using Avalonia.Controls;
using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.Core.Sync;
using Daynote.Desktop.Platform;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop.Composition;

/// <summary>
/// Optional cloud sync for the Avalonia app, mirroring the WPF <c>SyncRegistration</c>: nothing is
/// registered without an endpoint, so the default build has no HttpClient and makes no network calls.
/// The session store itself is registered unconditionally by <see cref="DesktopServiceRegistration"/>.
/// </summary>
public static class DesktopSyncRegistration
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static IServiceCollection AddDaynoteDesktopCloudSync(
        this IServiceCollection services, DaynoteAppOptions options, Func<TopLevel?> topLevel, Action<string> openExternal)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (options.SyncEndpoint is null)
        {
            return services;
        }

        services.AddSingleton<ISyncCrypto>(_ => new AesGcmSyncCrypto());
        services.AddSingleton<ISyncStore>(sp => new SqliteSyncStore(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<ISyncConflictSink>(_ => new FileSystemConflictSink(options.DataRoot));
        services.AddSingleton(_ => new HttpClient { BaseAddress = options.SyncEndpoint, Timeout = Timeout });
        services.AddSingleton<IAuthApiClient>(sp => new HttpAuthApiClient(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<ISyncTokenProvider>(sp => new SyncTokenProvider(
            sp.GetRequiredService<IAuthApiClient>(), sp.GetRequiredService<ISyncSessionStore>()));
        services.AddSingleton<ISyncApiClient>(sp => new HttpSyncApiClient(
            sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISyncTokenProvider>()));
        services.AddSingleton<IIdentityProvider>(_ => new GoogleIdentityProvider(DaynoteAppOptions.GoogleClientId));
        services.AddSingleton(sp => new AccountService(
            sp.GetRequiredService<IAuthApiClient>(),
            sp.GetRequiredService<IIdentityProvider>(),
            sp.GetRequiredService<ISyncCrypto>(),
            sp.GetRequiredService<ISyncSessionStore>(),
            sp.GetRequiredService<ISyncStore>()));
        services.AddSingleton(sp => new SyncEngine(
            sp.GetRequiredService<ISyncApiClient>(),
            sp.GetRequiredService<ISyncCrypto>(),
            sp.GetRequiredService<ISyncStore>(),
            conflicts: sp.GetRequiredService<ISyncConflictSink>()));

        services.AddSingleton<IRecoveryKeyExporter>(_ => new AvaloniaRecoveryKeyExporter(topLevel));
        services.AddSingleton(sp => new AccountViewModel(
            sp.GetRequiredService<AccountService>(),
            sp.GetRequiredService<ISyncStore>(),
            () => RunSyncAsync(sp),
            sp.GetRequiredService<IRecoveryKeyExporter>(),
            openExternal,
            Path.Combine(options.DataRoot, "conflicts")));

        return services;
    }

    /// <summary>Resolves the session per run so signing out takes effect immediately.</summary>
    private static async ValueTask<SyncReport> RunSyncAsync(IServiceProvider provider)
    {
        ResumedSession resumed = await provider.GetRequiredService<AccountService>().ResumeAsync().ConfigureAwait(false);
        if (resumed.Session is not { } session)
        {
            return SyncReport.For(
                resumed.State is ResumeState.KeyMissing or ResumeState.Locked ? SyncOutcome.Locked : SyncOutcome.SignedOut);
        }

        using (session.DataKey)
        {
            return await provider.GetRequiredService<SyncEngine>().SyncAsync(session).ConfigureAwait(false);
        }
    }
}
