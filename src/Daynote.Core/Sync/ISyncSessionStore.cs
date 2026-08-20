using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// Everything this PC keeps about the signed-in account between launches.
/// </summary>
/// <remarks>
/// <see cref="DataKey"/> is cached deliberately. It is what makes an ordinary forgotten-password
/// reset lossless for someone still using their own PC (docs/CLOUD_SYNC.md §4.8b), so it is cleared
/// only on explicit sign-out — never on a 401, and never as a reaction to a failed refresh.
/// <para>
/// It is null in exactly one state: signed in but locked, after a password reset the device cannot
/// yet undo. The session still has to persist, or closing the app would strand the user with no way
/// back to the unlock screen.
/// </para>
/// </remarks>
public sealed record SyncCredentials(
    string UserId,
    string Email,
    string AccessToken,
    DateTimeOffset AccessExpiresUtc,
    string RefreshToken,
    int DekGeneration,
    KeyMaterial? DataKey) : IDisposable
{
    /// <summary>False while the account is locked: signed in, but nothing here opens the content.</summary>
    public bool CanDecrypt => DataKey is not null;

    public void Dispose() => DataKey?.Dispose();
}

/// <summary>
/// Persists <see cref="SyncCredentials"/> outside the database, because the database is copied
/// verbatim into the plaintext backup zip and must never carry key material.
/// </summary>
public interface ISyncSessionStore
{
    /// <summary>Null when signed out, or when the stored blob cannot be read on this account/machine.</summary>
    ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Replaces just the tokens, leaving the cached data key in place.</summary>
    ValueTask UpdateTokensAsync(
        string accessToken,
        DateTimeOffset accessExpiresUtc,
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>Explicit sign-out: the only thing that discards the cached data key.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record RegisteredAccount(string UserId, RecoveryKey RecoveryKey);

public enum ResumeState
{
    SignedOut,

    /// <summary>Signed in, but no key on this device opens the content yet.</summary>
    Locked,

    Ready,
}

/// <summary>
/// What a device can do right now. <see cref="Session"/> is non-null only for
/// <see cref="ResumeState.Ready"/>, and the caller owns disposing its key.
/// </summary>
public sealed record ResumedSession(ResumeState State, SyncSession? Session)
{
    public static ResumedSession SignedOut { get; } = new(ResumeState.SignedOut, null);

    public static ResumedSession Locked { get; } = new(ResumeState.Locked, null);
}

public enum AccountFailure
{
    /// <summary>Wrong email or password. Indistinguishable by design.</summary>
    InvalidCredentials,

    EmailAlreadyRegistered,

    InvalidEmail,

    WeakPassword,

    /// <summary>
    /// Signed in, but the stored envelope no longer opens with this password — the state after a
    /// password reset. Needs the recovery key or a device that still holds the data key.
    /// </summary>
    RewrapRequired,

    /// <summary>
    /// The account was created by a build using a key-derivation profile this one does not know.
    /// Updating the app is the fix; guessing would just produce a wrong key.
    /// </summary>
    UnsupportedKdfProfile,

    /// <summary>The reset code was wrong, expired, already used, or out of attempts.</summary>
    InvalidResetCode,

    /// <summary>The recovery key did not open the stored envelope.</summary>
    InvalidRecoveryKey,

    /// <summary>
    /// Nothing on this device can open the cloud copy, and no recovery key was supplied. The only
    /// remaining options are another device or discarding the cloud copy.
    /// </summary>
    NoWayToUnlock,

    Offline,

    ServerError,
}

public sealed class AccountException(AccountFailure failure, string message) : Exception(message)
{
    public AccountFailure Failure { get; } = failure;

    public DomainError ToError() => new(DomainErrorCode.AccountOperationFailed, Message);
}
