using Daynote.Infrastructure.Persistence.Profiles;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Daynote.Infrastructure.Portable.Tests.Persistence.Profiles.ProfileFixture;

namespace Daynote.Infrastructure.Portable.Tests.Persistence.Profiles;

/// <summary>docs/PROFILES.md §3 (layout and pointer), §6 (device settings) and §7 (migration).</summary>
[TestClass]
public sealed class ProfileStoreTests
{
    [TestMethod]
    public void The_pointer_resolves_to_local_unless_it_names_an_account_folder_that_exists()
    {
        using var root = new TempDirectory();
        var store = new ProfileStore(root.Path);
        string pointer = Path.Combine(root.Path, ProfileStore.PointerFileName);

        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId(), "missing");
        Assert.AreEqual(root.Path, store.ResolveActiveFolder());

        foreach (string content in new[]
        {
            "not json",
            "[]",
            "{\"version\":2,\"active\":\"" + UserA + "\"}",
            "{\"version\":1}",
            "{\"version\":1,\"active\":\"../../etc\"}",
            "{\"version\":1,\"active\":\"" + UserA.ToUpperInvariant() + "\"}",
            "{\"version\":1,\"active\":\"" + UserA + "\"}",
        })
        {
            File.WriteAllText(pointer, content);
            Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId(), content);
        }

        Directory.CreateDirectory(Path.Combine(root.Path, "accounts", UserA));
        Assert.AreEqual(UserA, store.ReadActiveProfileId(), "names an existing folder");
        Assert.AreEqual(Path.Combine(root.Path, "accounts", UserA), store.ResolveActiveFolder());
    }

    [TestMethod]
    public void SetActiveProfile_round_trips_atomically_and_refuses_an_account_without_a_folder()
    {
        using var root = new TempDirectory();
        var store = new ProfileStore(root.Path);

        Assert.Throws<DirectoryNotFoundException>(() => store.SetActiveProfile(UserA));
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, ProfileStore.PointerFileName)));

        Directory.CreateDirectory(store.AccountFolder(UserA));
        store.SetActiveProfile(UserA);
        Assert.AreEqual(UserA, store.ReadActiveProfileId());
        Assert.AreEqual(
            "{\"version\":1,\"active\":\"" + UserA + "\"}",
            File.ReadAllText(Path.Combine(root.Path, ProfileStore.PointerFileName)));

        store.SetActiveProfile(ProfileStore.LocalProfileId);
        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId());
        Assert.AreEqual(
            0,
            Directory.GetFiles(root.Path, "*.tmp").Length,
            "the temp file is renamed, never left behind");
    }

    [TestMethod]
    public void A_user_id_is_accepted_only_as_a_canonical_uuid_never_as_a_path()
    {
        var store = new ProfileStore(Path.Combine(Path.GetTempPath(), "dn-ids"));

        Assert.IsTrue(ProfileStore.IsValidUserId(UserA));
        Assert.AreEqual(Path.Combine(store.AccountsRoot, UserA), store.AccountFolder(UserA));

        foreach (string bad in new[]
        {
            "",
            "local",
            "..",
            "../" + UserA,
            UserA + "/..",
            UserA.ToUpperInvariant(),
            "{" + UserA + "}",
            UserA.Replace("-", "", StringComparison.Ordinal),
            "/tmp/x",
            "C:\\Windows",
        })
        {
            Assert.IsFalse(ProfileStore.IsValidUserId(bad), bad);
            Assert.Throws<ArgumentException>(() => store.AccountFolder(bad), bad);
        }

        Assert.AreEqual(store.BaseRoot, store.FolderFor(ProfileStore.LocalProfileId));
    }

    [TestMethod]
    public async Task A_signed_out_legacy_root_is_left_byte_for_byte_alone()
    {
        using var root = new TempDirectory();
        await CreateLegacyRootAsync(root.Path, userId: null);
        SortedDictionary<string, string> before = Snapshot(root.Path);

        var store = new ProfileStore(root.Path);
        ProfileMigrationResult result = store.MigrateLegacyLayout();

        Assert.AreEqual(ProfileMigrationOutcome.StayedLocal, result.Outcome);
        SortedDictionary<string, string> after = Snapshot(root.Path);
        Assert.IsTrue(after.Remove(ProfileStore.PointerFileName), "the pointer is written");
        CollectionAssert.AreEqual(before.ToList(), after.ToList());
        Assert.IsFalse(Directory.Exists(store.AccountsRoot));
        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId());

        Assert.AreEqual(ProfileMigrationOutcome.StayedLocal, store.MigrateLegacyLayout().Outcome, "second run");
        after = Snapshot(root.Path);
        after.Remove(ProfileStore.PointerFileName);
        CollectionAssert.AreEqual(before.ToList(), after.ToList());
    }

    [TestMethod]
    public void A_fresh_install_stays_local()
    {
        using var root = new TempDirectory();
        string baseRoot = Path.Combine(root.Path, "Daynote");
        var store = new ProfileStore(baseRoot);

        Assert.AreEqual(ProfileMigrationOutcome.StayedLocal, store.MigrateLegacyLayout().Outcome);
        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId());
        Assert.IsFalse(File.Exists(Path.Combine(baseRoot, "daynote.db")), "no database is created for it");
    }

    [TestMethod]
    public async Task A_signed_in_legacy_root_moves_into_its_account_with_notes_outbox_cursor_and_credentials()
    {
        using var root = new TempDirectory();
        await CreateLegacyRootAsync(root.Path, UserA);
        long outboxBefore = Count(root.Path, "SELECT COUNT(*) FROM sync_outbox;");
        Assert.IsTrue(outboxBefore > 0, "fixture queues its writes");

        var store = new ProfileStore(root.Path);
        ProfileMigrationResult result = store.MigrateLegacyLayout();

        Assert.AreEqual(ProfileMigrationOutcome.MovedToAccount, result.Outcome, result.Error?.ToString());
        Assert.AreEqual(UserA, result.UserId);
        Assert.AreEqual(UserA, store.ReadActiveProfileId());

        string account = store.AccountFolder(UserA);
        Assert.AreEqual(2L, Count(account, "SELECT COUNT(*) FROM notes;"));
        Assert.AreEqual(outboxBefore, Count(account, "SELECT COUNT(*) FROM sync_outbox;"));
        Assert.AreEqual(UserA, Scalar(account, "SELECT user_id FROM sync_state;"));
        Assert.AreEqual(42L, Count(account, "SELECT server_cursor FROM sync_state;"));
        Assert.AreEqual(1L, Count(account, "SELECT COUNT(*) FROM settings WHERE key LIKE 'note.custom-title.%';"));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(account, "credentials.dat")));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(account, "files"), "*", SearchOption.AllDirectories).Length);
        Assert.IsTrue(File.Exists(Path.Combine(account, "conflicts", "2026-09-01-old.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(account, "assets", "clip.png")));

        // The base is now a fresh local profile carrying only the device settings.
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, "credentials.dat")), "the local profile never holds credentials");
        Assert.IsFalse(Directory.Exists(Path.Combine(root.Path, "files")));
        Assert.IsFalse(Directory.Exists(Path.Combine(root.Path, "conflicts")));
        Assert.AreEqual(0L, Count(root.Path, "SELECT COUNT(*) FROM notes;"));
        Assert.IsInstanceOfType<DBNull>(Scalar(root.Path, "SELECT user_id FROM sync_state;"));
        Assert.AreEqual("en", Scalar(root.Path, "SELECT value FROM settings WHERE key='ui.language';"));
        Assert.AreEqual("dark", Scalar(root.Path, "SELECT value FROM settings WHERE key='product.theme';"));
        Assert.AreEqual(0L, Count(root.Path, "SELECT COUNT(*) FROM settings WHERE key LIKE 'note.custom-title.%';"));
        Assert.IsFalse(Directory.Exists(Path.Combine(root.Path, ProfileStore.MigrationStagingDirectoryName)));
        CollectionAssert.AreEqual(new[] { UserA }, store.ListAccounts().ToArray());
    }

    [TestMethod]
    public async Task A_sign_in_still_in_the_wal_of_a_crashed_app_is_seen_and_a_space_in_the_path_is_fine()
    {
        using var temp = new TempDirectory();
        string crashed = Path.Combine(temp.Path, "App Support");
        string live = Path.Combine(temp.Path, "live");
        await CreateLegacyRootAsync(live, userId: null);

        // Sign in with checkpointing off and copy the files while the connection is still open: the
        // copy is what a crash leaves, the sign-in only in the -wal.
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(live, "daynote.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA wal_autocheckpoint=0; UPDATE sync_state SET user_id='{UserA}' WHERE id=1;";
            command.ExecuteNonQuery();
            Directory.CreateDirectory(crashed);
            foreach (string name in new[] { "daynote.db", "daynote.db-wal" })
            {
                File.Copy(Path.Combine(live, name), Path.Combine(crashed, name));
            }
        }

        Assert.IsTrue(new FileInfo(Path.Combine(crashed, "daynote.db-wal")).Length > 0, "fixture: the sign-in is in the WAL");
        var store = new ProfileStore(crashed);
        ProfileMigrationResult result = store.MigrateLegacyLayout();

        Assert.AreEqual(ProfileMigrationOutcome.MovedToAccount, result.Outcome, result.Error?.ToString());
        Assert.AreEqual(UserA, Scalar(store.AccountFolder(UserA), "SELECT user_id FROM sync_state;"));
        Assert.AreEqual(2L, Count(store.AccountFolder(UserA), "SELECT COUNT(*) FROM notes;"));
    }

    [TestMethod]
    public async Task A_second_migration_run_is_a_no_op()
    {
        using var root = new TempDirectory();
        await CreateLegacyRootAsync(root.Path, UserA);
        var store = new ProfileStore(root.Path);
        Assert.AreEqual(ProfileMigrationOutcome.MovedToAccount, store.MigrateLegacyLayout().Outcome);
        SortedDictionary<string, string> before = Snapshot(root.Path);

        Assert.AreEqual(ProfileMigrationOutcome.AlreadyMigrated, store.MigrateLegacyLayout().Outcome);

        CollectionAssert.AreEqual(before.ToList(), Snapshot(root.Path).ToList());
        Assert.AreEqual(UserA, store.ReadActiveProfileId());
    }

    [TestMethod]
    public async Task A_migration_that_fails_part_way_leaves_the_base_untouched()
    {
        using var root = new TempDirectory();
        await CreateLegacyRootAsync(root.Path, UserA);
        // A plain file where accounts/ must go: everything moves into staging, then the final rename
        // fails — the latest point a failure can happen.
        File.WriteAllText(Path.Combine(root.Path, ProfileStore.AccountsDirectoryName), "in the way");
        SortedDictionary<string, string> before = Snapshot(root.Path);

        var store = new ProfileStore(root.Path);
        ProfileMigrationResult result = store.MigrateLegacyLayout();

        Assert.AreEqual(ProfileMigrationOutcome.Failed, result.Outcome);
        Assert.IsNotNull(result.Error);
        CollectionAssert.AreEqual(before.ToList(), Snapshot(root.Path).ToList());
        Assert.IsFalse(File.Exists(Path.Combine(root.Path, ProfileStore.PointerFileName)), "pointer restored to absent");
        Assert.IsFalse(Directory.Exists(Path.Combine(root.Path, ProfileStore.MigrationStagingDirectoryName)));
        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId(), "the app keeps running on the base");
        Assert.AreEqual(UserA, Scalar(root.Path, "SELECT user_id FROM sync_state;"));

        File.Delete(Path.Combine(root.Path, ProfileStore.AccountsDirectoryName));
        Assert.AreEqual(ProfileMigrationOutcome.MovedToAccount, store.MigrateLegacyLayout().Outcome, "the next start retries");
    }

    [TestMethod]
    public async Task A_migration_killed_mid_move_is_put_back_and_completed_on_the_next_start()
    {
        using var root = new TempDirectory();
        await CreateLegacyRootAsync(root.Path, UserA);
        string staged = Path.Combine(root.Path, ProfileStore.MigrationStagingDirectoryName, UserA);
        Directory.CreateDirectory(staged);
        File.Move(Path.Combine(root.Path, "daynote.db"), Path.Combine(staged, "daynote.db"));
        Directory.Move(Path.Combine(root.Path, "files"), Path.Combine(staged, "files"));

        var store = new ProfileStore(root.Path);
        ProfileMigrationResult result = store.MigrateLegacyLayout();

        Assert.AreEqual(ProfileMigrationOutcome.MovedToAccount, result.Outcome, result.Error?.ToString());
        string account = store.AccountFolder(UserA);
        Assert.AreEqual(2L, Count(account, "SELECT COUNT(*) FROM notes;"));
        Assert.IsTrue(Directory.Exists(Path.Combine(account, "files")));
        Assert.IsTrue(File.Exists(Path.Combine(account, "credentials.dat")));
        Assert.IsFalse(Directory.Exists(Path.Combine(root.Path, ProfileStore.MigrationStagingDirectoryName)));
    }

    [TestMethod]
    public async Task A_new_account_profile_is_seeded_with_device_settings_and_an_existing_one_is_reused()
    {
        using var root = new TempDirectory();
        var local = Open(root.Path);
        try
        {
            await SetSettingAsync(local, "ui.language", "en");
            await SetSettingAsync(local, "shortcuts.summon", "Ctrl+Alt+N");
            await SetSettingAsync(local, "onboarding.completed", "1");
            await InsertNoteAsync(local, NoteId(1), "2026-09-01", "Mine", "text", 0, Utc(1), Utc(1), customTitle: true);
        }
        finally
        {
            await local.DisposeAsync();
        }

        var store = new ProfileStore(root.Path);
        string folder = store.CreateAccountProfile(UserA, ProfileStore.LocalProfileId);

        Assert.AreEqual(store.AccountFolder(UserA), folder);
        Assert.AreEqual("en", Scalar(folder, "SELECT value FROM settings WHERE key='ui.language';"));
        Assert.AreEqual("Ctrl+Alt+N", Scalar(folder, "SELECT value FROM settings WHERE key='shortcuts.summon';"));
        Assert.AreEqual("1", Scalar(folder, "SELECT value FROM settings WHERE key='onboarding.completed';"));
        Assert.AreEqual(0L, Count(folder, "SELECT COUNT(*) FROM settings WHERE key LIKE 'note.custom-title.%';"), "content stays behind");
        Assert.AreEqual(0L, Count(folder, "SELECT COUNT(*) FROM notes;"));
        Assert.IsInstanceOfType<DBNull>(Scalar(folder, "SELECT user_id FROM sync_state;"));
        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId(), "creating does not switch");

        // Reused as it is: nothing is copied over an existing profile.
        var account = Open(folder);
        try
        {
            await SetSettingAsync(account, "ui.language", "ko");
        }
        finally
        {
            await account.DisposeAsync();
        }

        Assert.AreEqual(folder, store.CreateAccountProfile(UserA, ProfileStore.LocalProfileId));
        Assert.AreEqual("ko", Scalar(folder, "SELECT value FROM settings WHERE key='ui.language';"));
        CollectionAssert.AreEqual(new[] { UserA }, store.ListAccounts().ToArray());
    }

    [TestMethod]
    public void Accounts_are_listed_and_removed_and_removing_the_active_one_points_back_at_local()
    {
        using var root = new TempDirectory();
        var store = new ProfileStore(root.Path);
        store.CreateAccountProfile(UserB, ProfileStore.LocalProfileId);
        store.CreateAccountProfile(UserA, ProfileStore.LocalProfileId);
        Directory.CreateDirectory(Path.Combine(store.AccountsRoot, ".creating-leftover"));
        Directory.CreateDirectory(Path.Combine(store.AccountsRoot, "not-an-account"));
        store.SetActiveProfile(UserA);

        CollectionAssert.AreEqual(new[] { UserA, UserB }, store.ListAccounts().ToArray());

        Assert.IsTrue(store.RemoveAccount(UserA));
        Assert.IsFalse(Directory.Exists(store.AccountFolder(UserA)));
        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadActiveProfileId());
        StringAssert.Contains(File.ReadAllText(Path.Combine(root.Path, ProfileStore.PointerFileName)), "\"local\"");
        CollectionAssert.AreEqual(new[] { UserB }, store.ListAccounts().ToArray());
        Assert.IsFalse(store.RemoveAccount(UserA), "already gone");

        Assert.AreEqual(ProfileMigrationOutcome.AlreadyMigrated, store.MigrateLegacyLayout().Outcome);
        Assert.IsFalse(Directory.Exists(Path.Combine(store.AccountsRoot, ".creating-leftover")), "swept at start-up");
    }

    [TestMethod]
    public void A_pending_import_marker_round_trips_and_rejects_nonsense()
    {
        using var root = new TempDirectory();
        var store = new ProfileStore(root.Path);
        string folder = store.CreateAccountProfile(UserA, ProfileStore.LocalProfileId);

        Assert.IsNull(store.ReadPendingImport(UserA));
        Assert.Throws<ArgumentException>(() => store.RequestImport(UserA, UserA));
        Assert.Throws<DirectoryNotFoundException>(() => store.RequestImport(UserB, ProfileStore.LocalProfileId));

        store.RequestImport(UserA, ProfileStore.LocalProfileId);
        Assert.AreEqual(ProfileStore.LocalProfileId, store.ReadPendingImport(UserA));

        File.WriteAllText(Path.Combine(folder, ProfileStore.PendingImportFileName), "{\"version\":1,\"source\":\"../x\"}");
        Assert.IsNull(store.ReadPendingImport(UserA));

        store.RequestImport(UserA, ProfileStore.LocalProfileId);
        store.ClearPendingImport(UserA);
        Assert.IsNull(store.ReadPendingImport(UserA));
        store.ClearPendingImport(UserA);
    }
}
