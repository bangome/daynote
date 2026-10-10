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
    private readonly IAppleIdentityProvider? apple;
    private readonly IProfileHost? profiles;
    private readonly ISyncTokenProvider? tokens;

    /// <param name="profiles">
    /// The per-account stores (docs/PROFILES.md). Null composes a single-root service whose data root
    /// is taken to be the signed-in account's own folder, which is what the pre-profile layout and
    /// the tests that exercise one root are.
    /// </param>
    /// <param name="tokens">
    /// The sync transport's token provider, so a billing call renews an expired access token through
    /// the same gate as sync rather than racing it. Null sends the stored access token as it is.
    /// </param>
    public AccountService(
        IAuthApiClient auth,
        IIdentityProvider identity,
        ISyncCrypto crypto,
        ISyncSessionStore sessions,
        ISyncStore store,
        Func<string>? deviceName = null,
        IAppleIdentityProvider? apple = null,
        IProfileHost? profiles = null,
        ISyncTokenProvider? tokens = null)
    {
        this.apple = apple;
        this.profiles = profiles;
        this.tokens = tokens;
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        this.crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.deviceName = deviceName ?? (static () => Environment.MachineName);
    }

    /// <summary>
    /// Runs the browser sign-in and redeems the code. The session is saved here when this is the
    /// account's own profile; otherwise the result carries a hand-off for the caller to complete
    /// (docs/PROFILES.md §5.2). Throws <see cref="AccountException"/> for every user-facing outcome,
    /// including the user simply closing the browser.
    /// </summary>
    public async ValueTask<SignInResult> SignInAsync(CancellationToken cancellationToken = default)
    {
        IdentityGrant grant = await identity.AuthorizeAsync(cancellationToken).ConfigureAwait(false);

        SessionResponse session = await auth.SignInWithGoogleAsync(
            new GoogleSignInRequest(
                grant.AuthorizationCode,
                grant.CodeVerifier,
                grant.RedirectUri,
                deviceName(),
                grant.Client),
            cancellationToken).ConfigureAwait(false);

        return await AdoptSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True on a platform that offers Sign in with Apple.</summary>
    public bool CanSignInWithApple => apple is not null;

    /// <summary>
    /// Sign in with Apple, then the same key custody and hand-off as a Google sign-in. The nonce is
    /// minted here, per attempt, so a code intercepted from one attempt cannot be replayed with a
    /// token from another.
    /// </summary>
    public async ValueTask<SignInResult> SignInWithAppleAsync(CancellationToken cancellationToken = default)
    {
        if (apple is null)
        {
            throw new InvalidOperationException("Sign in with Apple is not available on this platform.");
        }

        string nonce = System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        string hashed = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nonce)));
        AppleIdentityGrant grant = await apple.AuthorizeAsync(hashed, cancellationToken).ConfigureAwait(false);

        SessionResponse session = await auth.SignInWithAppleAsync(
            new AppleSignInRequest(grant.AuthorizationCode, nonce, deviceName()),
            cancellationToken).ConfigureAwait(false);

        return await AdoptSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the account on the server, then signs this device out. The notes on this device are
    /// left exactly where they are: deleting a cloud account is not a request to lose local work.
    /// With profiles, what then happens to them — kept as local notes, or removed — is the user's
    /// choice, made afterwards through <see cref="FinishDeletionAsync"/> (docs/PROFILES.md §5.5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The session is refreshed first rather than trusting the stored access token, which may have
    /// expired while the app sat idle. The refresh rotates the refresh token, so the rotated pair is
    /// saved before the delete is sent: if the delete is then refused — a subscription still running,
    /// the network gone — the device keeps a valid session, where discarding the pair would leave it
    /// holding a spent token that the server treats as stolen on the next use.
    /// </para>
    /// <para>
    /// A refresh the server rejects means the session is already gone: most likely a delete that
    /// succeeded while its response was lost. There is nothing left to delete from here, so the
    /// device signs out and says so rather than leaving the user stuck signed in to nothing.
    /// </para>
    /// </remarks>
    public async ValueTask<AccountDeletion> DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new AccountException(AccountFailure.InvalidCredentials, "Not signed in.");

        using (credentials)
        {
            SessionResponse renewed;
            try
            {
                renewed = await auth.RefreshAsync(credentials.RefreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (AccountException failure) when (failure.Failure == AccountFailure.InvalidCredentials)
            {
                await ForgetLocallyAsync(cancellationToken).ConfigureAwait(false);
                return AccountDeletion.SessionAlreadyGone;
            }

            await sessions.UpdateTokensAsync(
                renewed.AccessToken, renewed.AccessExpiresUtc, renewed.RefreshToken, cancellationToken)
                .ConfigureAwait(false);
            await auth.DeleteAccountAsync(renewed.AccessToken, cancellationToken).ConfigureAwait(false);
        }

        // No server logout: the tokens died with the account.
        await ForgetLocallyAsync(cancellationToken).ConfigureAwait(false);
        return AccountDeletion.Deleted;
    }

    /// <summary>
    /// Ends a session the server has rejected (<see cref="AccountFailure.SessionExpired"/>): the
    /// stored tokens and cached key go, and nothing else. There is no server logout — the refresh
    /// token is already dead, and asking would only be another 401.
    /// </summary>
    /// <remarks>
    /// In an account profile the device stays on that account's folder, signed out: its notes stay on
    /// screen, its outbox and cursor are kept, and signing in again as the same account carries on
    /// from there (the database keeps its owner, so <see cref="ResumeAsync"/> sees a folder with an
    /// owner and no session, which is how the next start knows to say the session ended). Without
    /// profiles it is the ordinary local sign-out.
    /// </remarks>
    public async ValueTask EndRejectedSessionAsync(CancellationToken cancellationToken = default)
    {
        await sessions.ClearAsync(cancellationToken).ConfigureAwait(false);
        if (!IsAccountProfile)
        {
            await store.SignOutAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ForgetLocallyAsync(CancellationToken cancellationToken)
    {
        await sessions.ClearAsync(cancellationToken).ConfigureAwait(false);
        await store.SignOutAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SignInResult> AdoptSessionAsync(SessionResponse session, CancellationToken cancellationToken)
    {
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

        if (profiles is not null && !string.Equals(session.UserId, profiles.CurrentProfileId, StringComparison.Ordinal))
        {
            // Another account's session — or any session, from the local profile. It goes to that
            // account's own folder, never into this database, and nothing here is enrolled: whether
            // the local notes come along is the user's question to answer (docs/PROFILES.md §5.2).
            return await BeginHandOffAsync(credentials, cancellationToken).ConfigureAwait(false);
        }

        if (profiles is not null)
        {
            await RefuseAnotherOwnerAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        }

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
        // trigger-fed. Without this, months of local notes would simply never reach the cloud. Only
        // ever this account's own content: this is its folder (or, without profiles, its only root).
        await store.EnrollExistingContentAsync(cancellationToken).ConfigureAwait(false);
        return new SignInResult(session.Email);
    }

    /// <summary>
    /// Signs out. Revoking the refresh token server-side is best effort: a network failure must not
    /// leave the user stuck signed in on their own machine.
    /// </summary>
    /// <param name="removeFromDevice">
    /// In an account profile, also removes that account's folder from this device (docs/PROFILES.md
    /// §5.4, <i>Remove</i>); otherwise it is kept, so signing back in finds its notes and cursor.
    /// </param>
    /// <returns>True when the app has to switch to the local profile, which the caller asks the host for.</returns>
    public async ValueTask<bool> SignOutAsync(
        bool removeFromDevice = false,
        CancellationToken cancellationToken = default)
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

        if (!IsAccountProfile)
        {
            await store.SignOutAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        // The account's database keeps its owner, cursor and outbox: it is that account's folder, and
        // it never syncs without the credentials just cleared. Signing back in carries on from here.
        await profiles!.ReturnToLocalAsync(
            profiles.CurrentProfileId, removeFromDevice, keepNotesAsLocal: false, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Resolves what this device can currently do, without touching the network.
    /// </summary>
    /// <remarks>
    /// A stored session whose data key is missing is reported as <see cref="ResumeState.KeyMissing"/>
    /// rather than as signed out: the tokens are still good, so the key can be re-fetched with
    /// <see cref="RestoreDataKeyAsync"/> instead of sending the user back through the browser.
    /// <para>
    /// With profiles, this is also where the owner check runs, before every sync run (docs/PROFILES.md
    /// §4), and where an account profile's first start marks its database signed in.
    /// </para>
    /// </remarks>
    public async ValueTask<ResumedSession> ResumeAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            return ResumedSession.SignedOut;
        }

        if (profiles is not null && !await IsOwnedByAsync(credentials, cancellationToken).ConfigureAwait(false))
        {
            credentials.Dispose();
            return new ResumedSession(ResumeState.SignInRequired, null, credentials.Email);
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
        if (!await HoldsSessionAsync(cancellationToken).ConfigureAwait(false))
        {
            return (Entitlement.Unknown, BillingLinks.None);
        }

        return await WithAccessTokenAsync(auth.GetBillingAsync, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> HoldsSessionAsync(CancellationToken cancellationToken)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        credentials?.Dispose();
        return credentials is not null;
    }

    /// <summary>
    /// Creates a checkout for this account. Not cached: the transaction carries this account's id,
    /// so a reused URL would bill the wrong person.
    /// </summary>
    public ValueTask<string> CreateCheckoutSessionAsync(
        BillingTier tier,
        BillingPlan plan,
        CancellationToken cancellationToken = default) =>
        WithAccessTokenAsync(
            (token, ct) => auth.CreateCheckoutSessionAsync(token, tier, plan, ct),
            cancellationToken);

    /// <summary>
    /// Moves the running subscription to another tier or interval, and returns the state after it.
    /// </summary>
    public ValueTask<(Entitlement Entitlement, BillingLinks Links)> ChangePlanAsync(
        BillingTier tier,
        BillingPlan plan,
        CancellationToken cancellationToken = default) =>
        WithAccessTokenAsync(
            (token, ct) => auth.ChangePlanAsync(token, tier, plan, ct),
            cancellationToken);

    /// <summary>Records an App Store purchase with the server and returns the billing state after it.</summary>
    public ValueTask<(Entitlement Entitlement, BillingLinks Links)> SubmitAppStoreTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken = default) =>
        WithAccessTokenAsync(
            (token, ct) => auth.SubmitAppStoreTransactionAsync(token, transactionId, ct),
            cancellationToken);

    /// <summary>
    /// The signed-in account's id, or null when signed out. The iPhone hands it to StoreKit as the
    /// purchase's <c>appAccountToken</c>, which is how the server knows whose purchase it is.
    /// </summary>
    public async ValueTask<string?> SignedInUserIdAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            return null;
        }

        using (credentials)
        {
            return credentials.UserId;
        }
    }

    /// <summary>
    /// Mints a link to the provider's customer portal. Not cached: the link is single-use and
    /// expires, so it is created when the user clicks and used immediately.
    /// </summary>
    public ValueTask<string> CreatePortalSessionAsync(CancellationToken cancellationToken = default) =>
        WithAccessTokenAsync(auth.CreatePortalSessionAsync, cancellationToken);

    /// <summary>
    /// Makes an authorized call. With a token provider, an expired access token is renewed first and a
    /// 401 is answered by one refresh and one retry; a refresh the server rejects ends in
    /// <see cref="AccountFailure.SessionExpired"/>, and one it could not be asked ends in
    /// <see cref="AccountFailure.Offline"/>, which keeps the session.
    /// </summary>
    private async ValueTask<T> WithAccessTokenAsync<T>(
        Func<string, CancellationToken, ValueTask<T>> call,
        CancellationToken cancellationToken)
    {
        if (tokens is null)
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

        try
        {
            string token = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await call(token, cancellationToken).ConfigureAwait(false);
            }
            catch (AccountException failure) when (failure.Failure == AccountFailure.InvalidCredentials)
            {
                // Retried once only: a second 401 straight after a good refresh is the server's
                // answer, and another round would turn it into a refresh loop.
                if (!await tokens.TryRefreshAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new AccountException(AccountFailure.SessionExpired, "The session expired.");
                }

                token = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                return await call(token, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SyncTransportException transport)
        {
            throw new AccountException(AccountFailure.Offline, transport.Message);
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
