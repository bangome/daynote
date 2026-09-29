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
/// <remarks>
/// A single-root composition (no profile host): each simulated PC's root is taken to be the account's
/// own folder, which is how the service behaved before docs/PROFILES.md and still behaves without a
/// host. What changes with profiles — sign-in handing off instead of enrolling, sign-out choices,
/// deletion keeping or removing the notes — is <see cref="ProfileLifecycleTests"/>.
/// </remarks>
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

        string email = (await pc.Accounts.SignInAsync()).Email;

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
        return Pc.Create(root, authServer, syncServer, () => now, freshRoot, apple);
    }

    /// <summary>Set before <see cref="NewPc"/> to give that PC a Sign in with Apple sheet.</summary>
    private FakeAppleIdentityProvider? apple;

    // ---- deleting the account ----

    [TestMethod]
    public async Task Deleting_the_account_removes_it_and_signs_out_but_keeps_the_notes_here()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await pc.AddNote(1, "회의록", "분기 계획 논의");
        await pc.Sync();

        await pc.Accounts.DeleteAccountAsync();

        Assert.AreEqual(0, authServer.AccountCount, "The server still has the account.");
        Assert.IsFalse((await pc.Store.ReadStateAsync()).IsSignedIn);
        Assert.IsNull(await pc.Sessions.LoadAsync(), "The session outlived the account.");
        Assert.AreEqual("분기 계획 논의", (await pc.Notes()).Single().Body, "Deleting the cloud account took the local note.");
    }

    [TestMethod]
    public async Task Deleting_renews_the_session_first_so_a_stale_token_cannot_block_it()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        int refreshesBefore = authServer.RefreshCalls;

        // Long enough for the stored access token to have expired.
        now = now.AddHours(3);
        await pc.Accounts.DeleteAccountAsync();

        Assert.AreEqual(refreshesBefore + 1, authServer.RefreshCalls);
        Assert.AreEqual(1, authServer.DeleteCalls);
        Assert.AreEqual(0, authServer.AccountCount);
    }

    [TestMethod]
    public async Task A_running_subscription_stops_the_deletion_and_leaves_the_device_signed_in()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        authServer.SubscriptionStillActive = true;

        var refused = await Assert.ThrowsExactlyAsync<AccountException>(() => pc.Accounts.DeleteAccountAsync().AsTask());

        Assert.AreEqual(AccountFailure.SubscriptionStillActive, refused.Failure);
        Assert.AreEqual(1, authServer.AccountCount);
        Assert.IsTrue((await pc.Store.ReadStateAsync()).IsSignedIn, "A refused deletion signed the device out anyway.");
    }

    [TestMethod]
    public async Task A_refused_deletion_can_be_retried_once_the_subscription_is_cancelled()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        authServer.SubscriptionStillActive = true;
        await Assert.ThrowsExactlyAsync<AccountException>(() => pc.Accounts.DeleteAccountAsync().AsTask());

        // The refused attempt rotated the refresh token. Had the new one been thrown away, the retry
        // would present a spent token, the server would read it as theft, and the user would be
        // signed out instead of deleted.
        authServer.SubscriptionStillActive = false;
        AccountDeletion outcome = await pc.Accounts.DeleteAccountAsync();

        Assert.AreEqual(AccountDeletion.Deleted, outcome);
        Assert.AreEqual(0, authServer.AccountCount);
    }

    [TestMethod]
    public async Task A_session_the_server_no_longer_knows_signs_out_instead_of_sticking()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();

        // A delete that went through on the server while its answer was lost on the way back.
        await using (Pc elsewhere = NewPc(freshRoot: true))
        {
            await elsewhere.Accounts.SignInAsync();
            await elsewhere.Accounts.DeleteAccountAsync();
        }

        AccountDeletion outcome = await pc.Accounts.DeleteAccountAsync();

        Assert.AreEqual(AccountDeletion.SessionAlreadyGone, outcome);
        Assert.IsFalse((await pc.Store.ReadStateAsync()).IsSignedIn);
        Assert.IsNull(await pc.Sessions.LoadAsync());
    }

    [TestMethod]
    public async Task A_new_account_after_deletion_gets_the_local_notes_pushed_again()
    {
        await using Pc pc = NewPc();
        await pc.Accounts.SignInAsync();
        await pc.AddNote(1, "남은 노트", "이 기기에 있던 내용");
        await pc.Sync();
        await pc.Accounts.DeleteAccountAsync();
        syncServer.ForgetEverything();

        // Signing in again makes a brand-new account on the server; what is on this device goes up.
        await pc.Accounts.SignInAsync();
        await pc.Sync();
        await using Pc other = NewPc(freshRoot: true);
        await other.Accounts.SignInAsync();
        await other.Sync();

        Assert.AreEqual("이 기기에 있던 내용", (await other.Notes()).Single().Body);
    }

    // ---- Sign in with Apple ----

    [TestMethod]
    public async Task Apple_sign_in_sends_the_raw_nonce_whose_hash_went_to_Apple()
    {
        apple = new FakeAppleIdentityProvider(authServer, "apple-sub-alice", "relay@privaterelay.appleid.com");
        await using Pc pc = NewPc();
        Assert.IsTrue(pc.Accounts.CanSignInWithApple);

        string email = (await pc.Accounts.SignInWithAppleAsync()).Email;

        Assert.AreEqual("relay@privaterelay.appleid.com", email);
        string raw = authServer.AppleNonces.Single();
        string hashed = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        Assert.AreEqual(hashed, apple.HashedNonces.Single());
        Assert.IsTrue((await pc.Store.ReadStateAsync()).IsSignedIn);
    }

    [TestMethod]
    public async Task Every_Apple_attempt_gets_its_own_nonce()
    {
        apple = new FakeAppleIdentityProvider(authServer, "apple-sub-alice", "a@example.test");
        await using Pc pc = NewPc();

        await pc.Accounts.SignInWithAppleAsync();
        await pc.Accounts.SignOutAsync();
        await pc.Accounts.SignInWithAppleAsync();

        Assert.AreEqual(2, authServer.AppleNonces.Distinct().Count());
        Assert.AreEqual(1, authServer.AccountCount, "The second Apple sign-in made another account.");
    }

    [TestMethod]
    public async Task Apple_notes_sync_like_any_other_account()
    {
        apple = new FakeAppleIdentityProvider(authServer, "apple-sub-alice", "a@example.test");
        await using Pc phone = NewPc();
        await phone.Accounts.SignInWithAppleAsync();
        await phone.AddNote(1, "아이폰", "Apple 계정으로 쓴 노트");
        await phone.Sync();

        await using Pc tablet = NewPc(freshRoot: true);
        await tablet.Accounts.SignInWithAppleAsync();
        await tablet.Sync();

        Assert.AreEqual("Apple 계정으로 쓴 노트", (await tablet.Notes()).Single().Body);
    }

    [TestMethod]
    public void Without_an_Apple_sheet_the_option_is_absent()
    {
        Pc pc = NewPc();
        Assert.IsFalse(pc.Accounts.CanSignInWithApple);
        pc.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
            bool deleteRoot,
            IAppleIdentityProvider? apple = null)
        {
            TestDatabase fixture = TestDatabase.CreateIn(root);
            fixture.Database.Initialize();

            var repository = new SqliteNoteRepository(fixture.Database, utcNow);
            var store = new SqliteSyncStore(fixture.Database, utcNow);
            var sessions = new DpapiSyncSessionStore(root);
            // Every simulated PC signs in as the same Google account, which is what makes "a fresh
            // data root gets the notes back" a real test rather than two unrelated accounts.
            var identity = new FakeIdentityProvider(authServer, Subject, Email);
            var accounts = new AccountService(authServer, identity, Crypto, sessions, store, () => "Test PC", apple);
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

        public async ValueTask<FilePushResult> PushFilesAsync(
            FilePushRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            return await inner.PushFilesAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask UploadAssetAsync(
            string blindedKey,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
        {
            _ = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            await inner.UploadAssetAsync(blindedKey, body, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<byte[]?> DownloadAssetAsync(
            string blindedKey,
            CancellationToken cancellationToken = default)
        {
            _ = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            return await inner.DownloadAssetAsync(blindedKey, cancellationToken).ConfigureAwait(false);
        }
    }
}
