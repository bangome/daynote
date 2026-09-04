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
    /// Base address of the cloud sync service, or null when this build has none. Null is not a
    /// degraded mode: nothing is registered, there is no <c>HttpClient</c>, the account section is
    /// absent from the settings panel, and the app makes no network calls at all.
    /// </summary>
    public Uri? SyncEndpoint { get; init; }

    /// <summary>
    /// Whether a build points at <see cref="DeployedSyncEndpoint"/> without being asked to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// False, and deliberately so: cloud sync is not finished, so no shipped build offers an account.
    /// Password-reset mail has never been verified end to end, and half a recovery story is worse
    /// than none — a user who signs up, forgets their password, and cannot receive a reset code has
    /// lost their cloud copy for good. Hiding it is the honest state until that path works.
    /// </para>
    /// <para>
    /// This is the whole switch. Flipping it to true is what ships the feature; everything behind it
    /// is built, deployed, and covered by tests. <see cref="SyncEndpointEnvironmentVariable"/>
    /// enables it per-run in the meantime, which is how development and QA reach it.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// A field rather than a <c>const</c> on purpose: a const bool is baked into every referencing
    /// assembly at compile time, so a stale reference could disagree with the app about whether the
    /// feature shipped, and the compiler would fold away the tests that check it.
    /// </remarks>
    public static readonly bool SyncEnabledByDefault = false;

    /// <summary>
    /// The deployed service, used when <see cref="SyncEnabledByDefault"/> is true.
    /// </summary>
    /// <remarks>
    /// A build-time constant rather than something the user configures. It used to come only from
    /// <see cref="SyncEndpointEnvironmentVariable"/>, which no installed build has, so the feature
    /// was missing from every shipped copy by accident rather than by decision. The point of the flag
    /// above is that the decision is now explicit.
    /// </remarks>
    public const string DeployedSyncEndpoint = "https://daynote.arachat.cc";

    /// <summary>
    /// The Google OAuth desktop client this app signs in with.
    /// </summary>
    /// <remarks>
    /// Public by design: an installed app cannot keep a client id secret, and Google does not treat
    /// it as one. Its matching client secret is NOT here — the authorization code is exchanged by
    /// the Worker, which holds that secret, precisely so it never ships inside this binary. The same
    /// value is in cloud/worker/wrangler.toml and the two must agree.
    /// </remarks>
    public const string GoogleClientId =
        "298036592294-mp11166n940ojbq4js3u5233ruvkk2ic.apps.googleusercontent.com";

    /// <summary>
    /// Supplies the endpoint for a single run, overriding <see cref="SyncEnabledByDefault"/> in
    /// either direction: an https URL turns cloud sync on, and <c>off</c> forces it off.
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
    /// Resolves the sync endpoint from an override, falling back to what this build ships with.
    /// </summary>
    public static Uri? ResolveSyncEndpoint(string? overrideEndpoint)
    {
        if (string.IsNullOrWhiteSpace(overrideEndpoint))
        {
            return SyncEnabledByDefault ? new Uri(DeployedSyncEndpoint) : null;
        }

        string candidate = overrideEndpoint.Trim();
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
