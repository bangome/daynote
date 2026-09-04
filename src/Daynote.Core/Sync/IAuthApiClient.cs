namespace Daynote.Core.Sync;

/// <summary>
/// The authorization grant collected from the identity provider's browser flow.
/// </summary>
/// <remarks>
/// The code is exchanged for tokens by the Worker, not by this app: that exchange needs the OAuth
/// client secret, and a secret shipped inside a Windows binary is a published secret. The PKCE
/// verifier travels with the code so the Worker can prove the exchange belongs to the browser
/// session that started it. See docs/CLOUD_SYNC.md §4.
/// </remarks>
public sealed record IdentityGrant(string AuthorizationCode, string CodeVerifier, string RedirectUri);

/// <summary>
/// Runs the interactive sign-in for one identity provider. Google today; the shape is deliberately
/// provider-agnostic so Apple can be added without touching <see cref="AccountService"/>.
/// </summary>
public interface IIdentityProvider
{
    /// <summary>
    /// Opens the system browser and waits for the redirect. Throws <see cref="AccountException"/>
    /// with <see cref="AccountFailure.SignInCancelled"/> if the user closes the window or the wait
    /// times out — an ordinary outcome, not a fault.
    /// </summary>
    ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default);
}

public sealed record GoogleSignInRequest(
    string AuthorizationCode,
    string CodeVerifier,
    string RedirectUri,
    string DeviceName);

/// <summary>Which custody an account's data key is in (docs/CLOUD_SYNC.md §4.1b).</summary>
public enum KeyProtection
{
    /// <summary>The default: the server holds the key, so it can read note content.</summary>
    Server,

    /// <summary>The opt-in lock: the key is wrapped under a passphrase the server never sees.</summary>
    Passphrase,
}

/// <summary>
/// How this device gets at the data key. Exactly one of the two shapes is populated, decided by
/// <see cref="Protection"/>.
/// </summary>
/// <remarks>
/// With <see cref="KeyProtection.Server"/> the key arrives ready to use, and the server can read
/// note content — that is the cost of one-click sign-in (docs/CLOUD_SYNC.md §1). With
/// <see cref="KeyProtection.Passphrase"/> only envelopes arrive and nothing here opens them without
/// the user's passphrase or recovery key.
/// </remarks>
public sealed record KeyMaterialResponse(
    KeyProtection Protection,
    string? DataKeyBase64,
    string? WrappedDekPassphrase,
    string? WrappedDekRecovery,
    string? KdfParametersJson);

public sealed record SessionResponse(
    string UserId,
    string Email,
    string AccessToken,
    DateTimeOffset AccessExpiresUtc,
    string RefreshToken,
    // Keys is present on sign-in and absent on refresh: a refresh renews a session, not key custody.
    KeyMaterialResponse? Keys,
    // Sent on every session response, so a trial running out is noticed without a second request.
    Entitlement Entitlement,
    DateTimeOffset ServerUtc);

public sealed record AccountSummary(
    string UserId,
    string Email,
    IReadOnlyList<DeviceSummary> Devices);

public sealed record DeviceSummary(string Name, DateTimeOffset SignedInUtc, DateTimeOffset ExpiresUtc);

/// <summary>
/// The account endpoints. Separate from <see cref="ISyncApiClient"/> because they are used at
/// different moments by different code: sign-in is a user action, sync is a background loop.
/// </summary>
public interface IAuthApiClient
{
    /// <summary>
    /// Redeems a Google authorization code. Creates the account on first use — with an identity
    /// provider there is nothing a separate registration step could ask for.
    /// </summary>
    ValueTask<SessionResponse> SignInWithGoogleAsync(
        GoogleSignInRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<SessionResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Best-effort: a network failure here must not block a local sign-out.</summary>
    ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    ValueTask<AccountSummary> GetAccountAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-fetches the key material for a device that is still signed in but lost its local copy — a
    /// restored Windows profile, a cleared credential store. For a locked account that is the
    /// envelopes, which still need the passphrase.
    /// </summary>
    ValueTask<KeyMaterialResponse> GetKeyMaterialAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns the lock on. The server stores the two envelopes and destroys its own copy of the key
    /// in the same write.
    /// </summary>
    ValueTask ProtectAsync(
        string accessToken,
        string wrappedDekPassphrase,
        string wrappedDekRecovery,
        string kdfParametersJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns the lock off, handing key custody back to the server. Requires the raw key, because the
    /// server cannot recover it on its own — which is the point of the lock.
    /// </summary>
    ValueTask UnprotectAsync(
        string accessToken,
        KeyMaterial dataKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The billing state, plus the hosted pages for starting and managing a subscription. Read when
    /// the settings panel opens; the state also rides along on every session response.
    /// </summary>
    ValueTask<(Entitlement Entitlement, BillingLinks Links)> GetBillingAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a checkout for this account and returns the URL to open. Called at the moment the
    /// user asks for it: the transaction carries the account id, which is what ties the resulting
    /// subscription back to Daynote.
    /// </summary>
    ValueTask<string> CreateCheckoutSessionAsync(
        string accessToken,
        BillingPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints a link to the provider's customer portal, where a subscription is cancelled, a card is
    /// changed, and invoices are found. Called at the moment the user asks for it, because the link
    /// is single-use and expires.
    /// </summary>
    ValueTask<string> CreatePortalSessionAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
