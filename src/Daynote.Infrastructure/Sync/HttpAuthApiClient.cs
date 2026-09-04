using System.Buffers.Text;
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

    public async ValueTask<SessionResponse> SignInWithGoogleAsync(
        GoogleSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "v1/auth/google")
            {
                Content = JsonContent.Create(
                    new
                    {
                        code = request.AuthorizationCode,
                        code_verifier = request.CodeVerifier,
                        redirect_uri = request.RedirectUri,
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
            () => Authorized(HttpMethod.Get, "v1/auth/me", accessToken),
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

        return new AccountSummary(body.UserId, body.Email, devices);
    }

    public async ValueTask<KeyMaterialResponse> GetKeyMaterialAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using HttpResponseMessage response = await SendAsync(
            () => Authorized(HttpMethod.Get, "v1/auth/data-key", accessToken),
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        KeysBody? body = await response.Content
            .ReadFromJsonAsync<KeysBody>(Json, cancellationToken)
            .ConfigureAwait(false);

        return ToKeys(body
            ?? throw new AccountException(AccountFailure.ServerError, "The key response was empty."));
    }

    public async ValueTask ProtectAsync(
        string accessToken,
        string wrappedDekPassphrase,
        string wrappedDekRecovery,
        string kdfParametersJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(wrappedDekPassphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(wrappedDekRecovery);

        using HttpResponseMessage response = await SendAsync(
            () =>
            {
                HttpRequestMessage message = Authorized(HttpMethod.Post, "v1/auth/protect", accessToken);
                message.Content = JsonContent.Create(
                    new JsonObject
                    {
                        ["wrapped_dek_pw"] = wrappedDekPassphrase,
                        ["wrapped_dek_rk"] = wrappedDekRecovery,
                        ["kdf_params"] = JsonNode.Parse(kdfParametersJson),
                    },
                    options: Json);
                return message;
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask UnprotectAsync(
        string accessToken,
        KeyMaterial dataKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(dataKey);

        using HttpResponseMessage response = await SendAsync(
            () =>
            {
                HttpRequestMessage message =
                    Authorized(HttpMethod.Post, "v1/auth/unprotect", accessToken);
                message.Content = JsonContent.Create(
                    new { data_key = Base64Url.EncodeToString(dataKey.Span) },
                    options: Json);
                return message;
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return message;
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

        AccountFailure failure = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => AccountFailure.InvalidCredentials,
            _ => AccountFailure.ServerError,
        };

        string message = response.StatusCode switch
        {
            // 401 on these endpoints means the Google grant was stale or the session is gone. Both
            // are fixed by signing in again, which is what the UI offers.
            HttpStatusCode.Unauthorized => "That sign-in is no longer valid. Sign in again.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Wait a few minutes and try again.",
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
        body.Email,
        body.AccessToken,
        DateTimeOffset.FromUnixTimeSeconds(body.AccessExpiresEpoch),
        body.RefreshToken,
        // Absent on refresh: a refresh renews a session, not key custody.
        body.DataKey is null && body.WrappedDekPw is null ? null : ToKeys(body),
        body.Entitlement is null ? Entitlement.Unknown : ToEntitlement(body.Entitlement),
        RequireTimestamp(body.ServerUtc));

    /// <summary>
    /// Reads whichever custody the account is in. An unknown value is refused rather than defaulted:
    /// treating it as server-held would be the one wrong guess that hands a key request to an
    /// account that is supposed to be locked.
    /// </summary>
    private static KeyMaterialResponse ToKeys(IKeyBody body) => body.Protection switch
    {
        "passphrase" => new KeyMaterialResponse(
            KeyProtection.Passphrase,
            null,
            body.WrappedDekPw,
            body.WrappedDekRk,
            body.KdfParams?.ToJsonString()),
        "server" or null => new KeyMaterialResponse(KeyProtection.Server, body.DataKey, null, null, null),
        _ => throw new AccountException(
            AccountFailure.ServerError,
            "The sync service reported a key protection this version does not understand."),
    };

    private static DateTimeOffset RequireTimestamp(string? value)
    {
        var parsed = SyncTimestamps.ParseWire(value);
        return parsed.IsSuccess
            ? parsed.Value
            : throw new AccountException(
                AccountFailure.ServerError,
                $"The sync service sent an unreadable timestamp: '{value}'.");
    }

    public async ValueTask<(Entitlement Entitlement, BillingLinks Links)> GetBillingAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using HttpResponseMessage response = await SendAsync(
            () => Authorized(HttpMethod.Get, "v1/billing/status", accessToken),
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        BillingBody? body = await response.Content
            .ReadFromJsonAsync<BillingBody>(Json, cancellationToken)
            .ConfigureAwait(false);
        if (body is null)
        {
            throw new AccountException(AccountFailure.ServerError, "The billing endpoint returned no body.");
        }

        // An older Worker that does not list plans sells both; an empty list means neither.
        string[] plans = body.Plans ?? ["monthly", "annual"];
        return (
            ToEntitlement(body),
            new BillingLinks(
                body.CanCheckout,
                body.CanManage,
                OffersMonthly: plans.Contains("monthly", StringComparer.Ordinal),
                OffersAnnual: plans.Contains("annual", StringComparer.Ordinal)));
    }

    public ValueTask<string> CreateCheckoutSessionAsync(
        string accessToken,
        BillingPlan plan,
        CancellationToken cancellationToken = default) =>
        CreateSessionAsync(
            "v1/billing/checkout",
            accessToken,
            cancellationToken,
            payload: new { plan = plan.ToWire() });

    public ValueTask<string> CreatePortalSessionAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        CreateSessionAsync("v1/billing/portal", accessToken, cancellationToken, payload: null);

    /// <summary>
    /// Asks the Worker for a one-shot billing URL. Both billing pages work this way, so the only
    /// difference between them is the path.
    /// </summary>
    private async ValueTask<string> CreateSessionAsync(
        string path,
        string accessToken,
        CancellationToken cancellationToken,
        object? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using HttpResponseMessage response = await SendAsync(
            () =>
            {
                HttpRequestMessage message = Authorized(HttpMethod.Post, path, accessToken);
                if (payload is not null)
                {
                    message.Content = JsonContent.Create(payload, options: Json);
                }

                return message;
            },
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        SessionUrlBody? body = await response.Content
            .ReadFromJsonAsync<SessionUrlBody>(Json, cancellationToken)
            .ConfigureAwait(false);

        return body?.Url is { Length: > 0 } url
            ? url
            : throw new AccountException(AccountFailure.ServerError, "The billing link was empty.");
    }

    /// <summary>
    /// Reads the billing state. An unrecognised state is treated as
    /// <see cref="EntitlementState.Expired"/> rather than guessed at — failing closed on a value
    /// this build does not know is the only safe direction, and `can_sync` from the server still
    /// decides whether syncing is attempted.
    /// </summary>
    private static Entitlement ToEntitlement(IEntitlementBody body)
    {
        EntitlementState state = body.State switch
        {
            "trial" => EntitlementState.Trial,
            "active" => EntitlementState.Active,
            "grace" => EntitlementState.Grace,
            "expired" => EntitlementState.Expired,
            _ => EntitlementState.Expired,
        };

        DateTimeOffset? until = null;
        if (body.Until is { Length: > 0 } value)
        {
            var parsed = SyncTimestamps.ParseWire(value);
            until = parsed.IsSuccess ? parsed.Value : null;
        }

        return new Entitlement(state, until, body.CanSync, body.HasSubscribed);
    }

    /// <summary>The key-custody fields, shared by the session and key-material responses.</summary>
    private interface IKeyBody
    {
        string? Protection { get; }

        string? DataKey { get; }

        string? WrappedDekPw { get; }

        string? WrappedDekRk { get; }

        JsonNode? KdfParams { get; }
    }

    private sealed record SessionBody(
        string UserId,
        string Email,
        string AccessToken,
        long AccessExpiresEpoch,
        string RefreshToken,
        string? Protection,
        string? DataKey,
        string? WrappedDekPw,
        string? WrappedDekRk,
        JsonNode? KdfParams,
        EntitlementBody? Entitlement,
        string? ServerUtc) : IKeyBody;

    /// <summary>The entitlement fields, shared by the session and billing responses.</summary>
    private interface IEntitlementBody
    {
        string? State { get; }

        string? Until { get; }

        bool CanSync { get; }

        bool HasSubscribed { get; }
    }

    private sealed record EntitlementBody(
        string? State,
        string? Until,
        bool CanSync,
        bool HasSubscribed) : IEntitlementBody;

    private sealed record BillingBody(
        string? State,
        string? Until,
        bool CanSync,
        bool HasSubscribed,
        bool CanCheckout,
        bool CanManage,
        string[]? Plans) : IEntitlementBody;

    private sealed record SessionUrlBody(string? Url);

    private sealed record KeysBody(
        string? Protection,
        string? DataKey,
        string? WrappedDekPw,
        string? WrappedDekRk,
        JsonNode? KdfParams) : IKeyBody;

    private sealed record MeBody(string UserId, string Email, IReadOnlyList<DeviceBody>? Devices);

    private sealed record DeviceBody(string DeviceName, string IssuedUtc, string ExpiresUtc);
}

/// <summary>
/// Hands the sync transport a live access token, refreshing it when it has expired or been rejected.
/// </summary>
/// <remarks>
/// A failed refresh deliberately leaves the cached data key alone. Only an explicit sign-out
/// discards it, so a network outage or an expired session never costs the user local access to
/// their own notes.
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
