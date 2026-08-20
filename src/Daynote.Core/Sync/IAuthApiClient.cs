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
    IReadOnlyList<DeviceSummary> Devices);

public sealed record DeviceSummary(string Name, DateTimeOffset SignedInUtc, DateTimeOffset ExpiresUtc);

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
}
