using System.Buffers.Text;
using System.Security.Cryptography;
using Daynote.App.Account;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Profiles;
using Daynote.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Daynote.Infrastructure.Portable.Tests.Persistence.Profiles.ProfileFixture;

namespace Daynote.Infrastructure.Portable.Tests.Persistence.Profiles;

/// <summary>
/// The account panel's side of one store per account (docs/PROFILES.md §5): the Move/Keep question,
/// the sign-out choice with its second confirmation, what happens to a deleted account's notes, and
/// the switch signal the host rebuilds on. Real profile folders, a real <see cref="AccountService"/>
/// and <see cref="ProfileHost"/>; only the server and the browser are stubs.
/// </summary>
[TestClass]
public sealed class AccountProfileFlowTests
{
    private const string Subject = "google-sub-alice";

    private readonly StubServer server = new();
    private TempDirectory root = null!;

    [TestInitialize]
    public void Setup() => root = new TempDirectory();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        root.Dispose();
    }

    [TestMethod]
    public async Task Signing_in_with_notes_on_the_device_asks_before_anything_moves()
    {
        await using (Rig local = await Rig.OpenAsync(root.Path, server))
        {
            await InsertNoteAsync(local.Database, NoteId(1), "2026-09-29", "Mine", "body", 0, Utc(1), Utc(1));
            await InsertNoteAsync(local.Database, NoteId(2), "2026-09-29", "Also mine", "body", 1, Utc(1), Utc(1));

            await local.Account.SignInCommand.ExecuteAsync(null);

            Assert.IsTrue(local.Account.IsChoosingHandOff, "The question never opened.");
            Assert.IsFalse(local.Account.ShowsSignIn, "The sign-in buttons stayed up beside the question.");
            StringAssert.Contains(local.Account.HandOffMessage, "2");
            Assert.AreEqual(0, local.SwitchRequests, "The app switched before the user answered.");
            Assert.AreEqual(ProfileStore.LocalProfileId, new ProfileStore(root.Path).ReadActiveProfileId());

            await local.Account.KeepLocalNotesCommand.ExecuteAsync(null);

            Assert.AreEqual(1, local.SwitchRequests);
            Assert.IsTrue(local.Account.IsSwitchingProfile);
            Assert.IsFalse(local.Account.IsChoosingHandOff);
        }

        var profiles = new ProfileStore(root.Path);
        string userId = profiles.ReadActiveProfileId();
        Assert.AreEqual(server.UserIdOf(Subject), userId);
        Assert.IsNull(profiles.ReadPendingImport(userId), "Keep recorded an import.");
        Assert.IsTrue(File.Exists(Path.Combine(profiles.AccountFolder(userId), "credentials.dat")));
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, "credentials.dat")), "The session landed in the local profile.");
    }

    [TestMethod]
    public async Task Move_records_the_import_and_the_account_runs_it_on_its_first_start()
    {
        await using (Rig local = await Rig.OpenAsync(root.Path, server))
        {
            await InsertNoteAsync(local.Database, NoteId(1), "2026-09-29", "Mine", "body", 0, Utc(1), Utc(1));
            await local.Account.SignInCommand.ExecuteAsync(null);
            await local.Account.MoveLocalNotesCommand.ExecuteAsync(null);
            Assert.AreEqual(1, local.SwitchRequests);
        }

        var profiles = new ProfileStore(root.Path);
        string userId = profiles.ReadActiveProfileId();
        Assert.AreEqual(ProfileStore.LocalProfileId, profiles.ReadPendingImport(userId));

        await using Rig account = await Rig.OpenAsync(root.Path, server);
        await account.Account.PrepareProfileAsync();
        await account.Account.InitializeAsync();

        Assert.IsTrue(account.Account.IsSignedIn);
        Assert.AreEqual(1L, Count(account.Folder, "SELECT COUNT(*) FROM notes;"));
        Assert.AreEqual(0L, Count(root.Path, "SELECT COUNT(*) FROM notes;"), "The moved note is still local.");
        Assert.IsNull(profiles.ReadPendingImport(userId));
    }

    [TestMethod]
    public async Task With_nothing_to_move_there_is_no_question()
    {
        await using Rig local = await Rig.OpenAsync(root.Path, server);

        await local.Account.SignInCommand.ExecuteAsync(null);

        Assert.IsFalse(local.Account.IsChoosingHandOff);
        Assert.AreEqual(1, local.SwitchRequests);
    }

    [TestMethod]
    public async Task Signing_out_syncs_first_then_Remove_with_unsynced_changes_asks_twice()
    {
        await using Rig account = await SignedInAccountAsync();
        await InsertNoteAsync(account.Database, NoteId(7), "2026-09-29", "Offline edit", "body", 0, Utc(2), Utc(2));
        account.NextReport = SyncReport.For(SyncOutcome.Offline);

        await account.Account.SignOutCommand.ExecuteAsync(null);

        Assert.AreEqual(1, account.SyncRuns, "Sign-out did not try to sync what was waiting.");
        Assert.IsTrue(account.Account.IsChoosingSignOut);
        Assert.AreEqual(1, account.Account.UnsyncedChangeCount);
        Assert.IsTrue(account.Account.HasUnsyncedChanges);
        StringAssert.Contains(account.Account.UnsyncedChangesMessage, "1");

        await account.Account.SignOutRemoveCommand.ExecuteAsync(null);

        Assert.IsTrue(account.Account.IsConfirmingRemoveUnsynced, "Remove discarded unsynced changes without asking again.");
        Assert.AreEqual(0, account.SwitchRequests);
        Assert.IsTrue(Directory.Exists(account.Folder));

        await account.Account.SignOutRemoveCommand.ExecuteAsync(null);

        Assert.AreEqual(1, account.SwitchRequests);
        var profiles = new ProfileStore(root.Path);
        Assert.AreEqual(ProfileStore.LocalProfileId, profiles.ReadActiveProfileId());
        Assert.AreEqual(0, profiles.ListAccounts().Count, "The removed account is still listed.");
        Assert.IsFalse(File.Exists(Path.Combine(account.Folder, "credentials.dat")));
    }

    [TestMethod]
    public async Task Signing_out_with_Keep_leaves_the_folder_for_signing_back_in()
    {
        await using Rig account = await SignedInAccountAsync();

        await account.Account.SignOutCommand.ExecuteAsync(null);
        Assert.AreEqual(0, account.Account.UnsyncedChangeCount);
        Assert.IsFalse(account.Account.HasUnsyncedChanges);
        await account.Account.SignOutKeepCommand.ExecuteAsync(null);

        Assert.AreEqual(1, account.SwitchRequests);
        var profiles = new ProfileStore(root.Path);
        Assert.AreEqual(ProfileStore.LocalProfileId, profiles.ReadActiveProfileId());
        CollectionAssert.AreEqual(new[] { server.UserIdOf(Subject) }, profiles.ListAccounts().ToArray());
        Assert.IsFalse(File.Exists(Path.Combine(account.Folder, "credentials.dat")), "Sign-out kept the session.");
        Assert.AreEqual(server.UserIdOf(Subject), (await account.Store.ReadStateAsync()).UserId, "Keep dropped the account's cursor and owner.");
    }

    [TestMethod]
    public async Task Cancelling_the_sign_out_choice_changes_nothing()
    {
        await using Rig account = await SignedInAccountAsync();

        await account.Account.SignOutCommand.ExecuteAsync(null);
        account.Account.CancelSignOutCommand.Execute(null);

        Assert.IsFalse(account.Account.IsChoosingSignOut);
        Assert.IsTrue(account.Account.IsSignedIn);
        Assert.AreEqual(0, account.SwitchRequests);
    }

    [TestMethod]
    public async Task Deleting_asks_what_to_do_with_the_notes_and_Keep_makes_them_local()
    {
        await using (Rig account = await SignedInAccountAsync())
        {
            await InsertNoteAsync(account.Database, NoteId(3), "2026-09-29", "Kept", "body", 0, Utc(2), Utc(2), customTitle: true);

            await account.Account.DeleteAccountCommand.ExecuteAsync(null);

            Assert.IsTrue(account.Account.IsChoosingDeletedNotes);
            Assert.IsFalse(account.Account.WasSessionAlreadyGone);
            Assert.IsFalse(account.Account.ShowsSignIn);
            Assert.AreEqual(0, account.SwitchRequests);

            await account.Account.KeepDeletedNotesCommand.ExecuteAsync(null);
            Assert.AreEqual(1, account.SwitchRequests);
        }

        var profiles = new ProfileStore(root.Path);
        Assert.AreEqual(ProfileStore.LocalProfileId, profiles.ReadActiveProfileId());
        Assert.AreEqual(1L, Count(root.Path, "SELECT COUNT(*) FROM notes WHERE title='Kept';"));
        Assert.AreEqual(0, profiles.ListAccounts().Count);
    }

    [TestMethod]
    public async Task A_session_already_gone_offers_the_same_choice_and_Remove_keeps_nothing()
    {
        await using (Rig account = await SignedInAccountAsync())
        {
            await InsertNoteAsync(account.Database, NoteId(3), "2026-09-29", "Gone", "body", 0, Utc(2), Utc(2));
            server.ForgetSessions();

            await account.Account.DeleteAccountCommand.ExecuteAsync(null);

            Assert.IsTrue(account.Account.IsChoosingDeletedNotes);
            Assert.IsTrue(account.Account.WasSessionAlreadyGone);
            Assert.AreNotEqual(AppStringsOf.DeletedBody, account.Account.DeletedNotesMessage);

            await account.Account.RemoveDeletedNotesCommand.ExecuteAsync(null);
            Assert.IsTrue(account.Account.IsConfirmingRemoveDeletedNotes, "The only copy was removed on one press.");
            Assert.AreEqual(AppStringsOf.RemoveBodySessionGone, account.Account.RemoveDeletedNotesMessage);
            Assert.AreEqual(0, account.SwitchRequests);

            await account.Account.RemoveDeletedNotesCommand.ExecuteAsync(null);
            Assert.AreEqual(1, account.SwitchRequests);
        }

        Assert.AreEqual(0L, Count(root.Path, "SELECT COUNT(*) FROM notes;"));
        Assert.AreEqual(0, new ProfileStore(root.Path).ListAccounts().Count);
    }

    [TestMethod]
    public async Task An_account_folder_left_without_owner_or_session_asks_the_deletion_question_again()
    {
        string userId = server.UserIdOf(Subject);
        var profiles = new ProfileStore(root.Path);
        profiles.MigrateLegacyLayout();
        profiles.CreateAccountProfile(userId, ProfileStore.LocalProfileId);
        profiles.SetActiveProfile(userId);

        await using Rig account = await Rig.OpenAsync(root.Path, server);
        await account.Account.InitializeAsync();

        Assert.IsFalse(account.Account.IsSignedIn);
        Assert.IsTrue(account.Account.IsChoosingDeletedNotes);
    }

    [TestMethod]
    public async Task A_database_owned_by_another_account_reads_as_needing_sign_in()
    {
        await using Rig account = await SignedInAccountAsync();
        await ExecAsync(account.Database, "UPDATE sync_state SET user_id=$u WHERE id=1;", ("$u", UserB));

        await account.Account.InitializeAsync();

        Assert.IsFalse(account.Account.IsSignedIn, "A mismatched database shows as signed in.");
        Assert.IsNotNull(account.Account.ErrorMessage);
        Assert.IsTrue(account.Account.IsOwnerMismatch, "The mismatch has no way out.");

        await account.Account.LeaveMismatchedProfileCommand.ExecuteAsync(null);

        Assert.AreEqual(1, account.SwitchRequests);
        Assert.AreEqual(ProfileStore.LocalProfileId, new ProfileStore(root.Path).ReadActiveProfileId());
        Assert.AreEqual(UserB, (await account.Store.ReadStateAsync()).UserId, "Leaving re-labelled the database.");
    }

    [TestMethod]
    public async Task Remove_counts_again_after_saving_the_editor_and_asks_again_when_more_is_waiting()
    {
        await using Rig account = await SignedInAccountAsync();
        await account.Account.SignOutCommand.ExecuteAsync(null);
        Assert.AreEqual(0, account.Account.UnsyncedChangeCount);

        // Typed after the choice opened, and still only in the editor: the flush is what writes it.
        int saved = 0;
        account.Account.FlushEditor = async () =>
        {
            saved++;
            await InsertNoteAsync(account.Database, NoteId(20 + saved), "2026-09-29", "Typed", "text", saved, Utc(3), Utc(3));
            return true;
        };

        await account.Account.SignOutRemoveCommand.ExecuteAsync(null);

        Assert.AreEqual(0, account.SwitchRequests, "Remove discarded an edit made after the choice opened.");
        Assert.IsTrue(account.Account.IsConfirmingRemoveUnsynced);
        Assert.AreEqual(1, account.Account.UnsyncedChangeCount);

        // More typed while the confirmation was up: the number the user agreed to is out of date.
        await account.Account.SignOutRemoveCommand.ExecuteAsync(null);

        Assert.AreEqual(0, account.SwitchRequests, "The second press discarded more than it said.");
        Assert.AreEqual(2, account.Account.UnsyncedChangeCount);

        account.Account.FlushEditor = () => Task.FromResult(true);
        await account.Account.SignOutRemoveCommand.ExecuteAsync(null);

        Assert.AreEqual(1, account.SwitchRequests);
    }

    [TestMethod]
    public async Task The_editor_is_saved_before_a_deleted_accounts_notes_are_kept()
    {
        await using (Rig account = await SignedInAccountAsync())
        {
            await account.Account.DeleteAccountCommand.ExecuteAsync(null);
            account.Account.FlushEditor = async () =>
            {
                await InsertNoteAsync(account.Database, NoteId(30), "2026-09-29", "Last edit", "text", 0, Utc(3), Utc(3), customTitle: true);
                return true;
            };

            await account.Account.KeepDeletedNotesCommand.ExecuteAsync(null);
            Assert.AreEqual(1, account.SwitchRequests);
        }

        Assert.AreEqual(1L, Count(root.Path, "SELECT COUNT(*) FROM notes WHERE title='Last edit';"), "The open note was lost.");
    }

    [TestMethod]
    public async Task An_editor_that_will_not_save_stops_the_switch()
    {
        await using Rig account = await SignedInAccountAsync();
        await account.Account.DeleteAccountCommand.ExecuteAsync(null);
        account.Account.FlushEditor = () => Task.FromResult(false);

        await account.Account.KeepDeletedNotesCommand.ExecuteAsync(null);

        Assert.AreEqual(0, account.SwitchRequests);
        Assert.IsTrue(account.Account.IsChoosingDeletedNotes, "The question closed on a failed save.");
        Assert.AreEqual(AppStringsOf.Unsaved, account.Account.ErrorMessage);
        Assert.IsTrue(Directory.Exists(account.Folder));
        Assert.AreEqual(account.Folder, new ProfileStore(root.Path).ResolveActiveFolder());
    }

    [TestMethod]
    public async Task An_unexpected_fault_becomes_a_message_rather_than_escaping_the_command()
    {
        await using Rig account = await SignedInAccountAsync();
        await account.Account.DeleteAccountCommand.ExecuteAsync(null);
        account.Account.FlushEditor = () => throw new InvalidDataException("disk said no");

        await account.Account.KeepDeletedNotesCommand.ExecuteAsync(null);

        Assert.AreEqual(AppStringsOf.Unexpected, account.Account.ErrorMessage);
        Assert.AreEqual(0, account.SwitchRequests);
    }

    [TestMethod]
    public async Task A_pending_import_that_fails_does_not_stop_the_account_starting_and_retries_later()
    {
        await using (Rig local = await Rig.OpenAsync(root.Path, server))
        {
            await InsertNoteAsync(local.Database, NoteId(1), "2026-09-29", "Mine", "body", 0, Utc(1), Utc(1));
            await local.Account.SignInCommand.ExecuteAsync(null);
            await local.Account.MoveLocalNotesCommand.ExecuteAsync(null);
        }

        // The source cannot be opened: what a corrupt file, or a busy one, looks like to the import.
        SqliteConnection.ClearAllPools();
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(root.Path, "daynote.db"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "daynote.db"), "not a database");
        foreach (string sidecar in new[] { "daynote.db-wal", "daynote.db-shm" })
        {
            File.Delete(Path.Combine(root.Path, sidecar));
        }

        var profiles = new ProfileStore(root.Path);
        string userId = profiles.ReadActiveProfileId();
        await using (Rig account = await Rig.OpenAsync(root.Path, server))
        {
            await account.Account.PrepareProfileAsync();
            await account.Account.InitializeAsync();

            Assert.IsTrue(account.Account.IsSignedIn, "A failed import stopped the account from starting.");
            Assert.AreEqual(ResumeState.Ready, (await AccountsOf(account).ResumeAsync()).State);
            Assert.AreEqual(ProfileStore.LocalProfileId, profiles.ReadPendingImport(userId), "The failed import was forgotten.");
        }

        SqliteConnection.ClearAllPools();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "daynote.db"), original);
        await using (Rig account = await Rig.OpenAsync(root.Path, server))
        {
            await account.Account.PrepareProfileAsync();
            Assert.AreEqual(1L, Count(account.Folder, "SELECT COUNT(*) FROM notes;"), "The retry did not bring the note.");
            Assert.IsNull(profiles.ReadPendingImport(userId));
        }
    }

    [TestMethod]
    public async Task A_session_this_device_can_no_longer_open_asks_to_sign_in_again_not_about_deletion()
    {
        string userId = server.UserIdOf(Subject);
        var profiles = new ProfileStore(root.Path);
        profiles.MigrateLegacyLayout();
        string folder = profiles.CreateAccountProfile(userId, ProfileStore.LocalProfileId);
        profiles.SetActiveProfile(userId);
        await File.WriteAllBytesAsync(Path.Combine(folder, "credentials.dat"), [1, 2, 3]);

        await using Rig account = await Rig.OpenAsync(root.Path, server);
        await account.Account.InitializeAsync();

        Assert.IsFalse(account.Account.IsChoosingDeletedNotes, "A lost key read as a deleted account.");
        Assert.IsTrue(account.Account.ShowsSignIn);
        Assert.IsNotNull(account.Account.ErrorMessage);
    }

    [TestMethod]
    public async Task A_switch_the_host_could_not_finish_is_reported_and_can_be_retried()
    {
        await using Rig account = await SignedInAccountAsync();
        await account.Account.SignOutCommand.ExecuteAsync(null);
        await account.Account.SignOutKeepCommand.ExecuteAsync(null);
        Assert.AreEqual(1, account.SwitchRequests);

        account.Account.NotifyProfileSwitchFailed();

        Assert.IsTrue(account.Account.IsProfileSwitchStalled);
        Assert.IsFalse(account.Account.IsSwitchingProfile);
        Assert.IsFalse(account.Account.ShowsSignIn, "Sign-in was offered over a half-finished switch.");
        Assert.AreEqual(AppStringsOf.Stalled, account.Account.ErrorMessage);

        account.Account.RetryProfileSwitchCommand.Execute(null);

        Assert.AreEqual(2, account.SwitchRequests);
        Assert.IsFalse(account.Account.IsProfileSwitchStalled);
    }

    private static AccountService AccountsOf(Rig rig) => rig.Accounts;

    /// <summary>Signs in from an empty local profile and "relaunches" over the account.</summary>
    private async Task<Rig> SignedInAccountAsync()
    {
        await using (Rig local = await Rig.OpenAsync(root.Path, server))
        {
            await local.Account.SignInCommand.ExecuteAsync(null);
            Assert.AreEqual(1, local.SwitchRequests);
        }

        Rig account = await Rig.OpenAsync(root.Path, server);
        await account.Account.PrepareProfileAsync();
        await account.Account.InitializeAsync();
        Assert.IsTrue(account.Account.IsSignedIn, "The account profile did not start signed in.");
        return account;
    }

    private static class AppStringsOf
    {
        public static string DeletedBody => Daynote.App.Localization.AppStrings.DeletedNotesBody;

        public static string RemoveBodySessionGone => Daynote.App.Localization.AppStrings.DeletedNotesRemoveBodySessionGone;

        public static string Unsaved => Daynote.App.Localization.AppStrings.ProfileErrorUnsaved;

        public static string Unexpected => Daynote.App.Localization.AppStrings.AccountErrorUnexpected;

        public static string Stalled => Daynote.App.Localization.AppStrings.ProfileSwitchStalled;
    }

    /// <summary>The composition a host builds over the active profile, reduced to the account panel.</summary>
    private sealed class Rig : IAsyncDisposable
    {
        private Rig(string folder, SqliteDatabase database, SqliteSyncStore store, AccountService accounts, AccountViewModel account)
        {
            Folder = folder;
            Database = database;
            Store = store;
            Accounts = accounts;
            Account = account;
        }

        public AccountService Accounts { get; }

        public string Folder { get; }

        public SqliteDatabase Database { get; }

        public SqliteSyncStore Store { get; }

        public AccountViewModel Account { get; }

        public int SwitchRequests { get; private set; }

        public int SyncRuns { get; private set; }

        public SyncReport NextReport { get; set; } = SyncReport.For(SyncOutcome.Completed);

        public static Task<Rig> OpenAsync(string baseRoot, StubServer server)
        {
            var profiles = new ProfileStore(baseRoot);
            profiles.MigrateLegacyLayout();
            string folder = profiles.ResolveActiveFolder();
            SqliteDatabase database = Open(folder);
            var store = new SqliteSyncStore(database);
            var host = new ProfileHost(profiles, folder, database, SessionsFor);
            var accounts = new AccountService(
                server, server, new AesGcmSyncCrypto(), SessionsFor(folder), store, () => "Test", profiles: host);
            Rig? rig = null;
            var account = new AccountViewModel(
                accounts,
                store,
                () =>
                {
                    rig!.SyncRuns++;
                    return ValueTask.FromResult(rig.NextReport);
                },
                new NoExporter(),
                _ => { },
                Path.Combine(folder, "conflicts"));
            rig = new Rig(folder, database, store, accounts, account);
            account.ProfileSwitchRequested += (_, _) => rig.SwitchRequests++;
            return Task.FromResult(rig);
        }

        public async ValueTask DisposeAsync() => await Database.DisposeAsync();

        private static ISyncSessionStore SessionsFor(string folder) =>
            new ProtectedFileSyncSessionStore(folder, new XorProtector());
    }

    private sealed class NoExporter : IRecoveryKeyExporter
    {
        public Task<bool> TryCopyToClipboardAsync(string recoveryKey) => Task.FromResult(false);

        public Task<bool> TrySaveToFileAsync(string recoveryKey) => Task.FromResult(false);
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy) => Xor(plaintext);

        public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy) => Xor(sealedBytes);

        private static byte[] Xor(ReadOnlySpan<byte> input)
        {
            byte[] output = input.ToArray();
            for (int i = 0; i < output.Length; i++)
            {
                output[i] ^= 0x5A;
            }

            return output;
        }
    }

    /// <summary>
    /// The Worker and Google, as far as these flows reach: one account per subject with a stable id
    /// and key, rotating refresh tokens, logout and delete.
    /// </summary>
    private sealed class StubServer : IAuthApiClient, IIdentityProvider
    {
        private readonly Dictionary<string, string> userIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> keys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> refreshTokens = new(StringComparer.Ordinal);

        public string UserIdOf(string subject)
        {
            if (!userIds.TryGetValue(subject, out string? id))
            {
                id = Guid.NewGuid().ToString("D");
                userIds[subject] = id;
                keys[id] = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyMaterial.Length));
            }

            return id;
        }

        /// <summary>What a delete that went through elsewhere leaves: no session this device holds works.</summary>
        public void ForgetSessions() => refreshTokens.Clear();

        public ValueTask<IdentityGrant> AuthorizeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new IdentityGrant(Subject, "verifier", "http://127.0.0.1/"));

        public ValueTask<SessionResponse> SignInWithGoogleAsync(GoogleSignInRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Issue(UserIdOf(request.AuthorizationCode), withKeys: true));

        public ValueTask<SessionResponse> SignInWithAppleAsync(AppleSignInRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<SessionResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (!refreshTokens.Remove(refreshToken, out string? userId))
            {
                throw new AccountException(AccountFailure.InvalidCredentials, "Unknown session.");
            }

            return ValueTask.FromResult(Issue(userId, withKeys: false));
        }

        public ValueTask DeleteAccountAsync(string accessToken, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            refreshTokens.Remove(refreshToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AccountSummary> GetAccountAsync(string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<KeyMaterialResponse> GetKeyMaterialAsync(string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask ProtectAsync(string accessToken, string wrappedDekPassphrase, string wrappedDekRecovery, string kdfParametersJson, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask UnprotectAsync(string accessToken, KeyMaterial dataKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<(Entitlement Entitlement, BillingLinks Links)> GetBillingAsync(string accessToken, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult((Entitlement.Unknown, new BillingLinks(false, false)));

        public ValueTask<string> CreateCheckoutSessionAsync(string accessToken, BillingTier tier, BillingPlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<(Entitlement Entitlement, BillingLinks Links)> ChangePlanAsync(string accessToken, BillingTier tier, BillingPlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<string> CreatePortalSessionAsync(string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<(Entitlement Entitlement, BillingLinks Links)> SubmitAppStoreTransactionAsync(string accessToken, string transactionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private SessionResponse Issue(string userId, bool withKeys)
        {
            string refresh = "refresh-" + Guid.NewGuid().ToString("N");
            refreshTokens[refresh] = userId;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new SessionResponse(
                userId,
                "alice@example.test",
                "access-" + Guid.NewGuid().ToString("N"),
                now.AddMinutes(15),
                refresh,
                withKeys ? new KeyMaterialResponse(KeyProtection.Server, keys[userId], null, null, null) : null,
                Entitlement.Unknown,
                now);
        }
    }
}
