using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// The account acceptance test: sign in with Google, sync, sign out, then sign in again on a data
/// root that has never seen this account and get the notes back.
/// </summary>
[TestClass]
public sealed class AccountLifecycleTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-08-20").Value;
    private static readonly AesGcmSyncCrypto Crypto = new();
    private const string Email = "alice@example.test";

    /// <summary>The Google `sub`. Identity is keyed on this, not on the address.</summary>
    private const string Subject = "google-sub-alice";

    private DateTimeOffset now;
    private FakeAuthServer authServer = null!;
    private InMemorySyncServer syncServer = null!;
    private string dataRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        now = DateTimeOffset.Parse(
            "2026-08-20T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        authServer = new FakeAuthServer(() => now);
        syncServer = new InMemorySyncServer(() => now);
        dataRoot = Path.Combine(Path.GetTempPath(), "daynote-account", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Signing_in_creates_the_account_and_signs_this_device_in()
    {
        await using Pc pc = NewPc();

        string email = await pc.Accounts.SignInAsync();

        Assert.AreEqual(Email, email);
        Assert.AreEqual(1, authServer.AccountCount);
        Assert.IsTrue((await pc.Store.ReadStateAsync()).IsSignedIn);
    }

    [TestMethod]
    public async Task Content_survives_sign_out_and_a_fresh_sign_in_on_an_empty_data_root()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await pc.AddNote(1, "회의록", "분기 계획 논의");
        await pc.SetTags(1, ["프로젝트"]);
        await pc.Sync();

        await pc.Accounts.SignOutAsync();
        Assert.IsFalse((await pc.Store.ReadStateAsync()).IsSignedIn);

        // A different PC, or the same one after a reinstall: nothing local, no credentials file.
        await using Pc reinstalled = NewPc(freshRoot: true);
        Assert.AreEqual(0, (await reinstalled.Notes()).Count);

        await reinstalled.Accounts.SignInAsync();
        await reinstalled.Sync();

        Note restored = (await reinstalled.Notes()).Single();
        Assert.AreEqual("회의록", restored.Title);
        Assert.AreEqual("분기 계획 논의", restored.Body);
        CollectionAssert.AreEqual(new[] { "프로젝트" }, restored.Tags.ToArray());
    }

    [TestMethod]
    public async Task Signing_out_discards_the_stored_key_material()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        Assert.IsTrue(File.Exists(Path.Combine(dataRoot, "credentials.dat")));

        await pc.Accounts.SignOutAsync();

        // Explicit sign-out is the one path allowed to destroy the cached data key.
        Assert.IsFalse(File.Exists(Path.Combine(dataRoot, "credentials.dat")));
        Assert.IsNull(await pc.Sessions.LoadAsync());
    }

    [TestMethod]
    public async Task Signing_in_a_second_time_reuses_the_account_rather_than_making_another()
    {
        // The second sign-in redeems a different Google code for the same subject. Creating a second
        // account there would strand every note written under the first one.
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await pc.Accounts.SignOutAsync();

        await pc.Accounts.SignInAsync();

        Assert.AreEqual(1, authServer.AccountCount);
        Assert.AreEqual(2, authServer.SignInCalls);
    }

    [TestMethod]
    public async Task Closing_the_browser_leaves_the_device_signed_out()
    {
        await using Pc pc = NewPc();
        pc.Identity.Cancel = true;

        var failure = await Assert.ThrowsExactlyAsync<AccountException>(
            async () => await pc.Accounts.SignInAsync());

        Assert.AreEqual(AccountFailure.SignInCancelled, failure.Failure);
        Assert.IsNull(await pc.Sessions.LoadAsync());
        Assert.AreEqual(0, authServer.SignInCalls);
    }

    [TestMethod]
    public async Task A_session_that_lost_its_data_key_is_restored_without_the_browser()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();

        // What a restored profile looks like: the tokens survived, the key did not.
        SyncCredentials stored = (await pc.Sessions.LoadAsync())!;
        using (stored)
        {
            await pc.Sessions.SaveAsync(stored with { DataKey = null });
        }

        Assert.AreEqual(ResumeState.KeyMissing, (await pc.Accounts.ResumeAsync()).State);

        await pc.Accounts.RestoreDataKeyAsync();

        ResumedSession resumed = await pc.Accounts.ResumeAsync();
        Assert.AreEqual(ResumeState.Ready, resumed.State);
        resumed.Session!.DataKey.Dispose();
        // No second trip through the browser: the tokens were enough.
        Assert.AreEqual(1, pc.Identity.AuthorizeCalls);
    }

    [TestMethod]
    public async Task Content_written_before_signing_in_is_pushed_once_the_account_exists()
    {
        await using Pc pc = NewPc();
        await pc.AddNote(1, "Written months ago", "body");

        await pc.Accounts.SignInAsync();
        await pc.Sync();

        await using Pc other = NewPc(freshRoot: true);
        await other.Accounts.SignInAsync();
        await other.Sync();

        Assert.AreEqual("Written months ago", (await other.Notes()).Single().Title);
    }

    [TestMethod]
    public async Task Note_content_never_leaves_this_PC_in_the_clear()
    {
        // The server holds the key, so it CAN read what it stores — but the blob must still travel
        // and rest encrypted, or the ciphertext would be decoration.
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await pc.AddNote(1, "Title", "SECRET-BODY");
        await pc.Sync();

        foreach (string seen in authServer.EverythingReceived.Concat(syncServer.StoredBlobs))
        {
            Assert.IsFalse(
                seen.Contains("SECRET-BODY", StringComparison.Ordinal),
                "Note content reached the server in the clear.");
        }
    }

    [TestMethod]
    public async Task An_expired_access_token_is_renewed_without_asking_the_user()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await pc.AddNote(1, "Title", "body");

        // Past the 15-minute access-token lifetime, but well inside the refresh token's.
        now = now.AddHours(2);
        SyncReport report = await pc.Sync();

        Assert.AreEqual(SyncOutcome.Completed, report.Outcome);
        Assert.IsTrue(authServer.RefreshCalls > 0);
        Assert.AreEqual(1, syncServer.StoredBlobs.Count);
    }

    [TestMethod]
    public async Task A_credentials_file_that_cannot_be_read_reads_as_signed_out()
    {
        // What a copied profile or a restored machine image looks like. It must not be an error.
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await File.WriteAllTextAsync(Path.Combine(dataRoot, "credentials.dat"), "not a DPAPI blob");

        Assert.IsNull(await pc.Sessions.LoadAsync());
    }

    [TestMethod]
    public async Task Conflicting_versions_land_in_the_conflicts_folder_as_plain_text()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        NoteId id = await pc.AddNote(1, "Title", "Mine");
        await pc.Sync();

        // Another device wrote a newer version of the same note.
        await using Pc other = NewPc(freshRoot: true);
        await other.Accounts.SignInAsync();
        await other.Sync();
        now = now.AddMinutes(5);
        await other.EditNote(id, "Title", "Theirs");
        await other.Sync();

        now = now.AddMinutes(1);
        await pc.Sync();

        Assert.AreEqual("Theirs", (await pc.Notes()).Single().Body);
        string[] saved = Directory.GetFiles(Path.Combine(pc.DataRoot, "conflicts"), "*.txt");
        Assert.AreEqual(1, saved.Length);
        string text = await File.ReadAllTextAsync(saved[0]);
        StringAssert.Contains(text, "Mine");
    }

    [TestMethod]
    public async Task The_backup_zip_does_not_contain_the_credentials_file()
    {
        // A backup that carried credentials.dat would export the data key in a file the user is told
        // to copy onto other media.
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await pc.AddNote(1, "Title", "body");

        string zipPath = Path.Combine(dataRoot, "backup.zip");
        var backup = new Daynote.Infrastructure.Backup.BackupService(
            pc.DataRoot,
            Path.Combine(pc.DataRoot, "daynote.db"));
        await backup.CreateBackupAsync(zipPath);

        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        Assert.IsFalse(
            archive.Entries.Any(entry =>
                entry.FullName.Contains("credentials", StringComparison.OrdinalIgnoreCase)),
            "The backup archive contains the credentials file.");
    }

    private Pc NewPc(bool freshRoot = false)
    {
        string root = freshRoot
            ? Path.Combine(Path.GetTempPath(), "daynote-account", Guid.NewGuid().ToString("N"))
            : dataRoot;
        Directory.CreateDirectory(root);
        return Pc.Create(root, authServer, syncServer, () => now, freshRoot);
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    /// <summary>One machine: data root, database, repository, store, engine, and account service.</summary>
    private sealed class Pc : IAsyncDisposable
    {
        private readonly TestDatabase? owned;
        private readonly string root;
        private readonly bool deleteRoot;
        private readonly SqliteNoteRepository repository;
        private readonly SyncEngine engine;
        private readonly Func<DateTimeOffset> utcNow;

        private Pc(
            TestDatabase owned,
            string root,
            bool deleteRoot,
            SqliteNoteRepository repository,
            SqliteSyncStore store,
            SyncEngine engine,
            AccountService accounts,
            ISyncSessionStore sessions,
            FakeIdentityProvider identity,
            Func<DateTimeOffset> utcNow)
        {
            this.owned = owned;
            this.root = root;
            this.deleteRoot = deleteRoot;
            this.repository = repository;
            this.engine = engine;
            this.utcNow = utcNow;
            Store = store;
            Accounts = accounts;
            Sessions = sessions;
            Identity = identity;
        }

        internal SqliteSyncStore Store { get; }

        internal AccountService Accounts { get; }

        internal ISyncSessionStore Sessions { get; }

        internal FakeIdentityProvider Identity { get; }

        internal string DataRoot => root;

        internal static Pc Create(
            string root,
            FakeAuthServer authServer,
            InMemorySyncServer syncServer,
            Func<DateTimeOffset> utcNow,
            bool deleteRoot)
        {
            TestDatabase fixture = TestDatabase.CreateIn(root);
            fixture.Database.Initialize();

            var repository = new SqliteNoteRepository(fixture.Database, utcNow);
            var store = new SqliteSyncStore(fixture.Database, utcNow);
            var sessions = new DpapiSyncSessionStore(root);
            // Every simulated PC signs in as the same Google account, which is what makes "a fresh
            // data root gets the notes back" a real test rather than two unrelated accounts.
            var identity = new FakeIdentityProvider(authServer, Subject, Email);
            var accounts = new AccountService(authServer, identity, Crypto, sessions, store, () => "Test PC");
            var tokens = new SyncTokenProvider(authServer, sessions, utcNow);
            var engine = new SyncEngine(
                new TokenAwareSyncClient(syncServer.ClientFor(root), tokens),
                Crypto,
                store,
                utcNow,
                new FileSystemConflictSink(root));

            return new Pc(
                fixture, root, deleteRoot, repository, store, engine, accounts, sessions, identity, utcNow);
        }

        internal async ValueTask<SyncReport> Sync()
        {
            ResumedSession resumed = await Accounts.ResumeAsync();
            if (resumed.Session is not { } session)
            {
                return SyncReport.For(
                    resumed.State == ResumeState.KeyMissing ? SyncOutcome.Locked : SyncOutcome.SignedOut);
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

        internal async Task EditNote(NoteId id, string title, string body)
        {
            NoteSet set = await repository.GetDayWorkspaceAsync(Date);
            _ = set;
            using var connection = owned!.Database.OpenReadConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT revision FROM notes WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            int revision = Convert.ToInt32(
                command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture);
            await repository.SaveNoteAsync(
                new NoteSaveRequest(id, Date, title, body, revision, IsNew: false, HasCustomTitle: true));
        }

        internal Task SetTags(int suffix, IReadOnlyList<string> tags) =>
            repository.SetTagsAsync(Date, NoteId.Create(Id(suffix)).Value, tags).AsTask();

        internal async Task<IReadOnlyList<Note>> Notes()
        {
            NoteSet set = await repository.GetDayWorkspaceAsync(Date);
            return [.. set.Notes.Where(static note => !note.IsProjection)];
        }

        public async ValueTask DisposeAsync()
        {
            _ = utcNow;
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }

            if (deleteRoot && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Attaches a bearer token, so token renewal is exercised on the sync path too.</summary>
    private sealed class TokenAwareSyncClient(ISyncApiClient inner, ISyncTokenProvider tokens) : ISyncApiClient
    {
        public async ValueTask<PushResult> PushAsync(
            PushRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            return await inner.PushAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<PullResult> PullAsync(
            long since,
            int limit,
            CancellationToken cancellationToken = default)
        {
            _ = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            return await inner.PullAsync(since, limit, cancellationToken).ConfigureAwait(false);
        }
    }
}
