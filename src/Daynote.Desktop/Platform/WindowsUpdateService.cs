using System.Runtime.Versioning;
using Velopack;
using Velopack.Sources;

namespace Daynote.Desktop.Platform;

/// <summary>What the app can do about a newer version of itself.</summary>
public interface IUpdateService
{
    /// <summary>
    /// Looks for a newer release and downloads it. Returns true when one is staged and will be
    /// applied on the next start.
    /// </summary>
    ValueTask<bool> CheckAndStageAsync(CancellationToken cancellationToken = default);
}

/// <summary>Nothing to do: this build has no update feed (macOS, or a plain zip).</summary>
public sealed class NoUpdateService : IUpdateService
{
    public ValueTask<bool> CheckAndStageAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

/// <summary>
/// The Windows auto-updater for an unpackaged install — what the Microsoft Store does for the
/// packaged one (docs/WINDOWS_ON_AVALONIA.md §5b).
/// </summary>
/// <remarks>
/// <para>
/// Not on the shipping path today: §3 settled on the Store for Windows, so this is the updater for a
/// channel that is built and parked. It is inert until <c>Program.UpdateFeedUrl</c> is filled in, and
/// inert in a packaged build regardless, because <c>manager.IsInstalled</c> is false there.
/// </para>
/// Downloads in the background and applies on the next start, rather than restarting under the user.
/// This is a note-taking app that people leave open for days; interrupting it to install something
/// they did not ask for is worse than waiting for the next launch.
/// <para>
/// Every failure is swallowed. A missing feed, no network, a corporate proxy, a release that will not
/// parse — none of those are the user's problem and none of them should produce a dialog in a note
/// app. The consequence of getting this wrong is staying on an older version, which is where the app
/// already is.
/// </para>
/// <para>
/// It does nothing at all unless the app is running from a Velopack install: a developer build, or a
/// copy someone unzipped, has no update source and must not try to rewrite itself.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateService(string feedUrl) : IUpdateService
{
    public async ValueTask<bool> CheckAndStageAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return false;
        }

        try
        {
            var manager = new UpdateManager(new SimpleWebSource(feedUrl));
            if (!manager.IsInstalled)
            {
                return false;
            }

            UpdateInfo? update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return false;
            }

            await manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
