using System.Net;
using System.Text;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Sync;

/// <summary>
/// What a failed refresh means to the sync transport. Only a 401 from <c>/v1/auth/refresh</c> says
/// the session is gone; a 5xx or no network is a passing failure and must never read as one, or a
/// server hiccup would sign everybody out.
/// </summary>
[TestClass]
public sealed class SyncTokenProviderTests
{
    private const string Session = """
        {
          "user_id": "u-1", "email": "a@example.test",
          "access_token": "fresh", "access_expires_epoch": 4070908800,
          "refresh_token": "rotated", "server_utc": "2026-10-10T00:00:00.0000000Z"
        }
        """;

    [TestMethod]
    public async Task A_refresh_the_server_rejects_means_sign_in_again()
    {
        (HttpSyncApiClient client, MemorySessions sessions, _) = Compose(_ => Status(HttpStatusCode.Unauthorized));

        var failure = await Assert.ThrowsExactlyAsync<SyncTransportException>(async () => await client.PullAsync(0, 1));

        Assert.IsTrue(failure.RequiresSignIn);
        Assert.IsNotNull(sessions.Stored, "The transport cleared the session; that is the account's call to make.");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    public async Task A_refresh_that_fails_on_the_server_is_not_a_revoked_session(HttpStatusCode status)
    {
        (HttpSyncApiClient client, _, _) = Compose(_ => Status(status));

        var failure = await Assert.ThrowsExactlyAsync<SyncTransportException>(async () => await client.PullAsync(0, 1));

        Assert.IsFalse(failure.RequiresSignIn, $"A {(int)status} on refresh was reported as a revoked session.");
    }

    [TestMethod]
    public async Task A_refresh_with_no_network_is_not_a_revoked_session()
    {
        (HttpSyncApiClient client, _, _) = Compose(_ => throw new HttpRequestException("offline"));

        var failure = await Assert.ThrowsExactlyAsync<SyncTransportException>(async () => await client.PullAsync(0, 1));

        Assert.IsFalse(failure.RequiresSignIn);
    }

    [TestMethod]
    public async Task An_expired_token_whose_refresh_is_rejected_reports_the_session_expired()
    {
        (_, MemorySessions sessions, SyncTokenProvider tokens) = Compose(_ => Status(HttpStatusCode.Unauthorized));
        sessions.Stored = sessions.Stored! with { AccessExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5) };

        var failure = await Assert.ThrowsExactlyAsync<AccountException>(async () => await tokens.GetAccessTokenAsync());

        Assert.AreEqual(AccountFailure.SessionExpired, failure.Failure);
    }

    [TestMethod]
    public async Task A_good_refresh_retries_the_request_once_with_the_new_token()
    {
        (HttpSyncApiClient client, MemorySessions sessions, _) = Compose(_ => Json(Session), syncAnswersAfterRefresh: true);

        PullResult result = await client.PullAsync(0, 1);

        Assert.IsEmpty(result.Changes);
        Assert.AreEqual("rotated", sessions.Stored!.RefreshToken);
    }

    [TestMethod]
    public async Task Two_providers_over_one_session_never_present_the_same_refresh_token()
    {
        // A phone's profile switch: the old composition's provider is still refreshing when the new
        // one starts. Presenting one token twice is what the server reads as theft.
        var presented = new System.Collections.Concurrent.ConcurrentBag<string>();
        int serial = 0;
        var handler = new Handler(request =>
        {
            string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            presented.Add(System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("refresh_token").GetString()!);
            Thread.Sleep(20);
            int next = Interlocked.Increment(ref serial);
            return Json(Session.Replace("\"rotated\"", $"\"rotated-{next}\"", StringComparison.Ordinal));
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://daynote.test/") };
        var sessions = new MemorySessions
        {
            Stored = new SyncCredentials("u-1", "a@example.test", "stale", DateTimeOffset.UtcNow.AddHours(1), "refresh", 1, null),
        };
        var old = new SyncTokenProvider(new HttpAuthApiClient(http), sessions);
        var fresh = new SyncTokenProvider(new HttpAuthApiClient(http), sessions);

        bool[] results = await Task.WhenAll(
            Task.Run(async () => await old.TryRefreshAsync()),
            Task.Run(async () => await fresh.TryRefreshAsync()));

        Assert.IsTrue(results.All(ok => ok));
        Assert.HasCount(2, presented);
        Assert.HasCount(2, presented.Distinct(), "The same refresh token was presented twice.");
    }

    /// <summary>Sync endpoints answer 401 to the stale token; the refresh endpoint answers as told.</summary>
    private static (HttpSyncApiClient, MemorySessions, SyncTokenProvider) Compose(
        Func<HttpRequestMessage, HttpResponseMessage> refresh,
        bool syncAnswersAfterRefresh = false)
    {
        var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/v1/auth/refresh", StringComparison.Ordinal))
            {
                return refresh(request);
            }

            return syncAnswersAfterRefresh && request.Headers.Authorization?.Parameter == "fresh"
                ? Json("""{ "changes": [], "cursor": 0, "has_more": false, "server_utc": "2026-10-10T00:00:00.0000000Z" }""")
                : Status(HttpStatusCode.Unauthorized);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://daynote.test/") };
        var sessions = new MemorySessions
        {
            Stored = new SyncCredentials(
                "u-1", "a@example.test", "stale", DateTimeOffset.UtcNow.AddHours(1), "refresh", 1, null),
        };
        var tokens = new SyncTokenProvider(new HttpAuthApiClient(http), sessions);
        return (new HttpSyncApiClient(http, tokens), sessions, tokens);
    }

    private static HttpResponseMessage Status(HttpStatusCode status) =>
        new(status) { Content = new StringContent("""{ "error": "x" }""", Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    private sealed class MemorySessions : ISyncSessionStore
    {
        public SyncCredentials? Stored { get; set; }

        public ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Stored is null ? null : Stored with { });

        public ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default)
        {
            Stored = credentials;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateTokensAsync(
            string accessToken, DateTimeOffset accessExpiresUtc, string refreshToken, CancellationToken cancellationToken = default)
        {
            Stored = Stored! with { AccessToken = accessToken, AccessExpiresUtc = accessExpiresUtc, RefreshToken = refreshToken };
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            Stored = null;
            return ValueTask.CompletedTask;
        }
    }
}
