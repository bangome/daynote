using System.IO;
using System.Net.Http;
using Daynote.App.Account;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.App.Composition;

/// <summary>
/// Composition for the optional cloud sync feature.
/// </summary>
/// <remarks>
/// Everything here is conditional on <see cref="DaynoteAppOptions.SyncEndpoint"/>. With no endpoint
/// configured nothing is registered, no <see cref="HttpClient"/> exists, and the app makes no network
/// calls — which is the state the shipping build is in until an endpoint is deployed.
/// </remarks>
public static class SyncRegistration
{
    /// <summary>How long to wait on the sync service before treating the attempt as offline.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static IServiceCollection AddDaynoteCloudSync(
        this IServiceCollection services,
        DaynoteAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (options.SyncEndpoint is null)
        {
            return services;
        }

        services.AddSingleton<ISyncCrypto>(_ => new AesGcmSyncCrypto());
        services.AddSingleton<ISyncSessionStore>(_ => new DpapiSyncSessionStore(options.DataRoot));
        services.AddSingleton<ISyncStore>(sp =>
            new SqliteSyncStore(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<ISyncConflictSink>(_ => new FileSystemConflictSink(options.DataRoot));

        services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = options.SyncEndpoint,
            Timeout = Timeout,
        });

        services.AddSingleton<IAuthApiClient>(sp =>
            new HttpAuthApiClient(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<ISyncTokenProvider>(sp => new SyncTokenProvider(
            sp.GetRequiredService<IAuthApiClient>(),
            sp.GetRequiredService<ISyncSessionStore>()));
        services.AddSingleton<ISyncApiClient>(sp => new HttpSyncApiClient(
            sp.GetRequiredService<HttpClient>(),
            sp.GetRequiredService<ISyncTokenProvider>()));

        services.AddSingleton<IIdentityProvider>(_ =>
            new GoogleIdentityProvider(DaynoteAppOptions.GoogleClientId));
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

        services.AddSingleton<IRecoveryKeyExporter, WpfRecoveryKeyExporter>();
        services.AddSingleton(sp => new AccountViewModel(
            sp.GetRequiredService<AccountService>(),
            sp.GetRequiredService<ISyncStore>(),
            () => RunSyncAsync(sp),
            sp.GetRequiredService<IRecoveryKeyExporter>(),
            OpenExternal,
            Path.Combine(options.DataRoot, "conflicts")));

        return services;
    }

    /// <summary>
    /// Resolves the session and runs one cycle. The session is resolved per run rather than held,
    /// because signing out must take effect immediately rather than at the next app launch.
    /// </summary>
    private static async ValueTask<SyncReport> RunSyncAsync(IServiceProvider provider)
    {
        ResumedSession resumed = await provider
            .GetRequiredService<AccountService>()
            .ResumeAsync()
            .ConfigureAwait(false);

        if (resumed.Session is not { } session)
        {
            // A session without its key is not a signed-out one: the tokens still work. Locked means
            // the passphrase is needed; KeyMissing means the key is simply re-fetched.
            return SyncReport.For(
                resumed.State is ResumeState.KeyMissing or ResumeState.Locked
                    ? SyncOutcome.Locked
                    : SyncOutcome.SignedOut);
        }

        using (session.DataKey)
        {
            return await provider
                .GetRequiredService<SyncEngine>()
                .SyncAsync(session)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens a folder or a URL with the shell. One delegate for both because the shell makes no
    /// distinction, and the two callers — "show me the conflict copies" and "take me to the
    /// checkout page" — want exactly the same behaviour.
    /// </summary>
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
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            // No shell handler, or the folder could not be created. Not worth interrupting the user
            // over: the conflicts path is documented in DATA_AND_RECOVERY.md, and a checkout link
            // that will not open is visible in the settings panel to copy by hand.
        }
    }
}

/// <summary>Puts the one-time recovery key on the clipboard or into a file the user chooses.</summary>
internal sealed class WpfRecoveryKeyExporter : IRecoveryKeyExporter
{
    public Task<bool> TryCopyToClipboardAsync(string recoveryKey)
    {
        return Task.FromResult(TryCopyToClipboard(recoveryKey));
    }

    private static bool TryCopyToClipboard(string recoveryKey)
    {
        try
        {
            System.Windows.Clipboard.SetText(recoveryKey);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process holds the clipboard. The key is still on screen to copy by hand.
            return false;
        }
    }

    public Task<bool> TrySaveToFileAsync(string recoveryKey)
    {
        return Task.FromResult(TrySaveToFile(recoveryKey));
    }

    private static bool TrySaveToFile(string recoveryKey)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "daynote-recovery-key.txt",
            Filter = Localization.AppStrings.RecoveryKeyFileFilter,
            AddExtension = true,
            DefaultExt = ".txt",
        };

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        try
        {
            File.WriteAllText(dialog.FileName, recoveryKey + Environment.NewLine);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
