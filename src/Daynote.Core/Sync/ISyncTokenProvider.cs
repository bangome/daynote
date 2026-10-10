namespace Daynote.Core.Sync;

/// <summary>
/// Supplies the bearer token for a request and refreshes it once on a 401.
/// </summary>
/// <remarks>
/// Token lifecycle lives above the transport (with the account view model), so the transport does not
/// have to know about passwords, refresh rotation, or the locked state. One instance per composition:
/// the sync client and <see cref="AccountService"/> share it, because two refreshes of the same token
/// rotate it twice, and the server reads the second as theft and revokes the whole family.
/// </remarks>
public interface ISyncTokenProvider
{
    /// <summary>
    /// The stored access token, renewed first when it is about to expire. Throws
    /// <see cref="AccountException"/> with <see cref="AccountFailure.SessionExpired"/> when the server
    /// rejected the refresh token, and <see cref="SyncTransportException"/> when it could not be asked.
    /// </summary>
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges the refresh token. Returns false only when there is no session or the server rejected
    /// the refresh token, which means the user must sign in again. Being offline or a server fault is
    /// not that: it throws <see cref="SyncTransportException"/>, and the session is kept for next time.
    /// </summary>
    ValueTask<bool> TryRefreshAsync(CancellationToken cancellationToken = default);
}
