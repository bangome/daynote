using Daynote.Core.Domain;

namespace Daynote.Core.Sync;

/// <summary>
/// Everything this PC keeps about the signed-in account between launches.
/// </summary>
/// <remarks>
/// <see cref="DataKey"/> is cached deliberately: reading a note must not wait on the network. It is
/// cleared only on explicit sign-out — never on a 401, and never as a reaction to a failed refresh.
/// <para>
/// It is null only when the stored blob predates the key or was written by a restore that lost it.
/// The session still persists in that state, because the tokens are enough to fetch the key again
/// (<see cref="AccountService.RestoreDataKeyAsync"/>) without another trip through the browser.
/// </para>
/// </remarks>
public sealed record SyncCredentials(
    string UserId,
    string Email,
    string AccessToken,
    DateTimeOffset AccessExpiresUtc,
    string RefreshToken,
    int DekGeneration,
    KeyMaterial? DataKey,
    // Persisted so that a null key can be told apart from a locked account without asking the
    // server: one is re-fetched silently, the other has to prompt for a passphrase.
    KeyProtection Protection = KeyProtection.Server) : IDisposable
{
    /// <summary>False when the session is present but its data key is not.</summary>
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

public enum ResumeState
{
    SignedOut,

    /// <summary>Signed in, but this device has no copy of the data key. Re-fetchable.</summary>
    KeyMissing,

    /// <summary>
    /// Signed in to an account with the lock on, and this device has not been unlocked yet. Nothing
    /// here can open the content until the passphrase or the recovery key is supplied.
    /// </summary>
    Locked,

    Ready,
}

/// <summary>
/// What a device can do right now. <see cref="Session"/> is non-null only for
/// <see cref="ResumeState.Ready"/>, and the caller owns disposing its key. <see cref="Email"/> is
/// the signed-in address, carried here so the settings panel can name the account without a
/// network call.
/// </summary>
public sealed record ResumedSession(ResumeState State, SyncSession? Session, string? Email = null)
{
    public static ResumedSession SignedOut { get; } = new(ResumeState.SignedOut, null);

    public static ResumedSession Locked { get; } = new(ResumeState.Locked, null);
}

public enum AccountFailure
{
    /// <summary>The session is gone or was never established.</summary>
    InvalidCredentials,

    /// <summary>
    /// The browser window was closed, the wait timed out, or the provider reported that the person
    /// declined. An ordinary outcome: it gets a quiet message, not an error banner.
    /// </summary>
    SignInCancelled,

    /// <summary>The Google account has no verified address, so it cannot be used to sign in.</summary>
    UnverifiedIdentity,

    /// <summary>The passphrase did not open the stored envelope.</summary>
    InvalidPassphrase,

    /// <summary>The recovery key did not open the stored envelope.</summary>
    InvalidRecoveryKey,

    /// <summary>
    /// The account is locked and this device holds nothing that opens it. Needs the passphrase, the
    /// recovery key, or a device that is already unlocked.
    /// </summary>
    LockedOut,

    /// <summary>
    /// The account was locked by a build using a key-derivation profile this one does not know.
    /// Updating the app is the fix; guessing would just produce a wrong key.
    /// </summary>
    UnsupportedKdfProfile,

    /// <summary>A passphrase too short to be worth stretching.</summary>
    WeakPassphrase,

    Offline,

    ServerError,
}

public sealed class AccountException(AccountFailure failure, string message) : Exception(message)
{
    public AccountFailure Failure { get; } = failure;

    public DomainError ToError() => new(DomainErrorCode.AccountOperationFailed, Message);
}
