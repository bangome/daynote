using System.Text.RegularExpressions;

namespace Daynote.Core.Sync;

/// <summary>
/// Registration, sign-in, and sign-out: the only place a password is ever handled.
/// </summary>
/// <remarks>
/// The password reaches this class and stops here. It is turned into an auth key and a
/// key-encryption key (<see cref="ISyncCrypto.DeriveKeys"/>) and neither the password nor the KEK is
/// stored or transmitted. See docs/CLOUD_SYNC.md §4.
/// </remarks>
public sealed partial class AccountService
{
    /// <summary>
    /// The one key-derivation profile this protocol version uses. Login cannot ask the server which
    /// profile an account uses without also answering "does this email exist?", so the profile is
    /// pinned to the protocol version instead. The server still stores the account's parameters, so a
    /// future v2 can be introduced and detected; a build meeting parameters it does not recognise
    /// reports <see cref="AccountFailure.UnsupportedKdfProfile"/> rather than deriving a wrong key.
    /// </summary>
    private static KdfParameters Profile => KdfParameters.Argon2idDefault;

    /// <summary>
    /// A floor, not a policy. The KDF is what actually protects the password, and a length rule that
    /// pushes people toward "Passw0rd!" would make things worse, so this only rejects the obviously
    /// unusable.
    /// </summary>
    public const int MinimumPasswordLength = 10;

    private readonly IAuthApiClient auth;
    private readonly ISyncCrypto crypto;
    private readonly ISyncSessionStore sessions;
    private readonly ISyncStore store;
    private readonly Func<string> deviceName;

    public AccountService(
        IAuthApiClient auth,
        ISyncCrypto crypto,
        ISyncSessionStore sessions,
        ISyncStore store,
        Func<string>? deviceName = null)
    {
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        this.crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.deviceName = deviceName ?? (static () => Environment.MachineName);
    }

    /// <summary>
    /// Creates the account and signs in. Returns the recovery key, which the caller must show once
    /// and never store: it is the only way back in if the password is forgotten
    /// (docs/CLOUD_SYNC.md §4.6).
    /// </summary>
    public async ValueTask<RegisteredAccount> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        string normalized = ValidateEmail(email);
        ValidatePassword(password);

        RecoveryKey recoveryKey = RecoveryKey.Generate();
        using SyncKeySet keys = crypto.DeriveKeys(password, normalized, Profile);
        using KeyMaterial dataKey = crypto.GenerateDataKey();
        using KeyMaterial recoveryKek = crypto.DeriveRecoveryKek(recoveryKey);

        string wrappedByPassword = crypto.WrapDataKey(
            dataKey,
            keys.Kek,
            CipherScope.DataKey(DataKeyPurpose.Password, normalized));
        string wrappedByRecovery = crypto.WrapDataKey(
            dataKey,
            recoveryKek,
            CipherScope.DataKey(DataKeyPurpose.Recovery, normalized));

        string userId = await auth.RegisterAsync(
            new RegisterRequest(
                normalized,
                keys.AuthKeyForServer(),
                wrappedByPassword,
                wrappedByRecovery,
                Profile.ToJson()),
            cancellationToken).ConfigureAwait(false);

        // Sign in through the ordinary path rather than assuming the registration succeeded into a
        // usable state: this proves the envelope we just uploaded actually opens.
        await SignInAsync(normalized, password, cancellationToken).ConfigureAwait(false);
        return new RegisteredAccount(userId, recoveryKey);
    }

    /// <summary>
    /// Derives the keys, authenticates, opens the data key, and enrols existing local content for its
    /// first push. Throws <see cref="AccountException"/> for every user-facing outcome.
    /// </summary>
    public async ValueTask<string> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        string normalized = ValidateEmail(email);
        if (string.IsNullOrEmpty(password))
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "A password is required.");
        }

        using SyncKeySet keys = crypto.DeriveKeys(password, normalized, Profile);
        SessionResponse session = await auth.LoginAsync(
            new LoginRequest(normalized, keys.AuthKeyForServer(), deviceName()),
            cancellationToken).ConfigureAwait(false);

        var accountProfile = KdfParameters.Parse(session.KdfParametersJson);
        if (!accountProfile.IsSuccess || accountProfile.Value != Profile)
        {
            // Deriving with the wrong profile would produce a key that silently fails to unwrap, so
            // say what is actually wrong instead.
            throw new AccountException(
                AccountFailure.UnsupportedKdfProfile,
                "This account was created by a newer version of Daynote. Update the app to sign in.");
        }

        var opened = crypto.UnwrapDataKey(
            session.WrappedDekPassword,
            keys.Kek,
            CipherScope.DataKey(DataKeyPurpose.Password, normalized));
        if (!opened.IsSuccess)
        {
            // The password authenticated, so it is right — but the envelope predates a reset and was
            // never re-wrapped. Content stays unreadable until §4.8 unlock runs.
            await store.SetLockedAsync(true, cancellationToken).ConfigureAwait(false);
            throw new AccountException(
                AccountFailure.RewrapRequired,
                "Your notes are locked. Enter your recovery key, or sign in on a device you used before.");
        }

        KeyMaterial dataKey = opened.Value;
        await sessions.SaveAsync(
            new SyncCredentials(
                session.UserId,
                normalized,
                session.AccessToken,
                session.AccessExpiresUtc,
                session.RefreshToken,
                session.DekGeneration,
                dataKey),
            cancellationToken).ConfigureAwait(false);

        await store.SignInAsync(session.UserId, session.DekGeneration, cancellationToken).ConfigureAwait(false);
        if (session.RewrapPending)
        {
            await store.SetLockedAsync(true, cancellationToken).ConfigureAwait(false);
        }

        // Content written before this PC ever signed in has no outbox entry, because the outbox is
        // trigger-fed. Without this, months of local notes would simply never reach the cloud.
        await store.EnrollExistingContentAsync(cancellationToken).ConfigureAwait(false);
        return session.UserId;
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

    /// <summary>The session to hand <see cref="SyncEngine"/>, or null when signed out.</summary>
    public async ValueTask<SyncSession?> TryResumeAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        return credentials is null ? null : new SyncSession(credentials.UserId, credentials.DataKey);
    }

    private static string ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new AccountException(AccountFailure.InvalidEmail, "An email address is required.");
        }

        string normalized = CipherScope.NormalizeEmail(email);
        if (normalized.Length > 254 || !EmailPattern().IsMatch(normalized))
        {
            throw new AccountException(AccountFailure.InvalidEmail, "That does not look like an email address.");
        }

        return normalized;
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinimumPasswordLength)
        {
            throw new AccountException(
                AccountFailure.WeakPassword,
                $"Use at least {MinimumPasswordLength} characters.");
        }
    }

    /// <summary>
    /// A sanity check, not an RFC 5322 parser, and deliberately the same shape the server applies.
    /// Real validity is established by the verification email.
    /// </summary>
    [GeneratedRegex(@"^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
