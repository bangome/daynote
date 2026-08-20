using System.Buffers.Text;
using Daynote.Core.Domain;
using Daynote.Core.Sync;

namespace Daynote.App.Tests.Account;

/// <summary>
/// Builds a real <see cref="AccountService"/> over fakes, so the service's own logic is exercised
/// while the tests stay fast.
/// </summary>
/// <remarks>
/// The crypto fake derives instantly. Using the real Argon2 profile here would cost a third of a
/// second per sign-in for no added coverage — that derivation is already pinned by
/// <c>AesGcmSyncCryptoTests</c>, and this suite is about what the UI does with the result.
/// </remarks>
internal sealed class FakeAccounts
{
    private readonly InstantCrypto crypto = new();
    private readonly MemorySessionStore sessions = new();
    private readonly StubAuth auth;

    internal FakeAccounts(ISyncStore? store = null)
    {
        auth = new StubAuth(this);
        Store = store ?? new NullStore();
        Service = new AccountService(auth, crypto, sessions, Store, () => "Test PC");
    }

    internal AccountService Service { get; }

    internal ISyncStore Store { get; }

    /// <summary>Thrown by the next auth call, for the failure-path tests.</summary>
    internal AccountException? NextFailure { get; set; }

    internal bool SignedOut { get; private set; }

    /// <summary>Set when the server reports that the stored envelope no longer opens.</summary>
    internal bool RewrapPending { get; set; }

    /// <summary>Simulates resetting on a device that never held the key, which lands locked.</summary>
    internal bool LockAfterReset
    {
        get => crypto.FailUnwrap;
        set => crypto.FailUnwrap = value;
    }

    /// <summary>How many times a recovery key actually reached the unlock path.</summary>
    internal int UnlockAttempts { get; private set; }

    internal void RecordUnlockAttempt() => UnlockAttempts += 1;

    private sealed class StubAuth(FakeAccounts owner) : IAuthApiClient
    {
        public ValueTask<string> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.FromResult("11111111-1111-4111-8111-111111111111");
        }

        public ValueTask<SessionResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.FromResult(Session());
        }

        public ValueTask<SessionResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.FromResult(Session());
        }

        public ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            owner.SignedOut = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<AccountSummary> GetAccountAsync(string accessToken, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AccountSummary("u", "alice@example.test", true, false, 1, []));

        public ValueTask RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.CompletedTask;
        }

        public ValueTask ConfirmPasswordResetAsync(ResetConfirmRequest request, CancellationToken cancellationToken = default)
        {
            Throw();
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> RewrapAsync(
            string accessToken,
            string wrappedDekPassword,
            int dekGeneration,
            CancellationToken cancellationToken = default)
        {
            owner.RecordUnlockAttempt();
            Throw();
            // A successful re-wrap means the envelope now opens with the current password.
            owner.LockAfterReset = false;
            return ValueTask.FromResult(dekGeneration + 1);
        }

        private void Throw()
        {
            if (owner.NextFailure is { } failure)
            {
                owner.NextFailure = null;
                throw failure;
            }
        }

        private SessionResponse Session() => new(
            "11111111-1111-4111-8111-111111111111",
            "access",
            DateTimeOffset.UtcNow.AddMinutes(15),
            "refresh",
            // The wrapped-DEK envelope the instant crypto below knows how to open.
            InstantCrypto.Envelope,
            InstantCrypto.Envelope,
            KdfParameters.Argon2idDefault.ToJson(),
            1,
            owner.RewrapPending,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// A stand-in that satisfies the contract's shape without the cost. Wrapping is a fixed marker
    /// rather than real ciphertext; unwrapping returns a key unless <see cref="FailUnwrap"/> is set.
    /// </summary>
    private sealed class InstantCrypto : ISyncCrypto
    {
        internal const string Envelope = "v1.AAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        internal bool FailUnwrap { get; set; }

        public SyncKeySet DeriveKeys(string password, string email, KdfParameters parameters) =>
            new(KeyMaterial.Random(), KeyMaterial.Random());

        public KeyMaterial DeriveRecoveryKek(RecoveryKey recoveryKey) => KeyMaterial.Random();

        public KeyMaterial GenerateDataKey() => KeyMaterial.Random();

        public string WrapDataKey(KeyMaterial dataKey, KeyMaterial wrappingKey, CipherScope scope) => Envelope;

        public DomainResult<KeyMaterial> UnwrapDataKey(string envelope, KeyMaterial wrappingKey, CipherScope scope) =>
            FailUnwrap
                ? DomainResult<KeyMaterial>.Failure(
                    DomainErrorCode.CiphertextAuthenticationFailed,
                    "The data could not be decrypted with this key.")
                : DomainResult<KeyMaterial>.Success(KeyMaterial.Random());

        public string Encrypt(string plaintext, KeyMaterial dataKey, CipherScope scope) => Envelope;

        public DomainResult<string> Decrypt(string envelope, KeyMaterial dataKey, CipherScope scope) =>
            DomainResult<string>.Success("{}");

        public string BlindAssetKey(KeyMaterial dataKey, string contentHash) =>
            Base64Url.EncodeToString(dataKey.Span);
    }

    private sealed class MemorySessionStore : ISyncSessionStore
    {
        private SyncCredentials? current;

        public ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(current is null
                ? null
                // A copy, because the caller disposes what it is handed and the store keeps its own.
                : current with
                {
                    DataKey = current.DataKey is { } key ? KeyMaterial.CopyFrom(key.Span) : null,
                });

        public ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default)
        {
            current = credentials with
            {
                DataKey = credentials.DataKey is { } key ? KeyMaterial.CopyFrom(key.Span) : null,
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateTokensAsync(
            string accessToken,
            DateTimeOffset accessExpiresUtc,
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            current = current is null
                ? null
                : current with
                {
                    AccessToken = accessToken,
                    AccessExpiresUtc = accessExpiresUtc,
                    RefreshToken = refreshToken,
                };
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            current = null;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Used only when a test supplies no store of its own.</summary>
    private sealed class NullStore : ISyncStore
    {
        public ValueTask<int> EnrollExistingContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<IReadOnlyList<PendingNote>> ReadPendingNotesAsync(int limit, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PendingNote>>([]);

        public ValueTask<IReadOnlyList<SyncTombstone>> ReadPendingTombstonesAsync(int limit, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SyncTombstone>>([]);

        public ValueTask<int> AcknowledgePushAsync(IReadOnlyList<PendingAck> acknowledged, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<int> AcknowledgeTombstonesAsync(IReadOnlyList<SyncTombstone> acknowledged, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<MergeOutcome> MergeNotesAsync(IReadOnlyList<SyncNote> notes, IReadOnlyList<SyncTombstone> tombstones, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MergeOutcome.Empty);

        public ValueTask<SyncStateSnapshot> ReadStateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SyncStateSnapshot(null, 0, 0, false, null));

        public ValueTask AdvanceCursorAsync(long cursor, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SignInAsync(string userId, int dekGeneration, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SignOutAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask SetLockedAsync(bool locked, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
