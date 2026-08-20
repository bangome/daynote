using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Sync;

/// <summary>
/// The account endpoints over HTTP. Translates status codes into the
/// <see cref="AccountFailure"/> values the UI can actually say something useful about.
/// </summary>
public sealed class HttpAuthApiClient : IAuthApiClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient http;

    public HttpAuthApiClient(HttpClient http)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async ValueTask<string> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject
        {
            ["email"] = request.Email,
            ["auth_key"] = request.AuthKey,
            ["wrapped_dek_pw"] = request.WrappedDekPassword,
            ["kdf_params"] = JsonNode.Parse(request.KdfParametersJson),
        };
        if (request.WrappedDekRecovery is not null)
        {
            body["wrapped_dek_rk"] = request.WrappedDekRecovery;
        }

        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/auth/register")
            {
                Content = JsonContent.Create(body, options: Json),
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        RegisterBody? parsed = await response.Content
            .ReadFromJsonAsync<RegisterBody>(Json, cancellationToken)
            .ConfigureAwait(false);
        return parsed?.UserId
            ?? throw new AccountException(AccountFailure.ServerError, "Registration returned no account id.");
    }

    public async ValueTask<SessionResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/auth/login")
            {
                Content = JsonContent.Create(
                    new
                    {
                        email = request.Email,
                        auth_key = request.AuthKey,
                        device_name = request.DeviceName,
                    },
                    options: Json),
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return ToSession(await ReadSessionAsync(response, cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask<SessionResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/auth/refresh")
            {
                Content = JsonContent.Create(new { refresh_token = refreshToken }, options: Json),
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return ToSession(await ReadSessionAsync(response, cancellationToken).ConfigureAwait(false));
    }

    public async ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/auth/logout")
            {
                Content = JsonContent.Create(new { refresh_token = refreshToken }, options: Json),
            },
            cancellationToken).ConfigureAwait(false);

        // The endpoint answers 204 whether or not the token existed, so anything else is a real fault.
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AccountSummary> GetAccountAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using HttpResponseMessage response = await SendAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Get, "v1/auth/me");
                message.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                return message;
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        MeBody? body = await response.Content
            .ReadFromJsonAsync<MeBody>(Json, cancellationToken)
            .ConfigureAwait(false);
        if (body is null)
        {
            throw new AccountException(AccountFailure.ServerError, "The account endpoint returned no body.");
        }

        var devices = new List<DeviceSummary>();
        foreach (DeviceBody device in body.Devices ?? [])
        {
            devices.Add(new DeviceSummary(
                device.DeviceName,
                RequireTimestamp(device.IssuedUtc),
                RequireTimestamp(device.ExpiresUtc)));
        }

        return new AccountSummary(
            body.UserId,
            body.Email,
            body.RecoveryKeySet,
            body.RewrapPending,
            body.DekGeneration,
            devices);
    }

    private async ValueTask<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> factory,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = factory();
        try
        {
            return await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new AccountException(
                AccountFailure.Offline,
                "Daynote could not reach the sync service. Check your connection and try again.");
        }
    }

    private static async ValueTask EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // The server deliberately gives the same answer for a wrong password and an unknown email, so
        // the message here must not try to be more specific than that.
        AccountFailure failure = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => AccountFailure.InvalidCredentials,
            HttpStatusCode.Conflict => AccountFailure.EmailAlreadyRegistered,
            HttpStatusCode.TooManyRequests => AccountFailure.ServerError,
            _ => AccountFailure.ServerError,
        };

        string message = failure switch
        {
            AccountFailure.InvalidCredentials => "That email address or password is incorrect.",
            AccountFailure.EmailAlreadyRegistered => "That email address is already registered.",
            _ when response.StatusCode == HttpStatusCode.TooManyRequests =>
                "Too many attempts. Wait a few minutes and try again.",
            _ => $"The sync service returned an error ({(int)response.StatusCode}).",
        };

        _ = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new AccountException(failure, message);
    }

    private static async ValueTask<SessionBody> ReadSessionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        SessionBody? body = await response.Content
            .ReadFromJsonAsync<SessionBody>(Json, cancellationToken)
            .ConfigureAwait(false);
        return body ?? throw new AccountException(AccountFailure.ServerError, "The session response was empty.");
    }

    private static SessionResponse ToSession(SessionBody body) => new(
        body.UserId,
        body.AccessToken,
        DateTimeOffset.FromUnixTimeSeconds(body.AccessExpiresEpoch),
        body.RefreshToken,
        body.WrappedDekPw,
        body.KdfParams?.ToJsonString()
            ?? throw new AccountException(AccountFailure.ServerError, "The session response had no KDF parameters."),
        body.DekGeneration,
        body.RewrapPending,
        RequireTimestamp(body.ServerUtc));

    private static DateTimeOffset RequireTimestamp(string? value)
    {
        var parsed = SyncTimestamps.ParseWire(value);
        return parsed.IsSuccess
            ? parsed.Value
            : throw new AccountException(
                AccountFailure.ServerError,
                $"The sync service sent an unreadable timestamp: '{value}'.");
    }

    private sealed record RegisterBody(string UserId, bool RecoveryKeySet);

    private sealed record SessionBody(
        string UserId,
        string AccessToken,
        long AccessExpiresEpoch,
        string RefreshToken,
        string WrappedDekPw,
        JsonNode? KdfParams,
        int DekGeneration,
        bool RewrapPending,
        string? ServerUtc);

    private sealed record MeBody(
        string UserId,
        string Email,
        IReadOnlyList<DeviceBody>? Devices,
        bool RecoveryKeySet,
        bool RewrapPending,
        int DekGeneration);

    private sealed record DeviceBody(string DeviceName, string IssuedUtc, string ExpiresUtc);
}

/// <summary>
/// Hands the sync transport a live access token, refreshing it when it has expired or been rejected.
/// </summary>
/// <remarks>
/// A failed refresh clears the tokens but deliberately leaves the cached data key alone: that key is
/// what makes an ordinary password reset lossless on the user's own PC (docs/CLOUD_SYNC.md §4.8b).
/// Only an explicit sign-out discards it.
/// </remarks>
public sealed class SyncTokenProvider : ISyncTokenProvider
{
    /// <summary>Refresh a little early so a token does not expire mid-request.</summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    private readonly IAuthApiClient auth;
    private readonly ISyncSessionStore sessions;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SyncTokenProvider(
        IAuthApiClient auth,
        ISyncSessionStore sessions,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.utcNow = utcNow ?? (static () => DateTimeOffset.UtcNow);
    }

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null)
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "Not signed in.");
        }

        using (credentials)
        {
            if (credentials.AccessExpiresUtc - utcNow() > RenewBefore)
            {
                return credentials.AccessToken;
            }
        }

        if (!await TryRefreshAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "The session expired.");
        }

        SyncCredentials? renewed = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (renewed is null)
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "Not signed in.");
        }

        using (renewed)
        {
            return renewed.AccessToken;
        }
    }

    public async ValueTask<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        // One refresh at a time: concurrent attempts would rotate the token twice, and the second
        // rotation of an already-rotated token is treated as theft and revokes the whole family.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SyncCredentials? credentials = await sessions.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (credentials is null)
            {
                return false;
            }

            string refreshToken;
            using (credentials)
            {
                refreshToken = credentials.RefreshToken;
            }

            try
            {
                SessionResponse renewed = await auth
                    .RefreshAsync(refreshToken, cancellationToken)
                    .ConfigureAwait(false);
                await sessions.UpdateTokensAsync(
                    renewed.AccessToken,
                    renewed.AccessExpiresUtc,
                    renewed.RefreshToken,
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (AccountException failure) when (failure.Failure == AccountFailure.Offline)
            {
                // Offline is not a revoked session. Reporting failure here would push the UI into
                // "sign in again" every time the network hiccups.
                return false;
            }
            catch (AccountException)
            {
                return false;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
