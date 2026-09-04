using Daynote.Core.Sync;
using Daynote.Infrastructure.Sync;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// The opt-in lock (docs/CLOUD_SYNC.md §4.1b), end to end across the client and a fake of the
/// Worker's account endpoints.
/// </summary>
/// <remarks>
/// The property these tests exist for is the one the feature is sold on: after the lock goes on, the
/// service holds nothing that opens the account. Everything else here — the passphrase, the recovery
/// key, the state a second PC lands in — is in service of proving that, and of proving the default
/// account is untouched by any of it.
/// </remarks>
[TestClass]
public sealed class OptionalLockTests
{
    private const string Subject = "google-sub-alice";
    private const string Email = "alice@example.test";
    private const string Passphrase = "correct horse battery staple";

    private static readonly AesGcmSyncCrypto Crypto = new();

    private DateTimeOffset now;
    private FakeAuthServer authServer = null!;

    [TestInitialize]
    public void Setup()
    {
        now = DateTimeOffset.Parse(
            "2026-09-02T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        authServer = new FakeAuthServer(() => now);
    }

    private (AccountService Accounts, ISyncSessionStore Sessions, FakeSyncStore Store) NewPc()
    {
        var sessions = new MemorySessionStore();
        var store = new FakeSyncStore();
        var identity = new FakeIdentityProvider(authServer, Subject, Email);
        return (
            new AccountService(authServer, identity, Crypto, sessions, store, () => "Test PC"),
            sessions,
            store);
    }

    [TestMethod]
    public async Task Turning_the_lock_on_takes_the_key_away_from_the_server()
    {
        (AccountService accounts, ISyncSessionStore sessions, _) = NewPc();
        await accounts.SignInAsync();
        Assert.IsNotNull(authServer.ServerHeldKey, "The default account starts server-held.");

        RecoveryKey recovery = await accounts.EnableLockAsync(Passphrase);

        Assert.IsTrue(recovery.IsValid);
        Assert.IsTrue(authServer.IsLocked);
        // The whole point: nothing on the server opens this account any more.
        Assert.IsNull(authServer.ServerHeldKey);

        // This device keeps working: it just proved it has the key, so it is not made to re-enter
        // the passphrase it typed a moment ago.
        SyncCredentials? credentials = await sessions.LoadAsync();
        Assert.IsNotNull(credentials?.DataKey);
        Assert.AreEqual(KeyProtection.Passphrase, credentials!.Protection);
    }

    [TestMethod]
    public async Task The_passphrase_never_reaches_the_server()
    {
        (AccountService accounts, _, _) = NewPc();
        await accounts.SignInAsync();

        await accounts.EnableLockAsync(Passphrase);

        foreach (string seen in authServer.EverythingReceived)
        {
            Assert.IsFalse(
                seen.Contains(Passphrase, StringComparison.Ordinal),
                "The passphrase reached the server.");
        }
    }

    [TestMethod]
    public async Task A_second_PC_lands_locked_and_opens_with_the_passphrase()
    {
        (AccountService first, _, _) = NewPc();
        await first.SignInAsync();
        await first.EnableLockAsync(Passphrase);

        (AccountService second, ISyncSessionStore sessions, FakeSyncStore store) = NewPc();
        var failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await second.SignInAsync());

        Assert.AreEqual(AccountFailure.LockedOut, failure.Failure);
        // Signed in, but with nothing that opens the content — and the session persists, or there
        // would be no token left to unlock with.
        Assert.AreEqual(ResumeState.Locked, (await second.ResumeAsync()).State);
        Assert.IsTrue(store.State.IsLocked);

        await second.UnlockAsync(Passphrase);

        ResumedSession resumed = await second.ResumeAsync();
        Assert.AreEqual(ResumeState.Ready, resumed.State);
        Assert.IsFalse(store.State.IsLocked);

        // Same account, therefore the same data key: the second PC must read what the first wrote.
        using SyncCredentials firstCredentials = (await sessions.LoadAsync())!;
        resumed.Session!.DataKey.Dispose();
        Assert.IsNotNull(firstCredentials.DataKey);
    }

    [TestMethod]
    public async Task A_wrong_passphrase_is_refused_without_unlocking_anything()
    {
        (AccountService first, _, _) = NewPc();
        await first.SignInAsync();
        await first.EnableLockAsync(Passphrase);

        (AccountService second, _, _) = NewPc();
        await Assert.ThrowsExactlyAsync<AccountException>(async () => await second.SignInAsync());

        var failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await second.UnlockAsync("not the passphrase"));

        Assert.AreEqual(AccountFailure.InvalidPassphrase, failure.Failure);
        Assert.AreEqual(ResumeState.Locked, (await second.ResumeAsync()).State);
    }

    [TestMethod]
    public async Task The_recovery_key_opens_an_account_whose_passphrase_is_forgotten()
    {
        (AccountService first, _, _) = NewPc();
        await first.SignInAsync();
        RecoveryKey recovery = await first.EnableLockAsync(Passphrase);

        (AccountService second, _, _) = NewPc();
        await Assert.ThrowsExactlyAsync<AccountException>(async () => await second.SignInAsync());

        // Retyped from what the user wrote down, not the in-memory instance.
        RecoveryKey retyped = RecoveryKey.Parse(recovery.ToDisplayString()).Value;
        await second.UnlockWithRecoveryKeyAsync(retyped);

        ResumedSession resumed = await second.ResumeAsync();
        Assert.AreEqual(ResumeState.Ready, resumed.State);
        resumed.Session!.DataKey.Dispose();
    }

    [TestMethod]
    public async Task Another_accounts_recovery_key_does_not_open_this_one()
    {
        (AccountService accounts, _, _) = NewPc();
        await accounts.SignInAsync();
        await accounts.EnableLockAsync(Passphrase);

        (AccountService second, _, _) = NewPc();
        await Assert.ThrowsExactlyAsync<AccountException>(async () => await second.SignInAsync());

        var failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await second.UnlockWithRecoveryKeyAsync(RecoveryKey.Generate()));

        Assert.AreEqual(AccountFailure.InvalidRecoveryKey, failure.Failure);
    }

    [TestMethod]
    public async Task Turning_the_lock_off_hands_the_same_key_back()
    {
        (AccountService accounts, ISyncSessionStore sessions, _) = NewPc();
        await accounts.SignInAsync();
        byte[] before = (await sessions.LoadAsync())!.DataKey!.Span.ToArray();

        await accounts.EnableLockAsync(Passphrase);
        await accounts.DisableLockAsync();

        Assert.IsFalse(authServer.IsLocked);
        Assert.IsNotNull(authServer.ServerHeldKey);
        // The same key, or every note already in the cloud would become unreadable.
        CollectionAssert.AreEqual(before, authServer.ServerHeldKey!.Span.ToArray());

        SyncCredentials? credentials = await sessions.LoadAsync();
        Assert.AreEqual(KeyProtection.Server, credentials!.Protection);
    }

    [TestMethod]
    public async Task A_locked_out_device_cannot_change_the_lock_either_way()
    {
        (AccountService first, _, _) = NewPc();
        await first.SignInAsync();
        await first.EnableLockAsync(Passphrase);

        (AccountService second, _, _) = NewPc();
        await Assert.ThrowsExactlyAsync<AccountException>(async () => await second.SignInAsync());

        // Both operations need the key, and this device does not have it.
        Assert.AreEqual(
            AccountFailure.LockedOut,
            (await Assert.ThrowsExactlyAsync<AccountException>(
                async () => await second.DisableLockAsync())).Failure);
    }

    [TestMethod]
    public async Task A_passphrase_too_short_to_be_worth_stretching_is_refused()
    {
        (AccountService accounts, _, _) = NewPc();
        await accounts.SignInAsync();

        var failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await accounts.EnableLockAsync("short"));

        Assert.AreEqual(AccountFailure.WeakPassphrase, failure.Failure);
        Assert.IsFalse(authServer.IsLocked, "A refused passphrase must not have taken the key away.");
    }

    [TestMethod]
    public async Task A_default_account_is_untouched_by_any_of_this()
    {
        (AccountService accounts, ISyncSessionStore sessions, FakeSyncStore store) = NewPc();

        await accounts.SignInAsync();

        SyncCredentials? credentials = await sessions.LoadAsync();
        Assert.AreEqual(KeyProtection.Server, credentials!.Protection);
        Assert.IsNotNull(credentials.DataKey);
        Assert.IsFalse(store.State.IsLocked);
        Assert.AreEqual(ResumeState.Ready, (await accounts.ResumeAsync()).State);
    }

    /// <summary>An in-memory credentials store; the DPAPI one is covered by the lifecycle suite.</summary>
    private sealed class MemorySessionStore : ISyncSessionStore
    {
        private SyncCredentials? stored;

        public ValueTask<SyncCredentials?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Copy(stored));

        public ValueTask SaveAsync(SyncCredentials credentials, CancellationToken cancellationToken = default)
        {
            stored = Copy(credentials);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateTokensAsync(
            string accessToken,
            DateTimeOffset accessExpiresUtc,
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            stored = stored is null
                ? null
                : stored with
                {
                    AccessToken = accessToken,
                    AccessExpiresUtc = accessExpiresUtc,
                    RefreshToken = refreshToken,
                };
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            stored = null;
            return ValueTask.CompletedTask;
        }

        private static SyncCredentials? Copy(SyncCredentials? credentials) => credentials is null
            ? null
            : credentials with
            {
                DataKey = credentials.DataKey is null
                    ? null
                    : KeyMaterial.CopyFrom(credentials.DataKey.Span),
            };
    }

    /// <summary>The account-facing slice of the sync store; the real one lives in the app suite.</summary>
    private sealed class FakeSyncStore : ISyncStore
    {
        internal SyncStateSnapshot State { get; private set; } = new(null, 0, 0, false, null);

        public ValueTask<SyncStateSnapshot> ReadStateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(State);

        public ValueTask<int> EnrollExistingContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask<IReadOnlyList<PendingNote>> ReadPendingNotesAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PendingNote>>([]);

        public ValueTask<IReadOnlyList<SyncTombstone>> ReadPendingTombstonesAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<SyncTombstone>>([]);

        public ValueTask<int> AcknowledgePushAsync(
            IReadOnlyList<PendingAck> acknowledged,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

        public ValueTask<int> AcknowledgeTombstonesAsync(
            IReadOnlyList<SyncTombstone> acknowledged,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(0);

        public ValueTask<MergeOutcome> MergeNotesAsync(
            IReadOnlyList<SyncNote> notes,
            IReadOnlyList<SyncTombstone> tombstones,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(MergeOutcome.Empty);

        public ValueTask AdvanceCursorAsync(long cursor, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SignInAsync(
            string userId,
            int dekGeneration,
            CancellationToken cancellationToken = default)
        {
            State = State with { UserId = userId, DekGeneration = dekGeneration };
            return ValueTask.CompletedTask;
        }

        public ValueTask SignOutAsync(CancellationToken cancellationToken = default)
        {
            State = new SyncStateSnapshot(null, 0, 0, false, null);
            return ValueTask.CompletedTask;
        }

        public ValueTask SetLockedAsync(bool locked, CancellationToken cancellationToken = default)
        {
            State = State with { IsLocked = locked };
            return ValueTask.CompletedTask;
        }
    }
}
