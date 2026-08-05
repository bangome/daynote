using System.Text;
using Daynote.Core.Domain;
using Daynote.Core.Files;

namespace Daynote.Core.Tests;

[TestClass]
public sealed class AddDayFileTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-14").Value;

    [TestMethod]
    public async Task Execute_removes_a_brand_new_orphan_asset_when_the_row_write_fails()
    {
        var store = new FakeFileAssetStore(createdNew: true);
        var repository = new ThrowingDayFileRepository(referenced: false);
        var useCase = new AddDayFile(repository, store, () => Guid.Parse("00000000-0000-0000-0000-000000000001"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await useCase.ExecuteAsync(Date, "report.pdf", new MemoryStream(Encoding.UTF8.GetBytes("bytes"))));

        CollectionAssert.AreEqual(new[] { "ab/hash.pdf" }, store.Deleted);
    }

    [TestMethod]
    public async Task Execute_keeps_a_shared_asset_when_the_row_write_fails()
    {
        var store = new FakeFileAssetStore(createdNew: false);
        var repository = new ThrowingDayFileRepository(referenced: true);
        var useCase = new AddDayFile(repository, store, () => Guid.Parse("00000000-0000-0000-0000-000000000001"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await useCase.ExecuteAsync(Date, "report.pdf", new MemoryStream(Encoding.UTF8.GetBytes("bytes"))));

        Assert.AreEqual(0, store.Deleted.Count);
    }

    [TestMethod]
    public async Task Execute_rejects_a_blank_display_name()
    {
        var store = new FakeFileAssetStore(createdNew: true);
        var repository = new ThrowingDayFileRepository(referenced: false);
        var useCase = new AddDayFile(repository, store);

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await useCase.ExecuteAsync(Date, "   ", new MemoryStream()));
    }

    private sealed class FakeFileAssetStore(bool createdNew) : IFileAssetStore
    {
        public List<string> Deleted { get; } = [];

        public async ValueTask<PreparedFileAsset> PrepareAsync(
            Stream content, string extension, CancellationToken cancellationToken = default)
        {
            using var sink = new MemoryStream();
            await content.CopyToAsync(sink, cancellationToken);
            return new PreparedFileAsset("hash", "ab/hash" + extension, sink.Length, createdNew);
        }

        public ValueTask<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<byte[]?>(null);

        public ValueTask DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Deleted.Add(relativePath);
            return ValueTask.CompletedTask;
        }

        public ValueTask<AssetReconciliationResult> ReconcileAsync(
            IReadOnlySet<string> referencedPaths, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AssetReconciliationResult(0, 0));
    }

    private sealed class ThrowingDayFileRepository(bool referenced) : IDayFileRepository
    {
        public ValueTask<DayFile> AddAsync(
            Guid id, LocalDate localDate, string displayName, PreparedFileAsset asset,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("row write failed");

        public ValueTask<IReadOnlyList<DayFile>> GetForDateAsync(
            LocalDate localDate, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DayFile>>([]);

        public ValueTask<DayFileDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DayFileDeleteResult(false, null));

        public ValueTask<IReadOnlySet<string>> GetReferencedAssetPathsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public ValueTask<bool> IsAssetReferencedAsync(string hash, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(referenced);
    }
}
