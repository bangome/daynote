using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// Everything this PC keeps about the signed-in account between launches.
/// </summary>
/// <remarks>
/// <see cref="DataKey"/> is cached deliberately. It is what makes an ordinary forgotten-password
/// reset lossless for someone still using their own PC (docs/CLOUD_SYNC.md §4.8b), so it is cleared
/// only on explicit sign-out — never on a 401, and never as a reaction to a failed refresh.
/// </remarks>
public sealed record SyncCredentials(
    string UserId,
    string Email,
    string AccessToken,
    DateTimeOffset AccessExpiresUtc,
    string RefreshToken,
    int DekGeneration,
    KeyMaterial DataKey) : IDisposable
{
    public void Dispose() => DataKey.Dispose();
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

    Offline,

    ServerError,
}

public sealed class AccountException(AccountFailure failure, string message) : Exception(message)
{
    public AccountFailure Failure { get; } = failure;

    public DomainError ToError() => new(DomainErrorCode.AccountOperationFailed, Message);
}
