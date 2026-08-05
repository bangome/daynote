using System.Text;
using Daynote.Core.Domain;
using Daynote.Core.Files;
using Daynote.Core.Search;
using Daynote.Infrastructure.Assets;
using Daynote.Infrastructure.Files;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Files;

[TestClass]
public sealed class DayFilePersistenceTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-15").Value;

    [TestMethod]
    public async Task Identical_content_shares_one_asset_and_the_file_survives_until_the_last_reference()
    {
        await using DayFileFixture fixture = DayFileFixture.Create();

        DayFile first = await fixture.AddText("doc-a.txt", "shared-bytes");
        DayFile second = await fixture.AddText("doc-b.txt", "shared-bytes");

        Assert.AreEqual(first.AssetHash, second.AssetHash);
        Assert.AreEqual(first.RelativePath, second.RelativePath);
        Assert.AreEqual(1, fixture.PhysicalFileCount());

        IReadOnlyList<DayFile> listed = await fixture.List.ExecuteAsync(Date);
        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, listed.Select(static file => file.Id).ToArray());
        Assert.IsTrue(listed.All(static file => file.IsAvailable));

        DayFileDeleteReceipt firstDelete = await fixture.Delete.ExecuteAsync(first.Id);
        Assert.IsTrue(firstDelete.Deleted);
        Assert.IsFalse(firstDelete.CleanupPending);
        Assert.IsTrue(File.Exists(fixture.AbsolutePath(second.RelativePath)));

        DayFileDeleteReceipt secondDelete = await fixture.Delete.ExecuteAsync(second.Id);
        Assert.IsTrue(secondDelete.Deleted);
        Assert.IsFalse(File.Exists(fixture.AbsolutePath(second.RelativePath)));
        fixture.AssertCounts(files: 0, assets: 0);
    }

    [TestMethod]
    public async Task Oversize_attachment_is_rejected_without_leaving_a_row_or_a_file()
    {
        await using DayFileFixture fixture = DayFileFixture.Create();

        await Assert.ThrowsExactlyAsync<DayFileTooLargeException>(async () =>
            await fixture.Add.ExecuteAsync(Date, "huge.bin", new FixedLengthZeroStream(FileCapturePolicy.MaxFileBytes + 1)));

        fixture.AssertCounts(files: 0, assets: 0);
        Assert.AreEqual(0, fixture.PhysicalFileCount(includeTemp: true));
    }

    [TestMethod]
    public async Task Reconcile_deletes_only_unreferenced_files_and_temps_inside_the_file_root()
    {
        await using DayFileFixture fixture = DayFileFixture.Create();
        DayFile referenced = await fixture.AddText("keep.txt", "keep-me");
        string orphan = Path.Combine(fixture.FileRoot, "ff", "orphan.dat");
        string stale = Path.Combine(fixture.FileRoot, "aa", "interrupted.tmp");
        string outside = Path.Combine(Path.GetDirectoryName(fixture.DataRoot)!, "outside.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        await File.WriteAllBytesAsync(orphan, [1, 2, 3]);
        await File.WriteAllBytesAsync(stale, [4, 5, 6]);
        await File.WriteAllBytesAsync(outside, [7, 8, 9]);

        AssetReconciliationResult first = await fixture.Reconciler.ReconcileAsync();
        AssetReconciliationResult second = await fixture.Reconciler.ReconcileAsync();

        Assert.AreEqual(1, first.OrphanFilesDeleted);
        Assert.AreEqual(1, first.TemporaryFilesDeleted);
        Assert.AreEqual(0, second.OrphanFilesDeleted + second.TemporaryFilesDeleted);
        Assert.IsTrue(File.Exists(fixture.AbsolutePath(referenced.RelativePath)));
        Assert.IsTrue(File.Exists(outside));
        File.Delete(outside);
    }

    [TestMethod]
    public async Task A_missing_asset_is_reported_as_unavailable_and_the_row_is_kept()
    {
        await using DayFileFixture fixture = DayFileFixture.Create();
        DayFile file = await fixture.AddText("gone.txt", "temporary");
        File.Delete(fixture.AbsolutePath(file.RelativePath));

        IReadOnlyList<DayFile> listed = await fixture.List.ExecuteAsync(Date);

        Assert.HasCount(1, listed);
        Assert.IsFalse(listed[0].IsAvailable);
        fixture.AssertCounts(files: 1, assets: 1);
    }

    [TestMethod]
    public async Task Delete_physical_failure_after_commit_flags_cleanup_and_restart_reconciles_the_orphan()
    {
        var deleteFault = new OneShotAssetFault(ImageAssetFaultPoint.Delete);
        await using DayFileFixture fixture = DayFileFixture.Create(assetFault: deleteFault);
        DayFile file = await fixture.AddText("pending.txt", "pending-delete");
        string path = fixture.AbsolutePath(file.RelativePath);

        DayFileDeleteReceipt receipt = await fixture.Delete.ExecuteAsync(file.Id);

        Assert.IsTrue(receipt.Deleted);
        Assert.IsTrue(receipt.CleanupPending);
        Assert.IsTrue(File.Exists(path));
        fixture.AssertCounts(files: 0, assets: 0);

        await fixture.ReopenAsync();
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task File_name_is_searchable_but_the_bytes_are_not_indexed()
    {
        await using DayFileFixture fixture = DayFileFixture.Create();
        await fixture.AddText("요구사항_정의서.docx", "SECRETCONTENT inside the file body");

        SearchResult hit = (await fixture.Search.SearchAsync("요구사항")).Results.Single();
        Assert.AreEqual(SearchSourceType.File, hit.SourceType);
        StringAssert.Contains(hit.Title, "요구사항_정의서.docx");

        Assert.IsEmpty((await fixture.Search.SearchAsync("SECRETCONTENT")).Results);
        Assert.IsTrue(fixture.Database.CheckIntegrity().IsValid);
    }

    /// <summary>Injects a single I/O fault the first time the store reaches the target pipeline point.</summary>
    private sealed class OneShotAssetFault(ImageAssetFaultPoint target) : IImageAssetFaultInjector
    {
        private bool fired;

        public void At(ImageAssetFaultPoint point, string path)
        {
            if (point == target && !fired)
            {
                fired = true;
                throw new IOException("Injected asset fault.");
            }
        }
    }

    private sealed class DayFileFixture : IAsyncDisposable
    {
        private readonly IImageAssetFaultInjector? assetFault;
        private long sequence;
        private long clockTicks;

        private DayFileFixture(string dataRoot, SqliteDatabase database, IImageAssetFaultInjector? assetFault)
        {
            DataRoot = dataRoot;
            Database = database;
            this.assetFault = assetFault;
            Wire();
        }

        public string DataRoot { get; }
        public string FileRoot => Path.Combine(DataRoot, "files");
        public SqliteDatabase Database { get; private set; }
        public AddDayFile Add { get; private set; } = null!;
        public ListDayFiles List { get; private set; } = null!;
        public DeleteDayFile Delete { get; private set; } = null!;
        public FileAssetReconciler Reconciler { get; private set; } = null!;
        public SearchService Search { get; private set; } = null!;

        public static DayFileFixture Create(IImageAssetFaultInjector? assetFault = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "daynote-dayfiles", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new DayFileFixture(root, OpenDatabase(root), assetFault);
        }

        public ValueTask<DayFile> AddText(string name, string content) =>
            Add.ExecuteAsync(Date, name, new MemoryStream(Encoding.UTF8.GetBytes(content)));

        public string AbsolutePath(string relativePath) => Path.Combine(FileRoot, relativePath);

        public int PhysicalFileCount(bool includeTemp = false) =>
            Directory.Exists(FileRoot)
                ? Directory.GetFiles(FileRoot, "*", SearchOption.AllDirectories)
                    .Count(path => includeTemp || !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                : 0;

        public void AssertCounts(long files, long assets)
        {
            using var connection = Database.OpenReadConnection();
            Assert.AreEqual(files, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM day_files;"));
            Assert.AreEqual(assets, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM file_assets;"));
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        }

        public async Task ReopenAsync()
        {
            await Database.DisposeAsync();
            Database = OpenDatabase(DataRoot);
            Wire();
            await Reconciler.ReconcileAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }
        }

        private void Wire()
        {
            var repository = new SqliteDayFileRepository(Database, NextUtc);
            var store = new ContentAddressedFileStore(DataRoot, assetFault);
            Add = new AddDayFile(repository, store, NextId);
            List = new ListDayFiles(repository, store);
            Delete = new DeleteDayFile(repository, store);
            Reconciler = new FileAssetReconciler(repository, store);
            Search = new SearchService(new SqliteSearchRepository(Database));
        }

        private Guid NextId() =>
            Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref sequence):D12}");

        private DateTimeOffset NextUtc() =>
            DateTimeOffset.Parse("2026-07-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind)
                .AddSeconds(Interlocked.Increment(ref clockTicks));

        private static SqliteDatabase OpenDatabase(string root)
        {
            var database = new SqliteDatabase(new SqliteDatabaseOptions(Path.Combine(root, "daynote.db")));
            database.Initialize();
            return database;
        }
    }

    private sealed class FixedLengthZeroStream(long length) : Stream
    {
        private long position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long remaining = length - position;
            if (remaining <= 0)
            {
                return 0;
            }

            int read = (int)Math.Min(count, remaining);
            Array.Clear(buffer, offset, read);
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => position;
        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
