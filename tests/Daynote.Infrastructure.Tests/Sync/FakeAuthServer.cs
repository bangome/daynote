using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// An in-memory stand-in for the Worker's account endpoints, mirroring
/// <c>cloud/worker/src/auth.ts</c>: an account per Google subject, created on first sign-in, one
/// server-held data key per account, and rotating refresh tokens.
/// </summary>
/// <remarks>
/// A mirror, so the two can drift; the Worker's own behaviour is pinned by
/// <c>cloud/worker/test/auth.test.ts</c>. What this buys is exercising the client's account flow —
/// session persistence, enrolment, sign-out — without a network or a browser.
/// <para>
/// <see cref="EverythingReceived"/> records every value the client sent, so a test can assert what
/// does and does not leave the device.
/// </para>
/// </remarks>
internal sealed class FakeAuthServer(Func<DateTimeOffset> utcNow) : IAuthApiClient
{
    private readonly Dictionary<string, Account> accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> refreshTokens = new(StringComparer.Ordinal);

    /// <summary>Codes the fake Google will redeem, mapped to the identity behind them.</summary>
    internal Dictionary<string, (string Subject, string Email)> Codes { get; } =
        new(StringComparer.Ordinal);

    internal List<string> EverythingReceived { get; } = [];

    internal int SignInCalls { get; private set; }

    internal int RefreshCalls { get; private set; }

    /// <summary>How many accounts exist, so a test can prove a second sign-in did not create one.</summary>
    internal int AccountCount => accounts.Count;

    public ValueTask<SessionResponse> SignInWithGoogleAsync(
        GoogleSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        SignInCalls += 1;
        Record(request.AuthorizationCode, request.CodeVerifier, request.RedirectUri, request.DeviceName);

        if (!Codes.TryGetValue(request.AuthorizationCode, out (string Subject, string Email) identity))
        {
            // A code that was already redeemed, expired, or never issued. The Worker turns Google's
            // invalid_grant into exactly this.
            throw new AccountException(
                AccountFailure.InvalidCredentials,
                "That Google sign-in is no longer valid. Try again.");
        }

        if (!accounts.TryGetValue(identity.Subject, out Account? account))
        {
            account = new Account(
                Guid.NewGuid().ToString("D"),
                identity.Email,
                // The real server generates this and seals it under a Worker secret; what matters to
                // the client is that the same account always gets the same key back.
                KeyMaterial.Random(),
                KeyProtection.Server,
                null,
                null,
                null);
            accounts[identity.Subject] = account;
        }
        else
        {
            account = account with { Email = identity.Email };
            accounts[identity.Subject] = account;
        }

        return ValueTask.FromResult(NewSession(account, includeKeys: true));
    }

    public ValueTask<SessionResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        RefreshCalls += 1;

        if (!refreshTokens.TryGetValue(refreshToken, out Session? session) || session.Revoked)
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "The refresh token is not valid.");
        }

        // Rotation: the presented token is spent, and presenting it again is treated as theft.
        refreshTokens[refreshToken] = session with { Revoked = true };
        Account account = accounts.Values.Single(candidate => candidate.UserId == session.UserId);

        // No key material on refresh, matching the Worker: a refresh renews a session.
        return ValueTask.FromResult(NewSession(account, includeKeys: false));
    }

    public ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (refreshTokens.TryGetValue(refreshToken, out Session? session))
        {
            refreshTokens[refreshToken] = session with { Revoked = true };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<AccountSummary> GetAccountAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        Account account = Authenticate(accessToken);
        return ValueTask.FromResult(new AccountSummary(account.UserId, account.Email, []));
    }

    public ValueTask<KeyMaterialResponse> GetKeyMaterialAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(KeysFor(Authenticate(accessToken)));
    }

    public ValueTask ProtectAsync(
        string accessToken,
        string wrappedDekPassphrase,
        string wrappedDekRecovery,
        string kdfParametersJson,
        CancellationToken cancellationToken = default)
    {
        Account account = Authenticate(accessToken);
        Record(wrappedDekPassphrase, wrappedDekRecovery, kdfParametersJson);

        // The server destroys its own copy in the same step, exactly as the Worker does. Keeping it
        // here would let a test pass that the real thing would fail.
        Replace(account with
        {
            Protection = KeyProtection.Passphrase,
            ServerKey = null,
            WrappedPassphrase = wrappedDekPassphrase,
            WrappedRecovery = wrappedDekRecovery,
            KdfParametersJson = kdfParametersJson,
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask UnprotectAsync(
        string accessToken,
        KeyMaterial dataKey,
        CancellationToken cancellationToken = default)
    {
        Account account = Authenticate(accessToken);
        Replace(account with
        {
            Protection = KeyProtection.Server,
            ServerKey = KeyMaterial.CopyFrom(dataKey.Span),
            WrappedPassphrase = null,
            WrappedRecovery = null,
            KdfParametersJson = null,
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask<(Entitlement Entitlement, BillingLinks Links)> GetBillingAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        _ = Authenticate(accessToken);
        return ValueTask.FromResult((Entitlement, new BillingLinks(true, true)));
    }

    public ValueTask<string> CreateCheckoutSessionAsync(
        string accessToken,
        BillingPlan plan,
        CancellationToken cancellationToken = default)
    {
        Account account = Authenticate(accessToken);
        return ValueTask.FromResult($"https://pay.test/checkout?user={account.UserId}&plan={plan.ToWire()}");
    }

    public ValueTask<string> CreatePortalSessionAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        Account account = Authenticate(accessToken);
        return ValueTask.FromResult($"https://pay.test/manage/{account.UserId}?token=single-use");
    }

    /// <summary>
    /// The billing state this fake reports. Defaults to an active subscription so the account and
    /// lock suites are about what they are about; the entitlement rules themselves are pinned by
    /// the Worker's own tests.
    /// </summary>
    internal Entitlement Entitlement { get; set; } =
        new(EntitlementState.Active, DateTimeOffset.UtcNow.AddDays(30), true, true);

    /// <summary>True once the account has taken its key away from this server.</summary>
    internal bool IsLocked =>
        accounts.Values.Any(account => account.Protection == KeyProtection.Passphrase);

    /// <summary>The key the server still holds, or null once the lock is on.</summary>
    internal KeyMaterial? ServerHeldKey =>
        accounts.Values.SingleOrDefault()?.ServerKey;

    private static KeyMaterialResponse KeysFor(Account account) =>
        account.Protection == KeyProtection.Passphrase
            ? new KeyMaterialResponse(
                KeyProtection.Passphrase,
                null,
                account.WrappedPassphrase,
                account.WrappedRecovery,
                account.KdfParametersJson)
            : new KeyMaterialResponse(
                KeyProtection.Server,
                System.Buffers.Text.Base64Url.EncodeToString(account.ServerKey!.Span),
                null,
                null,
                null);

    private void Replace(Account updated)
    {
        string subject = accounts.Single(entry => entry.Value.UserId == updated.UserId).Key;
        accounts[subject] = updated;
    }

    /// <summary>Issues a code the way Google would, so a test can drive one sign-in.</summary>
    internal string IssueCode(string subject, string email)
    {
        string code = $"code-{Guid.NewGuid():N}";
        Codes[code] = (subject, email);
        return code;
    }

    private Account Authenticate(string accessToken)
    {
        Account? account = accounts.Values.FirstOrDefault(
            candidate => accessToken == $"access-{candidate.UserId}");
        return account
            ?? throw new AccountException(AccountFailure.InvalidCredentials, "The access token is not valid.");
    }

    private SessionResponse NewSession(Account account, bool includeKeys)
    {
        string refreshToken = $"refresh-{Guid.NewGuid():N}";
        refreshTokens[refreshToken] = new Session(account.UserId, Revoked: false);

        return new SessionResponse(
            account.UserId,
            account.Email,
            $"access-{account.UserId}",
            utcNow().AddMinutes(15),
            refreshToken,
            includeKeys ? KeysFor(account) : null,
            Entitlement,
            utcNow());
    }

    private void Record(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (value is not null)
            {
                EverythingReceived.Add(value);
            }
        }
    }

    private sealed record Account(
        string UserId,
        string Email,
        KeyMaterial? ServerKey,
        KeyProtection Protection,
        string? WrappedPassphrase,
        string? WrappedRecovery,
        string? KdfParametersJson);

    private sealed record Session(string UserId, bool Revoked);
}

/// <summary>
/// Stands in for the browser half of Google sign-in: hands back a grant for a code the fake server
/// will redeem, without opening anything.
/// </summary>
internal sealed class FakeIdentityProvider(FakeAuthServer server, string subject, string email)
    : IIdentityProvider
{
    /// <summary>Set to make the next attempt behave like the user closing the browser window.</summary>
    internal bool Cancel { get; set; }

    internal int AuthorizeCalls { get; private set; }

    public ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        AuthorizeCalls += 1;
        if (Cancel)
        {
            throw new AccountException(
                AccountFailure.SignInCancelled,
                "The sign-in was not completed. Try again when you are ready.");
        }

        return ValueTask.FromResult(new IdentityGrant(
            server.IssueCode(subject, email),
            "verifier",
            "http://127.0.0.1:53219/"));
    }
}
