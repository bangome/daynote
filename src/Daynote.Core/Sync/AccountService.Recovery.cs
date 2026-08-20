namespace Daynote.Core.Sync;

/// <summary>
/// Password reset and the unlock that has to follow it (docs/CLOUD_SYNC.md §4.8).
/// </summary>
/// <remarks>
/// A reset restores account access. It cannot restore data access, because the server has no way to
/// re-wrap a key it cannot read. Afterwards the data key comes from one of exactly three places:
/// <list type="number">
/// <item>the recovery key the user saved at registration,</item>
/// <item>a device that still has the key cached — which is why a failed refresh never clears it,</item>
/// <item>nowhere, in which case the cloud copy is gone and the user is told so explicitly.</item>
/// </list>
/// </remarks>
public sealed partial class AccountService
{
    /// <summary>
    /// Asks for a reset code. Reports success either way: the server will not say whether the
    /// address is registered, and echoing that decision here would undo it.
    /// </summary>
    public async ValueTask RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        string normalized = ValidateEmail(email);
        await auth.RequestPasswordResetAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets a new password and signs in with it. The account comes back locked: the caller must then
    /// call <see cref="UnlockWithRecoveryKeyAsync"/> or <see cref="UnlockWithCachedKeyAsync"/>.
    /// </summary>
    public async ValueTask ConfirmPasswordResetAsync(
        string email,
        string code,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        string normalized = ValidateEmail(email);
        ValidatePassword(newPassword);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new AccountException(AccountFailure.InvalidResetCode, "A reset code is required.");
        }

        // Read the cached key BEFORE the reset revokes this device's session: after the reset the
        // tokens are dead, but the key in credentials.dat is exactly what makes an ordinary
        // forgotten-password reset lossless on the user's own PC.
        KeyMaterial? cached = null;
        SyncCredentials? existing = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            using (existing)
            {
                // Null when the account was already locked, in which case there is nothing cached to
                // unlock with and the recovery key is the only route.
                cached = existing.DataKey is { } key ? KeyMaterial.CopyFrom(key.Span) : null;
            }
        }

        try
        {
            using SyncKeySet keys = crypto.DeriveKeys(newPassword, normalized, Profile);
            await auth.ConfirmPasswordResetAsync(
                new ResetConfirmRequest(normalized, code, keys.AuthKeyForServer(), Profile.ToJson()),
                cancellationToken).ConfigureAwait(false);

            // Sign in with the new password. This lands in the locked state by design: the envelope
            // on the server still opens only with the old key-encryption key.
            try
            {
                await SignInAsync(normalized, newPassword, cancellationToken).ConfigureAwait(false);
            }
            catch (AccountException failure) when (failure.Failure == AccountFailure.RewrapRequired)
            {
                // Expected. The caller unlocks next.
            }

            if (cached is not null)
            {
                // The device could open its own data all along, so unlock without asking for
                // anything. Nothing about a password reset should cost the user their notes when the
                // key never actually left this machine.
                await UnlockWithCachedKeyAsync(cached, newPassword, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            cached?.Dispose();
        }
    }

    /// <summary>
    /// Opens the recovery envelope and re-wraps the data key under the current password, clearing the
    /// locked state. The caller must already be signed in with the new password.
    /// </summary>
    public async ValueTask UnlockWithRecoveryKeyAsync(
        RecoveryKey recoveryKey,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!recoveryKey.IsValid)
        {
            throw new AccountException(AccountFailure.InvalidRecoveryKey, "A recovery key is required.");
        }

        SyncCredentials credentials = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        using (credentials)
        {
            AccountSummary summary = await auth
                .GetAccountAsync(credentials.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            if (!summary.RecoveryKeySet)
            {
                throw new AccountException(
                    AccountFailure.NoWayToUnlock,
                    "This account has no recovery key. Sign in on a device you used before.");
            }

            using KeyMaterial recoveryKek = crypto.DeriveRecoveryKek(recoveryKey);
            var opened = crypto.UnwrapDataKey(
                summary.WrappedDekRecovery
                    ?? throw new AccountException(
                        AccountFailure.NoWayToUnlock,
                        "The recovery envelope is not available for this account."),
                recoveryKek,
                CipherScope.DataKey(DataKeyPurpose.Recovery, credentials.Email));
            if (!opened.IsSuccess)
            {
                throw new AccountException(
                    AccountFailure.InvalidRecoveryKey,
                    "That recovery key does not match this account.");
            }

            using KeyMaterial dataKey = opened.Value;
            await RewrapAsync(dataKey, password, credentials, summary, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Unlocks using the key this device already had. Used automatically after a reset performed on
    /// the same PC, where nothing needs to be typed at all.
    /// </summary>
    public async ValueTask UnlockWithCachedKeyAsync(
        KeyMaterial dataKey,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataKey);

        SyncCredentials credentials = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        using (credentials)
        {
            AccountSummary summary = await auth
                .GetAccountAsync(credentials.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            await RewrapAsync(dataKey, password, credentials, summary, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gives up on the cloud copy: forgets the account locally so the user can start again. The notes
    /// on this PC are untouched, which is the whole reason this is a survivable outcome.
    /// </summary>
    public ValueTask DiscardCloudCopyAsync(CancellationToken cancellationToken = default) =>
        SignOutAsync(cancellationToken);

    private async ValueTask RewrapAsync(
        KeyMaterial dataKey,
        string password,
        SyncCredentials credentials,
        AccountSummary summary,
        CancellationToken cancellationToken)
    {
        using SyncKeySet keys = crypto.DeriveKeys(password, credentials.Email, Profile);
        string wrapped = crypto.WrapDataKey(
            dataKey,
            keys.Kek,
            CipherScope.DataKey(DataKeyPurpose.Password, credentials.Email));

        int generation = await auth
            .RewrapAsync(credentials.AccessToken, wrapped, summary.DekGeneration, cancellationToken)
            .ConfigureAwait(false);

        await sessions.SaveAsync(
            credentials with { DekGeneration = generation, DataKey = KeyMaterial.CopyFrom(dataKey.Span) },
            cancellationToken).ConfigureAwait(false);

        await store.SignInAsync(credentials.UserId, generation, cancellationToken).ConfigureAwait(false);
        await store.SetLockedAsync(false, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<SyncCredentials> RequireSessionAsync(CancellationToken cancellationToken)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        return credentials
            ?? throw new AccountException(
                AccountFailure.InvalidCredentials,
                "Sign in with your new password before unlocking.");
    }

}
