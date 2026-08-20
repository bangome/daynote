namespace Daynote.Core.Sync;

/// <summary>
/// A registration request. Everything key-shaped in here is either useless to the server
/// (<see cref="AuthKey"/>) or sealed against it (the two envelopes).
/// </summary>
public sealed record RegisterRequest(
    string Email,
    string AuthKey,
    string WrappedDekPassword,
    string? WrappedDekRecovery,
    string KdfParametersJson);

public sealed record LoginRequest(string Email, string AuthKey, string DeviceName);

/// <summary>
/// A session, plus the sealed key material this device needs to unlock its own content. The server
/// stores and forwards those envelopes; it cannot open them.
/// </summary>
public sealed record SessionResponse(
    string UserId,
    string AccessToken,
    DateTimeOffset AccessExpiresUtc,
    string RefreshToken,
    string WrappedDekPassword,
    string? WrappedDekRecovery,
    string KdfParametersJson,
    int DekGeneration,
    bool RewrapPending,
    DateTimeOffset ServerUtc);

public sealed record AccountSummary(
    string UserId,
    string Email,
    bool RecoveryKeySet,
    bool RewrapPending,
    int DekGeneration,
    IReadOnlyList<DeviceSummary> Devices,
    /// <summary>The recovery envelope, so an unlock can open it without rotating the session.</summary>
    string? WrappedDekRecovery = null);

public sealed record DeviceSummary(string Name, DateTimeOffset SignedInUtc, DateTimeOffset ExpiresUtc);

/// <summary>
/// What the client must supply to finish a reset. The new auth key and the KDF parameters come from
/// the new password; the code came from the email.
/// </summary>
public sealed record ResetConfirmRequest(
    string Email,
    string Code,
    string NewAuthKey,
    string KdfParametersJson);

/// <summary>
/// The account endpoints. Separate from <see cref="ISyncApiClient"/> because they are used at
/// different moments by different code: sign-in is a user action, sync is a background loop.
/// </summary>
public interface IAuthApiClient
{
    ValueTask<string> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    ValueTask<SessionResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    ValueTask<SessionResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Best-effort: a network failure here must not block a local sign-out.</summary>
    ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    ValueTask<AccountSummary> GetAccountAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for a reset code. Always succeeds, whether or not the address is registered: the server
    /// deliberately will not say, and neither will this.
    /// </summary>
    ValueTask RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the password. Does not unlock the data — the server cannot re-wrap a key it cannot
    /// read, so the account comes back locked until <see cref="RewrapAsync"/> runs.
    /// </summary>
    ValueTask ConfirmPasswordResetAsync(
        ResetConfirmRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Supplies a data key wrapped under the new password, clearing the locked state.</summary>
    ValueTask<int> RewrapAsync(
        string accessToken,
        string wrappedDekPassword,
        int dekGeneration,
        CancellationToken cancellationToken = default);
}
