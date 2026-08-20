using System.Security.Cryptography;
using Daynote.Core.Sync;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// An in-memory stand-in for the Worker's account endpoints, mirroring
/// <c>cloud/worker/src/auth.ts</c>: the same stored shape (a verifier over the auth key plus sealed
/// envelopes), the same refusal to distinguish a wrong password from an unknown email, and the same
/// rotating refresh tokens.
/// </summary>
/// <remarks>
/// A mirror, so the two can drift; the Worker's own behaviour is pinned by
/// <c>cloud/worker/test/auth.test.ts</c>. What this buys is exercising the client's account flow —
/// derivation, wrapping, unwrapping, persistence — without a network.
/// <para>
/// <see cref="EverythingReceived"/> records every value the client sent, so tests can assert the
/// password and the plaintext key never appear in it.
/// </para>
/// </remarks>
internal sealed class FakeAuthServer(Func<DateTimeOffset> utcNow) : IAuthApiClient
{
    private readonly Dictionary<string, Account> accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> refreshTokens = new(StringComparer.Ordinal);

    internal List<string> EverythingReceived { get; } = [];

    internal int RegisterCalls { get; private set; }

    internal int RefreshCalls { get; private set; }

    public ValueTask<string> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        RegisterCalls += 1;
        Record(request.Email, request.AuthKey, request.WrappedDekPassword, request.WrappedDekRecovery);

        if (accounts.ContainsKey(request.Email))
        {
            throw new AccountException(
                AccountFailure.EmailAlreadyRegistered,
                "That email address is already registered.");
        }

        string userId = Guid.NewGuid().ToString("D");
        accounts[request.Email] = new Account(
            userId,
            // The real server stores PBKDF2 over the auth key; a hash is enough to reproduce the
            // "cannot be replayed as a key" property that matters to these tests.
            Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(ToHex(request.AuthKey)))),
            request.WrappedDekPassword,
            request.WrappedDekRecovery,
            request.KdfParametersJson,
            Generation: 1,
            RewrapPending: false);
        LastResetCode = null;

        return ValueTask.FromResult(userId);
    }

    public ValueTask<SessionResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        Record(request.Email, request.AuthKey, request.DeviceName);

        if (!accounts.TryGetValue(request.Email, out Account? account) ||
            account.Verifier != Convert.ToHexStringLower(
                SHA256.HashData(Convert.FromHexString(ToHex(request.AuthKey)))))
        {
            // One answer for both, exactly as the Worker does: anything more specific is an
            // account-enumeration oracle.
            throw new AccountException(
                AccountFailure.InvalidCredentials,
                "That email address or password is incorrect.");
        }

        return ValueTask.FromResult(IssueSession(account));
    }

    public ValueTask<SessionResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        RefreshCalls += 1;
        if (!refreshTokens.Remove(refreshToken, out Session? session))
        {
            throw new AccountException(AccountFailure.InvalidCredentials, "The refresh token is not valid.");
        }

        return ValueTask.FromResult(IssueSession(accounts[session.Email]));
    }

    public ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        refreshTokens.Remove(refreshToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask<AccountSummary> GetAccountAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        Account account = accounts.Values.FirstOrDefault()
            ?? throw new AccountException(AccountFailure.InvalidCredentials, "Not signed in.");
        return ValueTask.FromResult(new AccountSummary(
            account.UserId,
            accounts.First(pair => pair.Value == account).Key,
            account.WrappedDekRecovery is not null,
            account.RewrapPending,
            account.Generation,
            [],
            account.WrappedDekRecovery));
    }

    /// <summary>Simulates a password reset: the stored envelope no longer opens with the password.</summary>
    internal void MarkRewrapPending(string email)
    {
        accounts[email] = accounts[email] with { RewrapPending = true };
    }

    /// <summary>The code the last reset request produced, as the email would have carried it.</summary>
    internal string? LastResetCode { get; private set; }

    internal int RewrapCalls { get; private set; }

    public ValueTask RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        Record(email);
        // Always succeeds, even for an unknown address: saying otherwise would be an enumeration
        // oracle, and the client must not be able to tell either.
        LastResetCode = accounts.ContainsKey(email) ? "ABCD-2345" : null;
        return ValueTask.CompletedTask;
    }

    public ValueTask ConfirmPasswordResetAsync(
        ResetConfirmRequest request,
        CancellationToken cancellationToken = default)
    {
        Record(request.Email, request.NewAuthKey, request.Code);

        if (LastResetCode is null || request.Code != LastResetCode || !accounts.ContainsKey(request.Email))
        {
            throw new AccountException(
                AccountFailure.InvalidResetCode,
                "That reset code is not valid. Request a new one.");
        }

        LastResetCode = null;
        // The verifier rotates and the account is flagged, but wrapped_dek_pw is deliberately left
        // alone: the server cannot re-wrap a key it cannot read.
        accounts[request.Email] = accounts[request.Email] with
        {
            Verifier = Verify(request.NewAuthKey),
            RewrapPending = true,
        };
        refreshTokens.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> RewrapAsync(
        string accessToken,
        string wrappedDekPassword,
        int dekGeneration,
        CancellationToken cancellationToken = default)
    {
        RewrapCalls += 1;
        Record(wrappedDekPassword);

        string email = accounts.Keys.Single();
        Account account = accounts[email];
        if (account.Generation != dekGeneration)
        {
            throw new AccountException(
                AccountFailure.ServerError,
                "The account was updated elsewhere. Sign in again and retry.");
        }

        accounts[email] = account with
        {
            WrappedDekPassword = wrappedDekPassword,
            Generation = dekGeneration + 1,
            RewrapPending = false,
        };
        return ValueTask.FromResult(dekGeneration + 1);
    }

    private static string Verify(string authKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(ToHex(authKey))));

    private SessionResponse IssueSession(Account account)
    {
        string email = accounts.First(pair => pair.Value == account).Key;
        string refresh = Guid.NewGuid().ToString("N");
        refreshTokens[refresh] = new Session(email);

        return new SessionResponse(
            account.UserId,
            AccessToken: Guid.NewGuid().ToString("N"),
            // Matches the Worker's 15-minute access token, so token renewal is exercised for real.
            AccessExpiresUtc: utcNow().AddMinutes(15),
            RefreshToken: refresh,
            WrappedDekPassword: account.WrappedDekPassword,
            WrappedDekRecovery: account.WrappedDekRecovery,
            KdfParametersJson: account.KdfParametersJson,
            DekGeneration: account.Generation,
            RewrapPending: account.RewrapPending,
            ServerUtc: utcNow());
    }

    private void Record(params string?[] values) =>
        EverythingReceived.AddRange(values.Where(value => value is not null)!);

    /// <summary>base64url to hex, so the fake can hash it the way the real verifier would.</summary>
    private static string ToHex(string base64Url) =>
        Convert.ToHexString(System.Buffers.Text.Base64Url.DecodeFromChars(base64Url));

    private sealed record Account(
        string UserId,
        string Verifier,
        string WrappedDekPassword,
        string? WrappedDekRecovery,
        string KdfParametersJson,
        int Generation,
        bool RewrapPending);

    private sealed record Session(string Email);
}
