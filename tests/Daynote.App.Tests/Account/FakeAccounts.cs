using System.Buffers.Text;
using Daynote.Core.Sync;

namespace Daynote.App.Tests.Account;

/// <summary>
/// Builds a real <see cref="AccountService"/> over fakes, so the service's own logic is exercised
/// while the tests stay fast.
/// </summary>
/// <remarks>
/// Neither Google nor a browser is involved: <see cref="StubIdentity"/> returns a grant, and
/// <see cref="StubAuth"/> redeems it. This suite is about what the UI does with the result.
/// </remarks>
internal sealed class FakeAccounts
{
    private readonly MemorySessionStore sessions = new();
    private readonly StubAuth auth;
    private readonly StubIdentity identity;

    internal FakeAccounts(ISyncStore? store = null)
    {
        auth = new StubAuth(this);
        identity = new StubIdentity(this);
        Store = store ?? new FakeSyncStore();
        Service = new AccountService(
            auth, identity, new Daynote.Infrastructure.Sync.AesGcmSyncCrypto(), sessions, Store,
            () => "Test PC");
    }

    internal AccountService Service { get; }

    internal ISyncStore Store { get; }

    /// <summary>The address the fake Google account signs in with.</summary>
    internal string Email { get; set; } = "alice@example.test";

    /// <summary>Thrown by the next auth call, for the failure-path tests.</summary>
    internal AccountException? NextFailure { get; set; }

    /// <summary>Thrown by the next browser step, for the cancellation path.</summary>
    internal AccountException? NextIdentityFailure { get; set; }

    /// <summary>Set to omit the data key from the sign-in response, as a broken server would.</summary>
    internal bool WithholdDataKey { get; set; }

    /// <summary>The billing state the fake server reports. Active by default.</summary>
    internal Entitlement Entitlement { get; set; } =
        new(EntitlementState.Active, DateTimeOffset.UtcNow.AddDays(30), true, true);

    internal BillingLinks Billing { get; set; } = new(true, true);

    /// <summary>The one-shot links the fake provider mints.</summary>
    internal string CheckoutUrl { get; set; } = "https://pay.test/checkout?txn=one-shot";

    internal string PortalUrl { get; set; } = "https://pay.test/manage?token=single-use";

    /// <summary>Which custody the fake account's key is in.</summary>
    internal KeyProtection Protection { get; set; } = KeyProtection.Server;

    internal string? WrappedPassphrase { get; set; }

    internal string? WrappedRecovery { get; set; }

    internal string? KdfParametersJson { get; set; }

    internal bool SignedOut { get; private set; }

    internal int SignInCalls { get; private set; }

    /// <summary>How many links were minted, so a test can prove they are not reused.</summary>
    internal int CheckoutSessionsMinted { get; set; }

    /// <summary>The plan the last checkout was minted for, so a test can prove the choice travelled.</summary>
    internal BillingPlan? LastCheckoutPlan { get; set; }

    internal int PortalSessionsMinted { get; set; }

    /// <summary>Reads the stored session, so a test can assert what sign-out actually cleared.</summary>
    internal ValueTask<SyncCredentials?> LoadSessionAsync() => sessions.LoadAsync();

    private sealed class StubIdentity(FakeAccounts owner) : IIdentityProvider
    {
        public ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default)
        {
            if (owner.NextIdentityFailure is { } failure)
            {
                owner.NextIdentityFailure = null;
                throw failure;
            }

            return ValueTask.FromResult(new IdentityGrant("code", "verifier", "http://127.0.0.1:1/"));
        }
    }

    private sealed class StubAuth(FakeAccounts owner) : IAuthApiClient
    {
        private readonly string userId = Guid.NewGuid().ToString("D");
        private readonly byte[] dataKey = KeyMaterial.Random().Span.ToArray();

        public ValueTask<SessionResponse> SignInWithGoogleAsync(
            GoogleSignInRequest request,
            CancellationToken cancellationToken = default)
        {
            owner.SignInCalls += 1;
            Throw();
            owner.SignedOut = false;
            return ValueTask.FromResult(Session(includeKeys: true));
        }

        public ValueTask<SessionResponse> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.FromResult(Session(includeKeys: false));
        }

        public ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            owner.SignedOut = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AccountSummary> GetAccountAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.FromResult(new AccountSummary(userId, owner.Email, []));
        }

        public ValueTask<KeyMaterialResponse> GetKeyMaterialAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.FromResult(Keys());
        }

        public ValueTask ProtectAsync(
            string accessToken,
            string wrappedDekPassphrase,
            string wrappedDekRecovery,
            string kdfParametersJson,
            CancellationToken cancellationToken = default)
        {
            Throw();
            owner.Protection = KeyProtection.Passphrase;
            owner.WrappedPassphrase = wrappedDekPassphrase;
            owner.WrappedRecovery = wrappedDekRecovery;
            owner.KdfParametersJson = kdfParametersJson;
            return ValueTask.CompletedTask;
        }

        public ValueTask UnprotectAsync(
            string accessToken,
            KeyMaterial key,
            CancellationToken cancellationToken = default)
        {
            Throw();
            owner.Protection = KeyProtection.Server;
            owner.WrappedPassphrase = null;
            owner.WrappedRecovery = null;
            owner.KdfParametersJson = null;
            return ValueTask.CompletedTask;
        }

        public ValueTask<(Entitlement Entitlement, BillingLinks Links)> GetBillingAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.FromResult((owner.Entitlement, owner.Billing));
        }

        public ValueTask<string> CreateCheckoutSessionAsync(
            string accessToken,
            BillingPlan plan,
            CancellationToken cancellationToken = default)
        {
            Throw();
            owner.CheckoutSessionsMinted += 1;
            owner.LastCheckoutPlan = plan;
            return ValueTask.FromResult(owner.CheckoutUrl);
        }

        public ValueTask<string> CreatePortalSessionAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            Throw();
            owner.PortalSessionsMinted += 1;
            return ValueTask.FromResult(owner.PortalUrl);
        }

        private KeyMaterialResponse Keys() => owner.Protection == KeyProtection.Passphrase
            ? new KeyMaterialResponse(
                KeyProtection.Passphrase,
                null,
                owner.WrappedPassphrase,
                owner.WrappedRecovery,
                owner.KdfParametersJson)
            : new KeyMaterialResponse(
                KeyProtection.Server,
                owner.WithholdDataKey ? null : Base64Url.EncodeToString(dataKey),
                null,
                null,
                null);

        private void Throw()
        {
            if (owner.NextFailure is { } failure)
            {
                owner.NextFailure = null;
                throw failure;
            }
        }

        private SessionResponse Session(bool includeKeys) => new(
            userId,
            owner.Email,
            "access-token",
            DateTimeOffset.UtcNow.AddMinutes(15),
            "refresh-token",
            includeKeys ? Keys() : null,
            owner.Entitlement,
            DateTimeOffset.UtcNow);
    }

    private sealed class MemorySessionStore : ISyncSessionStore
    {
        private SyncCredentials? stored;

        public ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(stored is null
                ? null
                : stored with
                {
                    DataKey = stored.DataKey is null ? null : KeyMaterial.CopyFrom(stored.DataKey.Span),
                });

        public ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default)
        {
            stored = credentials with
            {
                DataKey = credentials.DataKey is null ? null : KeyMaterial.CopyFrom(credentials.DataKey.Span),
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateTokensAsync(
            string accessToken,
            DateTimeOffset accessExpiresUtc,
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            if (stored is not null)
            {
                stored = stored with
                {
                    AccessToken = accessToken,
                    AccessExpiresUtc = accessExpiresUtc,
                    RefreshToken = refreshToken,
                };
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            stored = null;
            return ValueTask.CompletedTask;
        }
    }
}
