using System.Security.Cryptography;
using System.Text;
using Daynote.Core.Domain;
using Daynote.Core.Files;
using Daynote.Core.Sync;
using Daynote.Infrastructure.Assets;
using Daynote.Infrastructure.Files;
using Daynote.Infrastructure.Sync;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Sync;

/// <summary>
/// Attachment sync end to end (docs/CLOUD_SYNC.md §5, Phase 7): two real local databases, two real
/// content-addressed file stores, one shared server, real crypto.
/// </summary>
/// <remarks>
/// The checks that matter most are the ones a unit test of any single layer would miss — that the
/// bytes arrive intact and identical, that the server is handed nothing readable, that a delete
/// propagates and stays deleted, and that a lapsed subscription stops the transfer without losing
/// anything on either side.
/// </remarks>
[TestClass]
public sealed class FileSyncConvergenceTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-09-10").Value;
    private static readonly AesGcmSyncCrypto Crypto = new();
    private const string UserId = "22222222-2222-4222-8222-222222222222";

    private DateTimeOffset now;
    private InMemorySyncServer server = null!;
    private KeyMaterial dataKey = null!;
    private Device alice = null!;
    private Device bob = null!;

    [TestInitialize]
    public void Setup()
    {
        now = DateTimeOffset.Parse(
            "2026-09-10T09:00:00Z",
            null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        server = new InMemorySyncServer(() => now);
        dataKey = KeyMaterial.Random();
        alice = Device.Create("alice", server, dataKey, () => now);
        bob = Device.Create("bob", server, dataKey, () => now);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await alice.DisposeAsync();
        await bob.DisposeAsync();
        dataKey.Dispose();
    }

    [TestMethod]
    public async Task An_attachment_added_on_one_device_arrives_on_the_other_byte_for_byte()
    {
        byte[] content = Bytes("보고서 내용", 4096);
        await alice.AddFile("보고서.pdf", content);

        await Converge();

        DayFile received = (await bob.Files()).Single();
        Assert.AreEqual("보고서.pdf", received.DisplayName);
        Assert.AreEqual(content.Length, received.ByteLength);
        Assert.IsTrue(received.IsAvailable, "The bytes should have been downloaded, not just the row.");
        CollectionAssert.AreEqual(content, await bob.ReadBytes(received.AssetHash));
    }

    [TestMethod]
    public async Task The_server_never_holds_the_filename_or_the_content()
    {
        byte[] content = Bytes("기밀", 512);
        await alice.AddFile("급여명세서.pdf", content);
        await Converge();

        foreach (string blob in server.StoredBlobs)
        {
            StringAssert.DoesNotMatch(blob, new System.Text.RegularExpressions.Regex("급여명세서"));
            StringAssert.StartsWith(blob, "v1.");
        }

        byte[] stored = server.StoredObjects.Values.Single();
        Assert.AreNotEqual(
            Convert.ToHexStringLower(SHA256.HashData(content)),
            Convert.ToHexStringLower(SHA256.HashData(stored)),
            "The object is stored sealed, so it cannot hash to the plaintext.");
        CollectionAssert.AreNotEqual(content, stored);
    }

    [TestMethod]
    public async Task The_object_key_is_blinded_rather_than_the_content_hash()
    {
        byte[] content = Bytes("어떤 파일", 256);
        string hash = await alice.AddFile("파일.bin", content);
        await Converge();

        string key = server.StoredObjects.Keys.Single();

        // The operator must not be able to test "does this user hold a file I know the hash of".
        Assert.AreNotEqual(hash, key);
        Assert.AreEqual(64, key.Length);
        Assert.AreEqual(Crypto.BlindAssetKey(dataKey, hash), key);
    }

    [TestMethod]
    public async Task A_deleted_attachment_disappears_and_does_not_come_back()
    {
        string hash = await alice.AddFile("임시.txt", Bytes("버릴 것", 64));
        await Converge();
        Assert.AreEqual(1, (await bob.Files()).Count);

        Advance(5);
        await alice.DeleteFile((await alice.Files()).Single().Id);
        await Converge();

        Assert.AreEqual(0, (await bob.Files()).Count);
        Assert.AreEqual(0, server.StoredObjects.Count, "The last reference went, so the bytes go too.");

        // A second round must not resurrect it from either side's leftovers.
        await Converge();
        Assert.AreEqual(0, (await alice.Files()).Count);
        Assert.AreEqual(0, (await bob.Files()).Count);
        _ = hash;
    }

    [TestMethod]
    public async Task Two_attachments_of_identical_content_share_one_object()
    {
        byte[] content = Bytes("같은 내용", 1024);
        await alice.AddFile("사본1.bin", content);
        await alice.AddFile("사본2.bin", content);

        await Converge();

        Assert.AreEqual(2, (await bob.Files()).Count);
        Assert.AreEqual(1, server.StoredObjects.Count, "One content hash, one blinded key, one object.");
    }

    [TestMethod]
    public async Task A_pulled_attachment_is_not_pushed_straight_back()
    {
        await alice.AddFile("한번만.bin", Bytes("에코 금지", 128));
        await alice.Sync();

        // Bob's very first sync, and nothing after it: a later round would have drained his outbox
        // by pushing the echo, which is exactly the bug this test is for.
        await bob.Sync();

        // The merge inserted a row, and the AFTER INSERT trigger queued it for upload. That entry
        // has to be removed, or Bob pushes back what he just pulled — and with two devices doing
        // it, forever.
        Assert.AreEqual(0, await bob.PendingFileCount(), "The pulled attachment is still queued to push.");

        await bob.Sync();
        Assert.AreEqual(1, server.StoredObjects.Count);
        Assert.AreEqual(1, (await alice.Files()).Count);
        Assert.AreEqual(1, (await bob.Files()).Count);
    }

    [TestMethod]
    public async Task A_row_whose_bytes_have_not_landed_is_shown_and_finished_next_run()
    {
        byte[] content = Bytes("지연", 2048);
        await alice.AddFile("느린.bin", content);
        await alice.Sync();

        // Metadata and bytes are two calls, so a device can pull a row it cannot yet fetch.
        server.ObjectsHidden = true;
        SyncReport first = await bob.Sync();

        DayFile pending = (await bob.Files()).Single();
        Assert.IsFalse(pending.IsAvailable, "The row is here; the bytes are not.");
        Assert.AreEqual(0, first.AssetsDownloaded);
        // Not an error, and not a lost attachment: the day shows it, greyed.
        Assert.AreEqual(SyncOutcome.Completed, first.Outcome);

        server.ObjectsHidden = false;
        SyncReport second = await bob.Sync();

        Assert.AreEqual(1, second.AssetsDownloaded);
        Assert.IsTrue((await bob.Files()).Single().IsAvailable);
        CollectionAssert.AreEqual(content, await bob.ReadBytes(pending.AssetHash));
    }

    [TestMethod]
    public async Task A_failed_upload_does_not_stop_the_same_content_being_downloaded_later()
    {
        byte[] content = Bytes("같은 내용", 512);
        string hash = await alice.AddFile("공유.bin", content);

        // Bob records a failure against this hash first — the shape of "the bytes went missing on
        // this device". There is one queue row per hash, so a stale 'up' entry must not latch.
        await bob.RecordAssetFailure(hash);

        await Converge();

        Assert.IsTrue(
            (await bob.Files()).Single().IsAvailable,
            "A leftover upload failure kept the download from ever being queued.");
    }

    // ---- what a lapsed subscription does, and does not do ----

    [TestMethod]
    public async Task Without_a_subscription_the_bytes_stay_put_and_nothing_is_lost()
    {
        server.EntitledToFiles = false;
        byte[] content = Bytes("구독 없음", 256);
        await alice.AddFile("보류.bin", content);

        SyncReport report = await alice.Sync();

        Assert.IsTrue(report.FileSyncBlocked, "The run has to say the attachment was withheld.");
        // Not a failure: the notes in the same run synced.
        Assert.AreEqual(SyncOutcome.Completed, report.Outcome);
        Assert.AreEqual(0, server.StoredObjects.Count);

        // The attachment is untouched on this device, and still queued.
        DayFile local = (await alice.Files()).Single();
        Assert.IsTrue(local.IsAvailable);
        CollectionAssert.AreEqual(content, await alice.ReadBytes(local.AssetHash));

        // Subscribing resumes it, from the same queue, with no user action.
        server.EntitledToFiles = true;
        await Converge();
        Assert.AreEqual(1, (await bob.Files()).Count);
        CollectionAssert.AreEqual(content, await bob.ReadBytes(local.AssetHash));
    }

    [TestMethod]
    public async Task Text_keeps_syncing_while_attachments_are_withheld()
    {
        server.EntitledToFiles = false;
        await alice.AddFile("보류.bin", Bytes("x", 64));

        SyncReport report = await alice.Sync();

        Assert.AreEqual(SyncOutcome.Completed, report.Outcome);
        Assert.IsTrue(report.FileSyncBlocked);
    }

    [TestMethod]
    public async Task A_delete_propagates_even_without_a_subscription()
    {
        await alice.AddFile("지울것.bin", Bytes("삭제 대상", 128));
        await Converge();
        Assert.AreEqual(1, (await bob.Files()).Count);

        Advance(5);
        server.EntitledToFiles = false;
        await alice.DeleteFile((await alice.Files()).Single().Id);
        await Converge();

        // Refusing this would leave the attachment on Bob's PC forever: a lapse must stop a
        // feature, never corrupt the other devices.
        Assert.AreEqual(0, (await bob.Files()).Count);
    }

    // ---- helpers ----

    private async Task Converge()
    {
        for (int round = 0; round < 3; round += 1)
        {
            await alice.Sync();
            await bob.Sync();
        }

        await alice.Sync();
    }

    private void Advance(int minutes) => now = now.AddMinutes(minutes);

    /// <summary>Deterministic, compressible-looking bytes with a recognisable marker inside.</summary>
    private static byte[] Bytes(string marker, int length)
    {
        byte[] seed = Encoding.UTF8.GetBytes(marker);
        var content = new byte[length];
        for (int index = 0; index < length; index += 1)
        {
            content[index] = seed[index % seed.Length];
        }

        return content;
    }

    private sealed class Device : IAsyncDisposable
    {
        private readonly string root;
        private readonly TestDatabase fixture;
        private readonly SqliteDayFileRepository files;
        private readonly ContentAddressedFileStore bytes;
        private readonly SqliteSyncStore store;
        private readonly SyncEngine engine;
        private readonly KeyMaterial key;
        private int nextId;

        private Device(
            string root,
            TestDatabase fixture,
            SqliteDayFileRepository files,
            ContentAddressedFileStore bytes,
            SqliteSyncStore store,
            SyncEngine engine,
            KeyMaterial key)
        {
            this.root = root;
            this.fixture = fixture;
            this.files = files;
            this.bytes = bytes;
            this.store = store;
            this.engine = engine;
            this.key = key;
        }

        internal static Device Create(
            string label,
            InMemorySyncServer server,
            KeyMaterial key,
            Func<DateTimeOffset> clock)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "daynote-file-sync-tests",
                $"{label}-{Guid.NewGuid():N}");
            TestDatabase fixture = TestDatabase.CreateIn(root);
            fixture.Database.Initialize();

            var files = new SqliteDayFileRepository(fixture.Database, clock);
            var bytes = new ContentAddressedFileStore(root);
            var store = new SqliteSyncStore(fixture.Database, clock);
            var assets = new SqliteFileSyncAssetStore(fixture.Database, bytes);
            var engine = new SyncEngine(server.ClientFor(label), Crypto, store, clock, null, assets);

            store.SignInAsync(UserId, 1).AsTask().GetAwaiter().GetResult();
            return new Device(root, fixture, files, bytes, store, engine, key);
        }

        internal ValueTask<SyncReport> Sync() => engine.SyncAsync(new SyncSession(UserId, key));

        /// <summary>Adds an attachment the way the app does, and answers with its content hash.</summary>
        internal async Task<string> AddFile(string displayName, byte[] content)
        {
            using var stream = new MemoryStream(content, writable: false);
            PreparedFileAsset asset = await bytes.PrepareAsync(stream, Path.GetExtension(displayName));
            await files.AddAsync(NextGuid(), Date, displayName, asset);
            return asset.Hash;
        }

        internal async Task<IReadOnlyList<DayFile>> Files()
        {
            IReadOnlyList<DayFile> rows = await files.GetForDateAsync(Date);
            var resolved = new List<DayFile>(rows.Count);
            foreach (DayFile row in rows)
            {
                // What ListDayFiles does in the app: a row whose bytes are not here yet is present
                // and unavailable, not missing.
                resolved.Add(row with { IsAvailable = await bytes.ExistsAsync(row.RelativePath) });
            }

            return resolved;
        }

        internal Task DeleteFile(Guid id) => files.DeleteAsync(id).AsTask();

        /// <summary>Records a transfer failure against a hash, as a failed upload would.</summary>
        internal Task RecordAssetFailure(string assetHash) =>
            store.RecordAssetFailureAsync(assetHash, "missing here").AsTask();

        /// <summary>How many attachments this device still owes the server.</summary>
        internal async Task<int> PendingFileCount() =>
            (await store.ReadPendingFilesAsync(100)).Count;

        internal async Task<byte[]?> ReadBytes(string assetHash)
        {
            var assets = new SqliteFileSyncAssetStore(fixture.Database, bytes);
            return await assets.ReadAsync(assetHash);
        }

        public async ValueTask DisposeAsync()
        {
            await fixture.DisposeAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A test directory that will not delete is not a test failure.
            }
        }

        private Guid NextGuid()
        {
            nextId += 1;
            return Guid.Parse($"33333333-3333-4333-8333-{nextId:D12}");
        }
    }
}
