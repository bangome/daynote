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
    /// Base address of the cloud sync service, or null when this build has none configured. Cloud
    /// sync is opt-in twice over: the feature is inert without an endpoint, and inert again until the
    /// user signs in. A build with no endpoint makes no network calls at all.
    /// </summary>
    public Uri? SyncEndpoint { get; init; }

    /// <summary>Environment variable that supplies <see cref="SyncEndpoint"/>.</summary>
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
        string? endpoint = Environment.GetEnvironmentVariable(SyncEndpointEnvironmentVariable);
        return new DaynoteAppOptions(root)
        {
            SyncEndpoint = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed)
                && parsed.Scheme == Uri.UriSchemeHttps
                    ? parsed
                    // Anything but https is refused rather than downgraded: the bearer token and the
                    // ciphertext must not cross a plaintext connection.
                    : null,
        };
    }
}
