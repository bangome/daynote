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
    /// True since 2026-09-08: shipped builds offer an account.
    /// </para>
    /// <para>
    /// It was false, and the reason given was that password-reset mail had never been verified end to
    /// end — a user who forgot their password and could not receive a reset code would lose their
    /// cloud copy. That reason did not survive the move to Google sign-in: there is no password to
    /// forget and no reset mail to send. <c>cloud/worker/src/auth.ts</c> says so directly ("There is
    /// no register endpoint and no password"), and the Worker exposes no reset route. The recovery
    /// story that remains belongs to the optional note lock, which issues a recovery key, offers to
    /// save it to a file, and makes the user acknowledge it before the lock takes effect.
    /// </para>
    /// <para>
    /// This is the whole switch, and everything behind it was already built, deployed and covered by
    /// tests. <see cref="SyncEndpointEnvironmentVariable"/> still overrides it either way, which is
    /// how a run can be forced off.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// A field rather than a <c>const</c> on purpose: a const bool is baked into every referencing
    /// assembly at compile time, so a stale reference could disagree with the app about whether the
    /// feature shipped, and the compiler would fold away the tests that check it.
    /// </remarks>
    public static readonly bool SyncEnabledByDefault = true;

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
    public const string DataRootEnvironmentVariable =
        Daynote.Infrastructure.Persistence.DaynoteDataRoot.EnvironmentVariable;

    public static DaynoteAppOptions ForCurrentUser()
    {
        return new DaynoteAppOptions(Daynote.Infrastructure.Persistence.DaynoteDataRoot.Resolve())
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
