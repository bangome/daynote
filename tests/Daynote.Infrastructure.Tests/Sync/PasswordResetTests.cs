using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// Password reset with end-to-end encryption: the three ways the data key can come back, and the one
/// way it cannot (docs/CLOUD_SYNC.md §4.8).
/// </summary>
[TestClass]
public sealed class PasswordResetTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-08-20").Value;
    private static readonly AesGcmSyncCrypto Crypto = new();
    private const string Email = "alice@example.test";
    private const string OldPassword = "the original password";
    private const string NewPassword = "the replacement password";

    private DateTimeOffset now;
    private FakeAuthServer authServer = null!;
    private InMemorySyncServer syncServer = null!;
    private readonly List<string> roots = [];

    [TestInitialize]
    public void Setup()
    {
        now = DateTimeOffset.Parse(
            "2026-08-20T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        authServer = new FakeAuthServer(() => now);
        syncServer = new InMemorySyncServer(() => now);
        roots.Clear();
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string root in roots.Where(Directory.Exists))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Resetting_on_the_same_PC_unlocks_without_asking_for_anything()
    {
        // §4.8b, the common case: the user forgot their password but is sitting at their own machine,
        // where credentials.dat still holds the data key. Nothing should be lost, and nothing typed.
        await using Pc pc = NewPc();
        await pc.Accounts.RegisterAsync(Email, OldPassword);
        await pc.AddNote(1, "회의록", "분기 계획");
        await pc.Sync();

        await pc.Accounts.RequestPasswordResetAsync(Email);
        await pc.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);

        Assert.IsFalse((await pc.Store.ReadStateAsync()).IsLocked);
        SyncReport report = await pc.Sync();
        Assert.AreEqual(0, report.Undecryptable);
        Assert.AreEqual("회의록", (await pc.Notes()).Single().Title);
    }

    [TestMethod]
    public async Task Resetting_on_a_fresh_PC_leaves_the_account_locked()
    {
        await using Pc original = NewPc();
        await original.Accounts.RegisterAsync(Email, OldPassword);
        await original.AddNote(1, "Title", "body");
        await original.Sync();

        await using Pc fresh = NewPc();
        await fresh.Accounts.RequestPasswordResetAsync(Email);
        await fresh.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);

        // Nothing here can open the envelope, and the reset could not re-wrap it.
        Assert.IsTrue((await fresh.Store.ReadStateAsync()).IsLocked);
        Assert.AreEqual(SyncOutcome.Locked, (await fresh.Sync()).Outcome);
        Assert.AreEqual(0, (await fresh.Notes()).Count);
    }

    [TestMethod]
    public async Task The_recovery_key_unlocks_a_fresh_PC_after_a_reset()
    {
        // §4.8a: the promise the recovery-key screen makes at registration.
        await using Pc original = NewPc();
        RegisteredAccount account = await original.Accounts.RegisterAsync(Email, OldPassword);
        await original.AddNote(1, "회의록", "분기 계획 논의");
        await original.Sync();

        await using Pc fresh = NewPc();
        await fresh.Accounts.RequestPasswordResetAsync(Email);
        await fresh.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);
        Assert.IsTrue((await fresh.Store.ReadStateAsync()).IsLocked);

        await fresh.Accounts.UnlockWithRecoveryKeyAsync(account.RecoveryKey, NewPassword);

        Assert.IsFalse((await fresh.Store.ReadStateAsync()).IsLocked);
        SyncReport report = await fresh.Sync();
        Assert.AreEqual(0, report.Undecryptable);
        Assert.AreEqual("분기 계획 논의", (await fresh.Notes()).Single().Body);
    }

    [TestMethod]
    public async Task The_recovery_key_retyped_from_paper_still_works()
    {
        // The path that actually happens: the user reads it off a piece of paper.
        await using Pc original = NewPc();
        RegisteredAccount account = await original.Accounts.RegisterAsync(Email, OldPassword);
        await original.AddNote(1, "Title", "body");
        await original.Sync();

        await using Pc fresh = NewPc();
        await fresh.Accounts.RequestPasswordResetAsync(Email);
        await fresh.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);

        RecoveryKey retyped = RecoveryKey
            .Parse(account.RecoveryKey.ToDisplayString().ToLowerInvariant())
            .Value;
        await fresh.Accounts.UnlockWithRecoveryKeyAsync(retyped, NewPassword);

        await fresh.Sync();
        Assert.AreEqual("Title", (await fresh.Notes()).Single().Title);
    }

    [TestMethod]
    public async Task A_wrong_recovery_key_is_refused_and_leaves_the_account_locked()
    {
        await using Pc original = NewPc();
        await original.Accounts.RegisterAsync(Email, OldPassword);
        await original.Sync();

        await using Pc fresh = NewPc();
        await fresh.Accounts.RequestPasswordResetAsync(Email);
        await fresh.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);

        AccountException failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await fresh.Accounts.UnlockWithRecoveryKeyAsync(RecoveryKey.Generate(), NewPassword));

        Assert.AreEqual(AccountFailure.InvalidRecoveryKey, failure.Failure);
        Assert.IsTrue((await fresh.Store.ReadStateAsync()).IsLocked);
    }

    [TestMethod]
    public async Task After_unlocking_once_the_new_password_alone_works_everywhere_else()
    {
        // The re-wrap is what makes the unlock permanent rather than per-device.
        await using Pc original = NewPc();
        RegisteredAccount account = await original.Accounts.RegisterAsync(Email, OldPassword);
        await original.AddNote(1, "Title", "body");
        await original.Sync();

        await using Pc first = NewPc();
        await first.Accounts.RequestPasswordResetAsync(Email);
        await first.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);
        await first.Accounts.UnlockWithRecoveryKeyAsync(account.RecoveryKey, NewPassword);

        await using Pc second = NewPc();
        await second.Accounts.SignInAsync(Email, NewPassword);

        Assert.IsFalse((await second.Store.ReadStateAsync()).IsLocked);
        await second.Sync();
        Assert.AreEqual("Title", (await second.Notes()).Single().Title);
    }

    [TestMethod]
    public async Task The_old_password_stops_working_after_a_reset()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.RegisterAsync(Email, OldPassword);
        await pc.Accounts.RequestPasswordResetAsync(Email);
        await pc.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);
        await pc.Accounts.SignOutAsync();

        AccountException failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await pc.Accounts.SignInAsync(Email, OldPassword));

        Assert.AreEqual(AccountFailure.InvalidCredentials, failure.Failure);
    }

    [TestMethod]
    public async Task A_wrong_reset_code_changes_nothing()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.RegisterAsync(Email, OldPassword);
        await pc.Accounts.RequestPasswordResetAsync(Email);

        AccountException failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await pc.Accounts.ConfirmPasswordResetAsync(Email, "ZZZZ-ZZZZ", NewPassword));

        Assert.AreEqual(AccountFailure.InvalidResetCode, failure.Failure);
        await pc.Accounts.SignOutAsync();
        await pc.Accounts.SignInAsync(Email, OldPassword);
    }

    [TestMethod]
    public async Task Requesting_a_reset_for_an_unknown_address_reports_success()
    {
        // Reporting anything else would let the client be used to test whether an address is
        // registered, which is exactly what the server refuses to answer.
        await using Pc pc = NewPc();

        await pc.Accounts.RequestPasswordResetAsync("nobody-here@example.test");

        Assert.IsNull(authServer.LastResetCode);
    }

    [TestMethod]
    public async Task Discarding_the_cloud_copy_keeps_the_local_notes()
    {
        // §4.8c, the only unrecoverable case: no password, no recovery key, no other device. It has to
        // be survivable, and it is, because this PC's notes were never encrypted.
        await using Pc pc = NewPc();
        await pc.Accounts.RegisterAsync(Email, OldPassword);
        await pc.AddNote(1, "이건 남아야 한다", "본문");
        await pc.Sync();

        await pc.Accounts.DiscardCloudCopyAsync();

        Assert.IsFalse((await pc.Store.ReadStateAsync()).IsSignedIn);
        Assert.AreEqual("이건 남아야 한다", (await pc.Notes()).Single().Title);
    }

    [TestMethod]
    public async Task A_reset_never_sends_the_new_password_to_the_server()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.RegisterAsync(Email, OldPassword);
        await pc.Accounts.RequestPasswordResetAsync(Email);
        await pc.Accounts.ConfirmPasswordResetAsync(Email, authServer.LastResetCode!, NewPassword);

        foreach (string seen in authServer.EverythingReceived)
        {
            Assert.IsFalse(seen.Contains(NewPassword, StringComparison.Ordinal));
            Assert.IsFalse(seen.Contains(OldPassword, StringComparison.Ordinal));
        }
    }

    private Pc NewPc()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-reset", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        roots.Add(root);
        return Pc.Create(root, authServer, syncServer, () => now);
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    private sealed class Pc : IAsyncDisposable
    {
        private readonly TestDatabase fixture;
        private readonly SqliteNoteRepository repository;
        private readonly SyncEngine engine;

        private Pc(
            TestDatabase fixture,
            SqliteNoteRepository repository,
            SqliteSyncStore store,
            SyncEngine engine,
            AccountService accounts)
        {
            this.fixture = fixture;
            this.repository = repository;
            this.engine = engine;
            Store = store;
            Accounts = accounts;
        }

        internal SqliteSyncStore Store { get; }

        internal AccountService Accounts { get; }

        internal static Pc Create(
            string root,
            FakeAuthServer authServer,
            InMemorySyncServer syncServer,
            Func<DateTimeOffset> utcNow)
        {
            TestDatabase fixture = TestDatabase.CreateIn(root);
            fixture.Database.Initialize();

            var repository = new SqliteNoteRepository(fixture.Database, utcNow);
            var store = new SqliteSyncStore(fixture.Database, utcNow);
            var sessions = new DpapiSyncSessionStore(root);
            var accounts = new AccountService(authServer, Crypto, sessions, store, () => "Test PC");
            var engine = new SyncEngine(
                syncServer.ClientFor(root),
                Crypto,
                store,
                utcNow,
                new FileSystemConflictSink(root));

            return new Pc(fixture, repository, store, engine, accounts);
        }

        internal async ValueTask<SyncReport> Sync()
        {
            ResumedSession resumed = await Accounts.ResumeAsync();
            if (resumed.Session is not { } session)
            {
                return SyncReport.For(
                    resumed.State == ResumeState.Locked ? SyncOutcome.Locked : SyncOutcome.SignedOut);
            }

            using (session.DataKey)
            {
                return await engine.SyncAsync(session);
            }
        }

        internal async Task<NoteId> AddNote(int suffix, string title, string body)
        {
            NoteId id = NoteId.Create(Id(suffix)).Value;
            await repository.CreateNoteAsync(Date, default, id);
            await repository.SaveNoteAsync(
                new NoteSaveRequest(id, Date, title, body, 0, IsNew: false, HasCustomTitle: true));
            return id;
        }

        internal async Task<IReadOnlyList<Note>> Notes()
        {
            NoteSet set = await repository.GetDayWorkspaceAsync(Date);
            return [.. set.Notes.Where(static note => !note.IsProjection)];
        }

        public ValueTask DisposeAsync() => fixture.DisposeAsync();
    }
}
