using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Profiles;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// One local store per account (docs/PROFILES.md §10), end to end: real databases in real profile
/// folders, the real hand-off and return, and a sync server per account. A "relaunch" disposes the
/// composition and builds it again over whatever <c>profile.json</c> now names, which is what the
/// desktop hosts do on a switch.
/// </summary>
[TestClass]
public sealed class ProfileLifecycleTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-09-29").Value;
    private static readonly AesGcmSyncCrypto Crypto = new();

    private static readonly (string Subject, string Email) Alice = ("google-sub-alice", "alice@example.test");
    private static readonly (string Subject, string Email) Bob = ("apple-sub-bob", "bob@example.test");

    private readonly Dictionary<string, InMemorySyncServer> servers = new(StringComparer.Ordinal);
    private readonly List<string> roots = [];
    private DateTimeOffset now;
    private FakeAuthServer authServer = null!;

    [TestInitialize]
    public void Setup()
    {
        now = DateTimeOffset.Parse("2026-09-29T09:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);
        authServer = new FakeAuthServer(() => now);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (string root in roots.Where(Directory.Exists))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Signing_in_with_Keep_uploads_nothing_from_this_device()
    {
        // The 62-copy bug: signing in used to enroll every note on the device into the new account.
        await using Device phone = NewDevice();
        await phone.AddNote(1, "하나", "로컬 노트");
        await phone.AddNote(2, "둘", "로컬 노트");
        await phone.AddNote(3, "셋", "로컬 노트");

        SignInResult result = await phone.SignInAs(Alice);

        Assert.IsTrue(result.NeedsHandOff, "A sign-in from the local profile was saved into the local database.");
        AccountHandOff handOff = result.HandOff!;
        Assert.IsTrue(handOff.AsksToMove);
        Assert.AreEqual(3, handOff.LocalContent.Notes);
        string alice = handOff.UserId;

        await phone.Accounts.CompleteHandOffAsync(handOff, moveLocalNotes: false);
        await phone.RelaunchAsync();

        Assert.AreEqual(alice, phone.ProfileId);
        Assert.AreEqual(0, (await phone.Notes()).Count, "Keep brought the local notes into the account.");
        SyncReport report = await phone.Sync();
        Assert.AreEqual(SyncOutcome.Completed, report.Outcome);
        Assert.AreEqual(0, report.Pushed);
        Assert.AreEqual(0, ServerFor(alice).StoredBlobs.Count, "The account received notes the user kept local.");
        Assert.AreEqual(3L, CountNotes(phone.BaseRoot), "Keep lost the local notes.");
    }

    [TestMethod]
    public async Task Signing_in_with_Move_brings_the_notes_to_another_device_and_out_of_the_local_profile()
    {
        await using Device phone = NewDevice();
        await phone.AddNote(1, "회의록", "분기 계획 논의");
        await phone.SetTags(1, ["프로젝트"]);
        await phone.AddNote(2, "장보기", "우유");

        SignInResult result = await phone.SignInAs(Alice);
        await phone.Accounts.CompleteHandOffAsync(result.HandOff!, moveLocalNotes: true);
        await phone.RelaunchAsync();

        Assert.AreEqual(2, (await phone.Notes()).Count, "The moved notes are not in the account's profile.");
        Assert.AreEqual(0L, CountNotes(phone.BaseRoot), "Move left the notes in two places.");
        Assert.AreEqual(0L, Count(phone.BaseRoot, "SELECT COUNT(*) FROM sync_tombstones;"), "Moving left tombstones behind.");
        Assert.AreEqual(2, (await phone.Sync()).Pushed);

        await using Device tablet = NewDevice();
        SignInResult other = await tablet.SignInAs(Alice);
        Assert.IsFalse(other.HandOff!.AsksToMove, "A device with nothing of its own asked a question.");
        await tablet.Accounts.CompleteHandOffAsync(other.HandOff, moveLocalNotes: false);
        await tablet.RelaunchAsync();
        await tablet.Sync();

        Note meeting = (await tablet.Notes()).Single(note => note.Title == "회의록");
        Assert.AreEqual("분기 계획 논의", meeting.Body);
        CollectionAssert.AreEqual(new[] { "프로젝트" }, meeting.Tags.ToArray());
    }

    [TestMethod]
    public async Task Signing_out_and_into_another_account_gives_two_folders_that_share_nothing()
    {
        await using Device phone = NewDevice();
        await phone.AddNote(1, "Google", "구글 계정의 노트");
        string alice = await phone.SignInAndSwitch(Alice, move: true);
        await phone.Sync();

        Assert.IsTrue(await phone.Accounts.SignOutAsync(), "Signing out of an account profile did not switch.");
        await phone.RelaunchAsync();
        Assert.AreEqual(ProfileStore.LocalProfileId, phone.ProfileId);
        Assert.AreEqual(0, (await phone.Notes()).Count);

        await phone.AddNote(5, "Local", "로그아웃 상태에서 쓴 노트");
        SignInResult result = await phone.SignInAs(Bob);
        Assert.AreEqual(1, result.HandOff!.LocalContent.Notes);
        string bob = result.HandOff.UserId;
        await phone.Accounts.CompleteHandOffAsync(result.HandOff, moveLocalNotes: false);
        await phone.RelaunchAsync();
        await phone.Sync();

        Assert.AreEqual(0, ServerFor(bob).StoredBlobs.Count, "The second account received notes it was never given.");
        CollectionAssert.AreEquivalent(new[] { alice, bob }, new ProfileStore(phone.BaseRoot).ListAccounts().ToArray());
        HashSet<string> alicesNotes = NoteIds(new ProfileStore(phone.BaseRoot).AccountFolder(alice));
        HashSet<string> bobsNotes = NoteIds(new ProfileStore(phone.BaseRoot).AccountFolder(bob));
        Assert.AreEqual(1, alicesNotes.Count);
        Assert.IsFalse(alicesNotes.Overlaps(bobsNotes), "A note is in both accounts' databases.");
    }

    [TestMethod]
    public async Task Signing_back_in_finds_the_notes_and_cursor_it_left()
    {
        await using Device phone = NewDevice();
        string alice = await phone.SignInAndSwitch(Alice, move: false);
        await phone.AddNote(1, "Title", "body");
        await phone.Sync();
        long cursor = (await phone.Store.ReadStateAsync()).ServerCursor;
        Assert.IsGreaterThan(0L, cursor);

        await phone.Accounts.SignOutAsync();
        await phone.RelaunchAsync();
        Assert.AreEqual(alice, await phone.SignInAndSwitch(Alice, move: false));

        Assert.AreEqual("body", (await phone.Notes()).Single().Body, "The notes were not there before any sync.");
        SyncStateSnapshot state = await phone.Store.ReadStateAsync();
        Assert.AreEqual(alice, state.UserId);
        Assert.AreEqual(cursor, state.ServerCursor, "Signing back in started the pull over.");
        SyncReport report = await phone.Sync();
        Assert.AreEqual(SyncOutcome.Completed, report.Outcome);
        Assert.AreEqual(0, report.Pulled, "Signing back in downloaded again what the device already had.");
    }

    [TestMethod]
    public async Task Signing_out_with_Remove_deletes_the_folder_and_counts_what_never_synced()
    {
        await using Device phone = NewDevice();
        string alice = await phone.SignInAndSwitch(Alice, move: false);
        await phone.AddNote(1, "Synced", "body");
        await phone.Sync();
        await phone.AddNote(2, "Not synced", "body");

        Assert.AreEqual(1, await phone.Accounts.CountPendingChangesAsync());

        Assert.IsTrue(await phone.Accounts.SignOutAsync(removeFromDevice: true));
        await phone.RelaunchAsync();

        Assert.AreEqual(ProfileStore.LocalProfileId, phone.ProfileId);
        Assert.IsFalse(Directory.Exists(new ProfileStore(phone.BaseRoot).AccountFolder(alice)), "Remove left the folder.");
        Assert.AreEqual(0, new ProfileStore(phone.BaseRoot).ListAccounts().Count);
    }

    [TestMethod]
    public async Task Deleting_and_keeping_the_notes_makes_them_local_notes()
    {
        await using Device phone = NewDevice();
        string alice = await phone.SignInAndSwitch(Alice, move: false);
        await phone.AddNote(1, "회의록", "남길 노트");
        await phone.Sync();

        Assert.AreEqual(AccountDeletion.Deleted, await phone.Accounts.DeleteAccountAsync());
        Assert.IsTrue(await phone.Accounts.FinishDeletionAsync(keepNotesAsLocal: true));
        await phone.RelaunchAsync();

        Assert.AreEqual(ProfileStore.LocalProfileId, phone.ProfileId);
        Assert.AreEqual("남길 노트", (await phone.Notes()).Single().Body);
        Assert.IsFalse(Directory.Exists(new ProfileStore(phone.BaseRoot).AccountFolder(alice)));
        Assert.AreEqual(0, authServer.AccountCount);
    }

    [TestMethod]
    public async Task Deleting_and_removing_the_notes_leaves_nothing_of_the_account()
    {
        await using Device phone = NewDevice();
        string alice = await phone.SignInAndSwitch(Alice, move: false);
        await phone.AddNote(1, "회의록", "지울 노트");
        await phone.Sync();

        await phone.Accounts.DeleteAccountAsync();
        Assert.IsTrue(await phone.Accounts.FinishDeletionAsync(keepNotesAsLocal: false));
        await phone.RelaunchAsync();

        Assert.AreEqual(0, (await phone.Notes()).Count);
        Assert.IsFalse(Directory.Exists(new ProfileStore(phone.BaseRoot).AccountFolder(alice)));
    }

    [TestMethod]
    public async Task A_session_the_server_already_forgot_offers_the_same_choice()
    {
        await using Device phone = NewDevice();
        await phone.SignInAndSwitch(Alice, move: false);
        await phone.AddNote(1, "회의록", "남길 노트");
        await phone.Sync();

        await using (Device elsewhere = NewDevice())
        {
            await elsewhere.SignInAndSwitch(Alice, move: false);
            await elsewhere.Accounts.DeleteAccountAsync();
        }

        Assert.AreEqual(AccountDeletion.SessionAlreadyGone, await phone.Accounts.DeleteAccountAsync());
        Assert.IsTrue(await phone.Accounts.FinishDeletionAsync(keepNotesAsLocal: true));
        await phone.RelaunchAsync();

        Assert.AreEqual("남길 노트", (await phone.Notes()).Single().Body);
    }

    [TestMethod]
    public async Task A_database_owned_by_another_account_refuses_to_sync_and_is_never_relabelled()
    {
        await using Device phone = NewDevice();
        string alice = await phone.SignInAndSwitch(Alice, move: false);
        await phone.AddNote(1, "Title", "body");
        string stranger = Guid.NewGuid().ToString("D");
        await Execute(phone.Database, "UPDATE sync_state SET user_id=$u WHERE id=1;", ("$u", stranger));

        Assert.AreEqual(ResumeState.SignInRequired, (await phone.Accounts.ResumeAsync()).State);
        Assert.AreEqual(SyncOutcome.SignInRequired, (await phone.Sync()).Outcome);
        Assert.AreEqual(0, ServerFor(alice).StoredBlobs.Count, "A mismatched database pushed its notes.");

        // Signing in again as the folder's account does not adopt the stranger's database either.
        var refused = await Assert.ThrowsExactlyAsync<AccountException>(async () => await phone.SignInAs(Alice));
        Assert.AreEqual(AccountFailure.InvalidCredentials, refused.Failure);
        Assert.AreEqual(stranger, (await phone.Store.ReadStateAsync()).UserId);
    }

    [TestMethod]
    public async Task Another_accounts_session_in_a_folder_refuses_to_sync()
    {
        await using Device phone = NewDevice();
        await phone.SignInAndSwitch(Alice, move: false);
        SyncCredentials stored = (await phone.Sessions.LoadAsync())!;
        using (stored)
        {
            await phone.Sessions.SaveAsync(stored with { UserId = Guid.NewGuid().ToString("D") });
        }

        Assert.AreEqual(ResumeState.SignInRequired, (await phone.Accounts.ResumeAsync()).State);
    }

    [TestMethod]
    public async Task The_local_profile_never_holds_a_session_and_never_syncs()
    {
        await using Device phone = NewDevice();
        await phone.AddNote(1, "Title", "body");

        SignInResult result = await phone.SignInAs(Alice);
        Assert.IsFalse(File.Exists(Path.Combine(phone.BaseRoot, "credentials.dat")), "The session landed in the local profile.");
        Assert.IsFalse((await phone.Store.ReadStateAsync()).IsSignedIn);
        result.HandOff!.Dispose();

        Assert.AreEqual(SyncOutcome.SignedOut, (await phone.Sync()).Outcome);
    }

    [TestMethod]
    public async Task Device_settings_follow_the_user_into_a_new_account_profile()
    {
        await using Device phone = NewDevice();
        await Execute(
            phone.Database,
            "INSERT INTO settings(key,value,updated_utc) VALUES('ui.language','en',$u);",
            ("$u", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));

        await phone.SignInAndSwitch(Alice, move: false);

        Assert.AreEqual(1L, Count(phone.Folder, "SELECT COUNT(*) FROM settings WHERE key='ui.language' AND value='en';"));
    }

    private Device NewDevice()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-profiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        roots.Add(root);
        return Device.Start(this, root);
    }

    private InMemorySyncServer ServerFor(string userId)
    {
        if (!servers.TryGetValue(userId, out InMemorySyncServer? server))
        {
            server = new InMemorySyncServer(() => now);
            servers[userId] = server;
        }

        return server;
    }

    private static long CountNotes(string folder) => Count(folder, "SELECT COUNT(*) FROM notes;");

    private static long Count(string folder, string sql)
    {
        using SqliteConnection connection = new SqliteConnectionFactory(
            new SqliteDatabaseOptions(Path.Combine(folder, "daynote.db"))).OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static HashSet<string> NoteIds(string folder)
    {
        using SqliteConnection connection = new SqliteConnectionFactory(
            new SqliteDatabaseOptions(Path.Combine(folder, "daynote.db"))).OpenReadConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM notes;";
        using SqliteDataReader reader = command.ExecuteReader();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static Task Execute(SqliteDatabase database, string sql, params (string Name, object Value)[] parameters) =>
        database.WriteAsync(
            (connection, transaction, _) =>
            {
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach ((string name, object value) in parameters)
                {
                    command.Parameters.AddWithValue(name, value);
                }

                return command.ExecuteNonQuery();
            }).AsTask();

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}");

    /// <summary>
    /// One device: a base root, and the composition the host builds over its active profile. Only the
    /// pieces the account flows touch are here — database, notes, sync store, session store, the
    /// profile host and the account service.
    /// </summary>
    private sealed class Device : IAsyncDisposable
    {
        private readonly ProfileLifecycleTests test;
        private TestDatabase? owned;

        private Device(ProfileLifecycleTests test, string baseRoot)
        {
            this.test = test;
            BaseRoot = baseRoot;
        }

        internal string BaseRoot { get; }

        internal string Folder { get; private set; } = string.Empty;

        internal string ProfileId => Host.CurrentProfileId;

        internal SqliteDatabase Database => owned!.Database;

        internal SqliteNoteRepository Repository { get; private set; } = null!;

        internal SqliteSyncStore Store { get; private set; } = null!;

        internal ISyncSessionStore Sessions { get; private set; } = null!;

        internal ProfileHost Host { get; private set; } = null!;

        /// <summary>The account service for everything but a sign-in, which names its identity.</summary>
        internal AccountService Accounts { get; private set; } = null!;

        internal static Device Start(ProfileLifecycleTests test, string baseRoot)
        {
            var device = new Device(test, baseRoot);
            device.Open();
            return device;
        }

        internal Task<SignInResult> SignInAs((string Subject, string Email) who) =>
            AccountsAs(who).SignInAsync().AsTask();

        /// <summary>Signs in, answers the question, and relaunches over the account. Returns its id.</summary>
        internal async Task<string> SignInAndSwitch((string Subject, string Email) who, bool move)
        {
            SignInResult result = await SignInAs(who);
            AccountHandOff handOff = result.HandOff
                ?? throw new AssertFailedException("The sign-in did not hand off to an account profile.");
            string userId = handOff.UserId;
            await Accounts.CompleteHandOffAsync(handOff, move);
            await RelaunchAsync();
            Assert.AreEqual(userId, ProfileId);
            return userId;
        }

        /// <summary>What a desktop host does on a switch: close everything, start over the pointer.</summary>
        internal async Task RelaunchAsync()
        {
            await owned!.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Open();
            await Accounts.PrepareProfileAsync();
        }

        internal async ValueTask<SyncReport> Sync()
        {
            ResumedSession resumed = await Accounts.ResumeAsync();
            if (resumed.Session is not { } session)
            {
                return SyncReport.For(resumed.State switch
                {
                    ResumeState.KeyMissing or ResumeState.Locked => SyncOutcome.Locked,
                    ResumeState.SignInRequired => SyncOutcome.SignInRequired,
                    _ => SyncOutcome.SignedOut,
                });
            }

            using (session.DataKey)
            {
                var engine = new SyncEngine(
                    test.ServerFor(session.UserId).ClientFor(BaseRoot),
                    Crypto,
                    Store,
                    () => test.now,
                    new FileSystemConflictSink(Folder));
                return await engine.SyncAsync(session);
            }
        }

        internal async Task AddNote(int suffix, string title, string body)
        {
            NoteId id = NoteId.Create(Id(suffix)).Value;
            await Repository.CreateNoteAsync(Date, default, id);
            await Repository.SaveNoteAsync(new NoteSaveRequest(id, Date, title, body, 0, IsNew: false, HasCustomTitle: true));
        }

        internal Task SetTags(int suffix, IReadOnlyList<string> tags) =>
            Repository.SetTagsAsync(Date, NoteId.Create(Id(suffix)).Value, tags).AsTask();

        internal async Task<IReadOnlyList<Note>> Notes()
        {
            NoteSet set = await Repository.GetDayWorkspaceAsync(Date);
            return [.. set.Notes.Where(static note => !note.IsProjection)];
        }

        public async ValueTask DisposeAsync()
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
                owned = null;
            }
        }

        private void Open()
        {
            var profiles = new ProfileStore(BaseRoot);
            profiles.MigrateLegacyLayout();
            Folder = profiles.ResolveActiveFolder();
            owned = TestDatabase.CreateIn(Folder);
            owned.Database.Initialize();
            Repository = new SqliteNoteRepository(owned.Database, () => test.now);
            Store = new SqliteSyncStore(owned.Database, () => test.now);
            Sessions = SessionStoreFor(Folder);
            Host = new ProfileHost(profiles, Folder, owned.Database, SessionStoreFor, () => test.now);
            Accounts = AccountsAs(Alice);
        }

        private AccountService AccountsAs((string Subject, string Email) who) => new(
            test.authServer,
            new FakeIdentityProvider(test.authServer, who.Subject, who.Email),
            Crypto,
            Sessions,
            Store,
            () => "Test device",
            profiles: Host);

        /// <summary>The session store a profile folder gets, the same kind for every folder.</summary>
        private static ISyncSessionStore SessionStoreFor(string root) => new DpapiSyncSessionStore(root);
    }
}
