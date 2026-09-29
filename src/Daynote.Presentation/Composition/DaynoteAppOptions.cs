using System.IO;

namespace Daynote.App.Composition;

/// <summary>
/// Composition options. Defaults to the per-user data root under <c>%LocalAppData%\Daynote</c> for a
/// real run; tests inject a disposable root.
/// </summary>
/// <remarks>
/// <see cref="DataRoot"/> is the <i>active profile's</i> folder (docs/PROFILES.md §3): the base root for
/// the local profile, <c>accounts/&lt;userId&gt;</c> for an account. Every service built from it — database,
/// attachments, backup, conflict copies, session store — is therefore scoped to one profile without
/// knowing profiles exist.
/// </remarks>
public sealed class DaynoteAppOptions
{
    /// <summary>A single-profile root: base and active folder are the same. What tests use.</summary>
    public DaynoteAppOptions(string dataRoot)
        : this(dataRoot, dataRoot)
    {
    }

    public DaynoteAppOptions(string baseRoot, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        BaseRoot = Path.GetFullPath(baseRoot);
        DataRoot = Path.GetFullPath(dataRoot);
        DatabasePath = Path.Combine(DataRoot, "daynote.db");
    }

    /// <summary>The root every process resolves; holds <c>profile.json</c> and <c>accounts/</c>.</summary>
    public string BaseRoot { get; }

    /// <summary>The active profile's folder.</summary>
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

    public static DaynoteAppOptions ForCurrentUser() =>
        ForBaseRoot(Daynote.Infrastructure.Persistence.DaynoteDataRoot.Resolve());

    /// <summary>
    /// Runs the one-time profile migration under <paramref name="baseRoot"/>, then builds options over
    /// the active profile (docs/PROFILES.md §5.1). The phone heads call this with their sandbox folder;
    /// the desktop apps reach it through <see cref="ForCurrentUser"/>.
    /// </summary>
    /// <remarks>
    /// Must run before any database under the root is opened, which is why it is here and not in the
    /// composition. A failed migration is not fatal: it leaves the root as it was, the pointer then
    /// resolves to the base, and the app runs on the legacy layout exactly as the previous version did
    /// until a later start succeeds.
    /// </remarks>
    public static DaynoteAppOptions ForBaseRoot(string baseRoot)
    {
        var profiles = new Daynote.Infrastructure.Persistence.Profiles.ProfileStore(baseRoot);
        profiles.MigrateLegacyLayout();
        return new DaynoteAppOptions(profiles.BaseRoot, profiles.ResolveActiveFolder())
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
