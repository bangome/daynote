using Daynote.App.Account;
using Daynote.App.Composition;
using Daynote.Core.Files;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Profiles;
using Daynote.Infrastructure.Sync;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Mobile.Composition;

/// <summary>
/// Cloud sync on a phone, mirroring <c>DesktopSyncRegistration</c>: nothing is registered without an
/// endpoint, so a build with sync off makes no network calls at all.
/// </summary>
/// <remarks>
/// Two things differ from the desktop, both forced by the platform. Sign-in uses the head's identity
/// provider (an in-app browser tab returning to a custom scheme) rather than a loopback listener,
/// because a phone app cannot bind a port the system browser can reach. And the recovery key is
/// exported through the share sheet rather than a save dialog, since there is no file system the
/// user browses.
/// </remarks>
public static class MobileSyncRegistration
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static IServiceCollection AddDaynoteMobileCloudSync(
        this IServiceCollection services, DaynoteAppOptions options, MobilePlatformServices platform)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platform);

        // No endpoint, or no way to sign in on this platform: the account section stays absent.
        if (options.SyncEndpoint is null || platform.Identity is null)
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
        services.AddSingleton(_ => platform.Identity);
        // One store per account (docs/PROFILES.md): a sign-in is handed to its own folder, sealed by
        // the same keystore as the folder the app runs on.
        services.AddSingleton<IProfileHost>(sp => new ProfileHost(
            new ProfileStore(options.BaseRoot),
            options.DataRoot,
            sp.GetRequiredService<SqliteDatabase>(),
            MobileServiceRegistration.SessionStoreFactory(platform)));
        services.AddSingleton(sp => new AccountService(
            sp.GetRequiredService<IAuthApiClient>(),
            sp.GetRequiredService<IIdentityProvider>(),
            sp.GetRequiredService<ISyncCrypto>(),
            sp.GetRequiredService<ISyncSessionStore>(),
            sp.GetRequiredService<ISyncStore>(),
            apple: platform.AppleIdentity,
            profiles: sp.GetRequiredService<IProfileHost>()));
        services.AddSingleton<ISyncAssetStore>(sp => new SqliteFileSyncAssetStore(
            sp.GetRequiredService<SqliteDatabase>(),
            sp.GetRequiredService<IFileAssetStore>()));
        services.AddSingleton(sp => new SyncEngine(
            sp.GetRequiredService<ISyncApiClient>(),
            sp.GetRequiredService<ISyncCrypto>(),
            sp.GetRequiredService<ISyncStore>(),
            conflicts: sp.GetRequiredService<ISyncConflictSink>(),
            assets: sp.GetRequiredService<ISyncAssetStore>()));

        services.AddSingleton<IRecoveryKeyExporter>(_ => new MobileRecoveryKeyExporter(platform.TopLevel));
        services.AddSingleton(sp => new AccountViewModel(
            sp.GetRequiredService<AccountService>(),
            sp.GetRequiredService<ISyncStore>(),
            () => RunSyncAsync(sp),
            sp.GetRequiredService<IRecoveryKeyExporter>(),
            platform.OpenExternal,
            Path.Combine(options.DataRoot, "conflicts"))
        {
            IsPhone = true,
        });

        // In-App Purchase, where the head has it (iOS). Built with the account, so its payment-queue
        // handler is attached as soon as the shell is, which is at launch.
        if (platform.Store is { } store)
        {
            services.AddSingleton(sp => new MobileStoreViewModel(
                store,
                sp.GetRequiredService<AccountViewModel>(),
                token => sp.GetRequiredService<AccountService>().SignedInUserIdAsync(token),
                (transactionId, token) => sp.GetRequiredService<AccountService>().SubmitAppStoreTransactionAsync(transactionId, token),
                openExternal: platform.OpenExternal));
        }

        return services;
    }

    /// <summary>Resolves the session per run so signing out takes effect immediately.</summary>
    private static async ValueTask<SyncReport> RunSyncAsync(IServiceProvider provider)
    {
        ResumedSession resumed = await provider.GetRequiredService<AccountService>().ResumeAsync().ConfigureAwait(false);
        if (resumed.Session is not { } session)
        {
            return SyncReport.For(resumed.State switch
            {
                ResumeState.KeyMissing or ResumeState.Locked => SyncOutcome.Locked,
                ResumeState.SignInRequired => SyncOutcome.SignInRequired,
                _ => SyncOutcome.SignedOut,
            });
        }

        using (session.DataKey)
        {
            return await provider.GetRequiredService<SyncEngine>().SyncAsync(session).ConfigureAwait(false);
        }
    }
}
