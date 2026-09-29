using Daynote.Core.Settings;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Profiles;
using Daynote.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Daynote.Infrastructure.Portable.Tests.Persistence.Profiles.ProfileFixture;

namespace Daynote.Infrastructure.Portable.Tests.Persistence.Profiles;

/// <summary>
/// The file-system half of the account flows (docs/PROFILES.md §5): a <i>Move</i> that leaves nothing
/// in two places, removal that waits until nothing holds the folder, and a hand-off that only moves
/// the pointer once everything else is in place.
/// </summary>
[TestClass]
public sealed class ProfileHostTests
{
    [TestMethod]
    public async Task Move_takes_notes_and_attachments_out_of_the_source_without_tombstones_but_keeps_the_sample()
    {
        using var root = new TempDirectory();
        string source = Path.Combine(root.Path, "src");
        string destination = Path.Combine(root.Path, "dst");
        string sample = NoteId(50);

        SqliteDatabase from = Open(source);
        try
        {
            await InsertNoteAsync(from, NoteId(1), "2026-09-01", "Plan", "first", 0, Utc(1), Utc(3), false, true, "work");
            await InsertNoteAsync(from, sample, "2026-09-01", "Welcome", "sample body", 1, Utc(1), Utc(1));
            await SetSettingAsync(from, OnboardingSettings.SampleNoteIdKey, sample);
            await SetSettingAsync(from, OnboardingSettings.SampleNoteBodyKey, "sample body");
            await InsertFileAsync(from, source, NoteId(90), "2026-09-01", "report.txt", "attachment"u8.ToArray(), Utc(1));
        }
        finally
        {
            await from.DisposeAsync();
        }

        SqliteDatabase to = Open(destination);
        try
        {
            ProfileImportResult result = await new ProfileImporter(to, destination).MoveFromAsync(source);
            Assert.AreEqual(1, result.NotesImported);
            Assert.AreEqual(1, result.FilesImported);
        }
        finally
        {
            await to.DisposeAsync();
        }

        Assert.AreEqual(1L, Count(source, "SELECT COUNT(*) FROM notes;"), "only the untouched sample stays");
        Assert.AreEqual(1L, Count(source, "SELECT COUNT(*) FROM notes WHERE id=$id;", ("$id", sample)));
        Assert.AreEqual(0L, Count(source, "SELECT COUNT(*) FROM day_files;"));
        Assert.AreEqual(0L, Count(source, "SELECT COUNT(*) FROM file_assets;"));
        Assert.AreEqual(0L, Count(source, "SELECT COUNT(*) FROM note_tags;"));
        Assert.AreEqual(0L, Count(source, "SELECT COUNT(*) FROM settings WHERE key LIKE 'note.custom-title.%';"));
        Assert.AreEqual(0L, Count(source, "SELECT COUNT(*) FROM sync_tombstones;"), "a tombstone would block importing them back later");
        Assert.AreEqual(0, Directory.EnumerateFiles(Path.Combine(source, "files"), "*", SearchOption.AllDirectories).Count(), "the blob stayed");
        Assert.AreEqual(1L, Count(destination, "SELECT COUNT(*) FROM day_files;"));
        Assert.AreEqual(1, Directory.EnumerateFiles(Path.Combine(destination, "files"), "*", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public async Task Move_leaves_an_attachment_it_could_not_bring_in_the_source()
    {
        using var root = new TempDirectory();
        string source = Path.Combine(root.Path, "src");
        string destination = Path.Combine(root.Path, "dst");

        SqliteDatabase from = Open(source);
        try
        {
            await InsertFileAsync(from, source, NoteId(90), "2026-09-01", "here.txt", "has bytes"u8.ToArray(), Utc(1));
            // Pulled on this device but never downloaded: the row exists, the blob does not.
            await InsertFileAsync(from, source, NoteId(91), "2026-09-01", "missing.txt", "never here"u8.ToArray(), Utc(1), writeBlob: false);
        }
        finally
        {
            await from.DisposeAsync();
        }

        SqliteDatabase to = Open(destination);
        try
        {
            ProfileImportResult result = await new ProfileImporter(to, destination).MoveFromAsync(source);
            Assert.AreEqual(1, result.FilesImported);
            Assert.AreEqual(1, result.FilesSkipped);
        }
        finally
        {
            await to.DisposeAsync();
        }

        Assert.AreEqual(0L, Count(source, "SELECT COUNT(*) FROM day_files WHERE id=$id;", ("$id", NoteId(90))), "the moved attachment stayed");
        Assert.AreEqual(1L, Count(source, "SELECT COUNT(*) FROM day_files WHERE id=$id;", ("$id", NoteId(91))), "an attachment that never arrived was deleted");
        Assert.AreEqual(0L, Count(destination, "SELECT COUNT(*) FROM day_files WHERE id=$id;", ("$id", NoteId(91))));
    }

    [TestMethod]
    public void A_folder_marked_for_removal_is_gone_at_once_for_readers_and_on_disk_at_the_next_start()
    {
        using var root = new TempDirectory();
        var profiles = new ProfileStore(root.Path);
        profiles.MigrateLegacyLayout();
        profiles.CreateAccountProfile(UserA, ProfileStore.LocalProfileId);
        profiles.CreateAccountProfile(UserB, ProfileStore.LocalProfileId);
        profiles.SetActiveProfile(UserA);

        profiles.MarkForRemoval(UserA);

        Assert.AreEqual(ProfileStore.LocalProfileId, profiles.ReadActiveProfileId());
        CollectionAssert.AreEqual(new[] { UserB }, profiles.ListAccounts().ToArray());
        Assert.IsTrue(Directory.Exists(profiles.AccountFolder(UserA)), "removed while it might still be open");

        Assert.AreEqual(ProfileMigrationOutcome.AlreadyMigrated, profiles.MigrateLegacyLayout().Outcome);

        Assert.IsFalse(Directory.Exists(profiles.AccountFolder(UserA)));
        Assert.IsTrue(Directory.Exists(profiles.AccountFolder(UserB)));
    }

    [TestMethod]
    public async Task Signing_in_again_before_a_removal_ran_starts_that_account_over()
    {
        using var root = new TempDirectory();
        var profiles = new ProfileStore(root.Path);
        profiles.MigrateLegacyLayout();
        string folder = profiles.CreateAccountProfile(UserA, ProfileStore.LocalProfileId);
        SqliteDatabase database = Open(folder);
        await InsertNoteAsync(database, NoteId(1), "2026-09-01", "Removed", "body", 0, Utc(1), Utc(1));
        await database.DisposeAsync();
        SqliteConnection.ClearAllPools();
        profiles.MarkForRemoval(UserA);

        profiles.CreateAccountProfile(UserA, ProfileStore.LocalProfileId);

        Assert.IsFalse(profiles.IsMarkedForRemoval(UserA));
        Assert.AreEqual(0L, Count(folder, "SELECT COUNT(*) FROM notes;"), "what the user removed came back");
    }

    [TestMethod]
    public async Task A_hand_off_writes_the_session_into_the_account_and_moves_the_pointer_last()
    {
        using var root = new TempDirectory();
        var profiles = new ProfileStore(root.Path);
        profiles.MigrateLegacyLayout();
        SqliteDatabase local = Open(root.Path);
        try
        {
            var host = new ProfileHost(profiles, root.Path, local, SessionsFor);
            Assert.AreEqual(ProfileStore.LocalProfileId, host.CurrentProfileId);
            Assert.IsTrue(host.IsLocalProfile);

            // A marker left by an abandoned Move must not act on a later Keep.
            profiles.CreateAccountProfile(UserA, ProfileStore.LocalProfileId);
            profiles.RequestImport(UserA, ProfileStore.LocalProfileId);

            using var credentials = new SyncCredentials(UserA, "a@b.c", "access", DateTimeOffset.UtcNow, "refresh", 1, null);
            await host.HandOffToAccountAsync(UserA, credentials, moveLocalNotes: false);

            Assert.AreEqual(UserA, profiles.ReadActiveProfileId());
            Assert.IsNull(profiles.ReadPendingImport(UserA));
            using SyncCredentials? saved = await SessionsFor(profiles.AccountFolder(UserA)).LoadAsync();
            Assert.AreEqual(UserA, saved?.UserId);
            Assert.IsNull(await SessionsFor(root.Path).LoadAsync(), "the local profile got a session");

            var refused = await Assert.ThrowsExactlyAsync<AccountException>(
                async () => await host.HandOffToAccountAsync("../escape", credentials, moveLocalNotes: false));
            Assert.AreEqual(AccountFailure.ServerError, refused.Failure);
        }
        finally
        {
            await local.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task The_host_knows_which_profile_it_runs_on_and_counts_the_local_notes_from_an_account()
    {
        using var root = new TempDirectory();
        var profiles = new ProfileStore(root.Path);
        profiles.MigrateLegacyLayout();
        SqliteDatabase local = Open(root.Path);
        await InsertNoteAsync(local, NoteId(1), "2026-09-01", "Mine", "body", 0, Utc(1), Utc(1));
        await local.DisposeAsync();

        string folder = profiles.CreateAccountProfile(UserB, ProfileStore.LocalProfileId);
        SqliteDatabase account = Open(folder);
        try
        {
            var host = new ProfileHost(profiles, folder, account, SessionsFor);
            Assert.AreEqual(UserB, host.CurrentProfileId);
            Assert.IsFalse(host.IsLocalProfile);
            Assert.AreEqual(new LocalContent(1, 0), await host.CountLocalContentAsync());
        }
        finally
        {
            await account.DisposeAsync();
        }
    }

    private static ISyncSessionStore SessionsFor(string folder) => new ProtectedFileSyncSessionStore(folder, new PlainProtector());

    private sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> entropy) => plaintext.ToArray();

        public byte[] Unprotect(ReadOnlySpan<byte> sealedBytes, ReadOnlySpan<byte> entropy) => sealedBytes.ToArray();
    }
}
