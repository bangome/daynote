namespace Daynote.Core.Sync;

/// <summary>
/// Sign-in and sign-out for cloud sync.
/// </summary>
/// <remarks>
/// Identity comes from Google (<see cref="IIdentityProvider"/>). By default the data key comes from
/// the server too, so cloud sync is encrypted in transit and at rest but is not end-to-end encrypted
/// (docs/CLOUD_SYNC.md §1) — an identity provider proves who you are but hands the client no secret
/// to build a key from. An account can take that key away from the server with the opt-in lock; that
/// half lives in AccountService.Lock.cs.
/// </remarks>
public sealed partial class AccountService
{
    /// <summary>
    /// The data key does not rotate in this design — the server issues one per account and keeps it —
    /// so the store's generation column is pinned. It is kept rather than dropped because the sync
    /// store writes it alongside every row, and rewriting that schema would buy nothing.
    /// </summary>
    private const int DataKeyGeneration = 1;

    private readonly IAuthApiClient auth;
    private readonly ISyncCrypto crypto;
    private readonly IIdentityProvider identity;
    private readonly ISyncSessionStore sessions;
    private readonly ISyncStore store;
    private readonly Func<string> deviceName;

    public AccountService(
        IAuthApiClient auth,
        IIdentityProvider identity,
        ISyncCrypto crypto,
        ISyncSessionStore sessions,
        ISyncStore store,
        Func<string>? deviceName = null)
    {
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        this.crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.deviceName = deviceName ?? (static () => Environment.MachineName);
    }

    /// <summary>
    /// Runs the browser sign-in, redeems the code, and enrols existing local content for its first
    /// push. Returns the signed-in address. Throws <see cref="AccountException"/> for every
    /// user-facing outcome, including the user simply closing the browser.
    /// </summary>
    public async ValueTask<string> SignInAsync(CancellationToken cancellationToken = default)
    {
        IdentityGrant grant = await identity.AuthorizeAsync(cancellationToken).ConfigureAwait(false);

        SessionResponse session = await auth.SignInWithGoogleAsync(
            new GoogleSignInRequest(
                grant.AuthorizationCode,
                grant.CodeVerifier,
                grant.RedirectUri,
                deviceName()),
            cancellationToken).ConfigureAwait(false);

        if (session.Keys is not { } material)
        {
            throw new AccountException(
                AccountFailure.ServerError,
                "Signed in, but the server sent no key material. Try again.");
        }

        var credentials = new SyncCredentials(
            session.UserId,
            session.Email,
            session.AccessToken,
            session.AccessExpiresUtc,
            session.RefreshToken,
            DataKeyGeneration,
            // Null for a locked account: the envelopes need a passphrase this device has not been
            // given yet. The session is saved either way, or the app would have no token left to
            // unlock with.
            material.Protection == KeyProtection.Server ? DecodeDataKey(material) : null,
            material.Protection);

        await sessions.SaveAsync(credentials, cancellationToken).ConfigureAwait(false);
        await store.SignInAsync(session.UserId, DataKeyGeneration, cancellationToken).ConfigureAwait(false);

        if (material.Protection == KeyProtection.Passphrase)
        {
            await store.SetLockedAsync(true, cancellationToken).ConfigureAwait(false);
            throw new AccountException(
                AccountFailure.LockedOut,
                "This account is locked. Enter its passphrase to open your notes on this PC.");
        }

        // Content written before this PC ever signed in has no outbox entry, because the outbox is
        // trigger-fed. Without this, months of local notes would simply never reach the cloud.
        await store.EnrollExistingContentAsync(cancellationToken).ConfigureAwait(false);
        return session.Email;
    }

    /// <summary>
    /// Signs out. Revoking the refresh token server-side is best effort: a network failure must not
    /// leave the user stuck signed in on their own machine.
    /// </summary>
    public async ValueTask SignOutAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials? current = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (current is not null)
        {
            using (current)
            {
                try
                {
                    await auth.LogoutAsync(current.RefreshToken, cancellationToken).ConfigureAwait(false);
                }
                catch (AccountException)
                {
                    // Already invalid, or unreachable. Either way the local sign-out proceeds.
                }
            }
        }

        // This is the one path that discards the cached data key.
        await sessions.ClearAsync(cancellationToken).ConfigureAwait(false);
        await store.SignOutAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves what this device can currently do, without touching the network.
    /// </summary>
    /// <remarks>
    /// A stored session whose data key is missing is reported as <see cref="ResumeState.KeyMissing"/>
    /// rather than as signed out: the tokens are still good, so the key can be re-fetched with
    /// <see cref="RestoreDataKeyAsync"/> instead of sending the user back through the browser.
    /// </remarks>
    public async ValueTask<ResumedSession> ResumeAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            return ResumedSession.SignedOut;
        }

        if (credentials.DataKey is not { } dataKey)
        {
            credentials.Dispose();
            // Two different states that look alike from here and must not be collapsed: a default
            // account simply re-fetches its key, while a locked one has to ask for the passphrase.
            return new ResumedSession(
                credentials.Protection == KeyProtection.Passphrase
                    ? ResumeState.Locked
                    : ResumeState.KeyMissing,
                null,
                credentials.Email);
        }

        return new ResumedSession(
            ResumeState.Ready,
            new SyncSession(credentials.UserId, dataKey),
            credentials.Email);
    }

    /// <summary>
    /// Fetches the data key again for a session that has everything except the key. Cheap and safe
    /// to call whenever <see cref="ResumeAsync"/> reports <see cref="ResumeState.KeyMissing"/>.
    /// </summary>
    /// <remarks>
    /// Reports <see cref="AccountFailure.LockedOut"/> if the account turned out to be locked — the
    /// server has no key to hand back, and the caller has to ask for the passphrase instead.
    /// </remarks>
    public async ValueTask RestoreDataKeyAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "Sign in to restore the key.");
        }

        using (credentials)
        {
            KeyMaterialResponse material = await auth
                .GetKeyMaterialAsync(credentials.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (material.Protection == KeyProtection.Passphrase)
            {
                await sessions.SaveAsync(
                    credentials with { DataKey = null, Protection = KeyProtection.Passphrase },
                    cancellationToken).ConfigureAwait(false);
                await store.SetLockedAsync(true, cancellationToken).ConfigureAwait(false);
                throw new AccountException(
                    AccountFailure.LockedOut,
                    "This account is locked. Enter its passphrase to open your notes on this PC.");
            }

            await AdoptServerKeyAsync(credentials, material, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the billing state for the signed-in account (docs/CLOUD_SYNC.md §14). Separate from
    /// sign-in because the settings panel asks for it whenever it opens, and because a lapse noticed
    /// mid-session has to be re-read without another trip through the browser.
    /// </summary>
    public async ValueTask<(Entitlement Entitlement, BillingLinks Links)> ReadBillingAsync(
        CancellationToken cancellationToken = default)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            return (Entitlement.Unknown, BillingLinks.None);
        }

        using (credentials)
        {
            return await auth.GetBillingAsync(credentials.AccessToken, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates a checkout for this account. Not cached: the transaction carries this account's id,
    /// so a reused URL would bill the wrong person.
    /// </summary>
    public ValueTask<string> CreateCheckoutSessionAsync(
        BillingPlan plan,
        CancellationToken cancellationToken = default) =>
        WithAccessTokenAsync(
            (token, ct) => auth.CreateCheckoutSessionAsync(token, plan, ct),
            cancellationToken);

    /// <summary>
    /// Mints a link to the provider's customer portal. Not cached: the link is single-use and
    /// expires, so it is created when the user clicks and used immediately.
    /// </summary>
    public ValueTask<string> CreatePortalSessionAsync(CancellationToken cancellationToken = default) =>
        WithAccessTokenAsync(auth.CreatePortalSessionAsync, cancellationToken);

    private async ValueTask<string> WithAccessTokenAsync(
        Func<string, CancellationToken, ValueTask<string>> call,
        CancellationToken cancellationToken)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "Not signed in.");
        }

        using (credentials)
        {
            return await call(credentials.AccessToken, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Caches a server-held key and clears any locked state left over from before.</summary>
    private async ValueTask AdoptServerKeyAsync(
        SyncCredentials credentials,
        KeyMaterialResponse material,
        CancellationToken cancellationToken)
    {
        await sessions.SaveAsync(
            credentials with { DataKey = DecodeDataKey(material), Protection = KeyProtection.Server },
            cancellationToken).ConfigureAwait(false);
        await store.SetLockedAsync(false, cancellationToken).ConfigureAwait(false);
    }

    private static KeyMaterial DecodeDataKey(KeyMaterialResponse material)
    {
        if (material.DataKeyBase64 is not { Length: > 0 } encoded)
        {
            throw new AccountException(AccountFailure.ServerError, "The server sent no data key.");
        }

        return DecodeDataKey(encoded);
    }

    private static KeyMaterial DecodeDataKey(string encoded)
    {
        byte[] bytes;
        try
        {
            bytes = System.Buffers.Text.Base64Url.DecodeFromChars(encoded);
        }
        catch (FormatException)
        {
            throw new AccountException(AccountFailure.ServerError, "The server sent an unreadable data key.");
        }

        if (bytes.Length != KeyMaterial.Length)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            throw new AccountException(AccountFailure.ServerError, "The server sent a data key of the wrong size.");
        }

        return KeyMaterial.Adopt(bytes);
    }
}
