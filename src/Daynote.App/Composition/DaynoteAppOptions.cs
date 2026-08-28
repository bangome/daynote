using System.IO;

namespace Daynote.App.Composition;

/// <summary>
/// Composition options. Defaults to the per-user data root under <c>%LocalAppData%\Daynote</c> for a
/// real run; tests inject a disposable root.
/// </summary>
public sealed class DaynoteAppOptions
{
    public DaynoteAppOptions(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        DataRoot = Path.GetFullPath(dataRoot);
        DatabasePath = Path.Combine(DataRoot, "daynote.db");
    }

    public string DataRoot { get; }

    public string DatabasePath { get; }

    /// <summary>
    /// Base address of the cloud sync service, or null when this build has none. Cloud sync stays
    /// opt-in: the account section is visible, but nothing leaves the machine until the user signs
    /// in, and a signed-out app makes no network calls at all.
    /// </summary>
    public Uri? SyncEndpoint { get; init; }

    /// <summary>
    /// The deployed service. This is a build-time default rather than something the user configures,
    /// because an environment variable is not a setting a shipped app can ask for: without it
    /// <see cref="SyncEndpoint"/> was null in every installed build, which silently removed the whole
    /// account section from the settings panel. The variable below still overrides it for local
    /// development and QA.
    /// </summary>
    public const string DefaultSyncEndpoint = "https://daynote.arachat.cc";

    /// <summary>
    /// Overrides <see cref="DefaultSyncEndpoint"/>. Set it to <c>off</c> to build a wholly offline
    /// app with no account section at all.
    /// </summary>
    public const string SyncEndpointEnvironmentVariable = "DAYNOTE_SYNC_ENDPOINT";

    /// <summary>
    /// Environment variable that redirects the per-user data root. This is the deterministic-QA
    /// seam consumed by <c>qa/Daynote.UiQa</c>: it lets the harness run the real product against a
    /// namespaced, disposable data root (under the real Daynote root) so QA never touches the
    /// operator's own notes. It is unset during a normal run and the app falls back to
    /// <c>%LocalAppData%\Daynote</c>.
    /// </summary>
    public const string DataRootEnvironmentVariable = "DAYNOTE_DATA_ROOT";

    public static DaynoteAppOptions ForCurrentUser()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        string root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Daynote")
            : overrideRoot;
        return new DaynoteAppOptions(root)
        {
            SyncEndpoint = ResolveSyncEndpoint(
                Environment.GetEnvironmentVariable(SyncEndpointEnvironmentVariable)),
        };
    }

    /// <summary>
    /// Resolves the sync endpoint from an override, falling back to <see cref="DefaultSyncEndpoint"/>.
    /// </summary>
    public static Uri? ResolveSyncEndpoint(string? overrideEndpoint)
    {
        string candidate = string.IsNullOrWhiteSpace(overrideEndpoint)
            ? DefaultSyncEndpoint
            : overrideEndpoint.Trim();

        if (string.Equals(candidate, "off", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
                ? parsed
                // Anything but https is refused rather than downgraded: the bearer token and the
                // ciphertext must not cross a plaintext connection.
                : null;
    }
}
