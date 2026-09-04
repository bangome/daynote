namespace Daynote.Core.Sync;

/// <summary>
/// The opt-in lock: end-to-end encryption for the accounts that want it (docs/CLOUD_SYNC.md §4.1b).
/// </summary>
/// <remarks>
/// Default accounts never touch this file. Turning the lock on wraps the data key under a passphrase
/// and a one-time recovery key and asks the server to destroy its own copy; from then on this device
/// derives the wrapping key itself and the service cannot read note content.
/// <para>
/// The envelopes are deliberately **not** cached locally. Once unlocked, the data key is in the
/// credentials store and everything works offline; a device that has never been unlocked has to
/// reach the server anyway, so caching envelopes would only add a second copy to keep in step.
/// </para>
/// </remarks>
public sealed partial class AccountService
{
    /// <summary>
    /// A floor, not a policy. The KDF is what actually protects the passphrase, and a length rule
    /// that pushes people toward "Passw0rd!" would make things worse, so this only rejects the
    /// obviously unusable.
    /// </summary>
    public const int MinimumPassphraseLength = 10;

    /// <summary>
    /// The one key-derivation profile this protocol version uses. The account's own parameters are
    /// stored and echoed back, so a future v2 can be introduced and detected; a build meeting
    /// parameters it does not recognise reports <see cref="AccountFailure.UnsupportedKdfProfile"/>
    /// rather than deriving a key that would silently fail to unwrap.
    /// </summary>
    private static KdfParameters Profile => KdfParameters.Argon2idDefault;

    /// <summary>
    /// Turns the lock on and returns the recovery key, which the caller must show once and never
    /// store: with the server's copy destroyed, it is the only way back in if the passphrase is
    /// forgotten.
    /// </summary>
    public async ValueTask<RecoveryKey> EnableLockAsync(
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);

        SyncCredentials credentials = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        using (credentials)
        {
            if (credentials.Protection == KeyProtection.Passphrase)
            {
                throw new AccountException(AccountFailure.ServerError, "The lock is already on.");
            }

            if (credentials.DataKey is not { } dataKey)
            {
                // Wrapping needs the key itself, and only an unlocked device has it.
                throw new AccountException(
                    AccountFailure.LockedOut,
                    "This device cannot turn the lock on until it has the key.");
            }

            RecoveryKey recoveryKey = RecoveryKey.Generate();
            using SyncKeySet keys = crypto.DeriveKeys(passphrase, credentials.Email, Profile);
            using KeyMaterial recoveryKek = crypto.DeriveRecoveryKek(recoveryKey);

            await auth.ProtectAsync(
                credentials.AccessToken,
                crypto.WrapDataKey(
                    dataKey,
                    keys.Kek,
                    CipherScope.DataKey(DataKeyPurpose.Passphrase, credentials.Email)),
                crypto.WrapDataKey(
                    dataKey,
                    recoveryKek,
                    CipherScope.DataKey(DataKeyPurpose.Recovery, credentials.Email)),
                Profile.ToJson(),
                cancellationToken).ConfigureAwait(false);

            // The key stays cached here: this device just proved it has it, and making the user
            // re-enter the passphrase they typed one second ago would be theatre.
            await sessions.SaveAsync(
                credentials with { Protection = KeyProtection.Passphrase },
                cancellationToken).ConfigureAwait(false);

            return recoveryKey;
        }
    }

    /// <summary>
    /// Turns the lock off, handing key custody back to the server. Only an unlocked device can do
    /// it, because the server cannot recover the key on its own.
    /// </summary>
    public async ValueTask DisableLockAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials credentials = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        using (credentials)
        {
            if (credentials.Protection != KeyProtection.Passphrase)
            {
                throw new AccountException(AccountFailure.ServerError, "The lock is not on.");
            }

            if (credentials.DataKey is not { } dataKey)
            {
                throw new AccountException(
                    AccountFailure.LockedOut,
                    "Unlock this device before turning the lock off.");
            }

            await auth.UnprotectAsync(credentials.AccessToken, dataKey, cancellationToken)
                .ConfigureAwait(false);
            await sessions.SaveAsync(
                credentials with { Protection = KeyProtection.Server },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Opens the stored envelope with the passphrase and caches the key on this device.</summary>
    public ValueTask UnlockAsync(string passphrase, CancellationToken cancellationToken = default) =>
        UnlockCoreAsync(passphrase, recoveryKey: null, cancellationToken);

    /// <summary>
    /// The way back in when the passphrase is gone. Opens the recovery envelope instead, then caches
    /// the key exactly as an ordinary unlock would.
    /// </summary>
    public ValueTask UnlockWithRecoveryKeyAsync(
        RecoveryKey recoveryKey,
        CancellationToken cancellationToken = default) =>
        UnlockCoreAsync(passphrase: null, recoveryKey, cancellationToken);

    private async ValueTask UnlockCoreAsync(
        string? passphrase,
        RecoveryKey? recoveryKey,
        CancellationToken cancellationToken)
    {
        SyncCredentials credentials = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        using (credentials)
        {
            KeyMaterialResponse material = await auth
                .GetKeyMaterialAsync(credentials.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (material.Protection != KeyProtection.Passphrase)
            {
                // The lock was turned off from another device while this one was still locked.
                await AdoptServerKeyAsync(credentials, material, cancellationToken).ConfigureAwait(false);
                return;
            }

            var profile = KdfParameters.Parse(material.KdfParametersJson);
            if (!profile.IsSuccess || profile.Value != Profile)
            {
                throw new AccountException(
                    AccountFailure.UnsupportedKdfProfile,
                    "This account was locked by a newer version of Daynote. Update the app to unlock it.");
            }

            (string? envelope, KeyMaterial wrappingKey, CipherScope scope, AccountFailure failure) =
                recoveryKey is { } key
                    ? (material.WrappedDekRecovery,
                       crypto.DeriveRecoveryKek(key),
                       CipherScope.DataKey(DataKeyPurpose.Recovery, credentials.Email),
                       AccountFailure.InvalidRecoveryKey)
                    : (material.WrappedDekPassphrase,
                       crypto.DeriveKeys(passphrase!, credentials.Email, Profile).Kek,
                       CipherScope.DataKey(DataKeyPurpose.Passphrase, credentials.Email),
                       AccountFailure.InvalidPassphrase);

            using (wrappingKey)
            {
                if (envelope is null)
                {
                    throw new AccountException(
                        AccountFailure.LockedOut,
                        "The service holds no envelope for this account.");
                }

                Domain.DomainResult<KeyMaterial> opened =
                    crypto.UnwrapDataKey(envelope, wrappingKey, scope);
                if (!opened.IsSuccess)
                {
                    // A wrong passphrase and a tampered envelope are indistinguishable here, and the
                    // overwhelmingly likely one is a typo, so it is reported as one.
                    throw new AccountException(failure, "That did not open this account.");
                }

                await sessions.SaveAsync(
                    credentials with { DataKey = opened.Value, Protection = KeyProtection.Passphrase },
                    cancellationToken).ConfigureAwait(false);
            }

            await store.SetLockedAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidatePassphrase(string? passphrase)
    {
        if (passphrase is null || passphrase.Length < MinimumPassphraseLength)
        {
            throw new AccountException(
                AccountFailure.WeakPassphrase,
                $"Use at least {MinimumPassphraseLength} characters.");
        }
    }

    private async ValueTask<SyncCredentials> RequireSessionAsync(CancellationToken cancellationToken)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        return credentials
            ?? throw new AccountException(AccountFailure.InvalidCredentials, "Not signed in.");
    }
}
