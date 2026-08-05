using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Tests;

[TestClass]
public sealed class NoteFavoriteTagsTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-14").Value;

    [TestMethod]
    public void CreatePersisted_defaults_to_not_favorite_with_no_tags()
    {
        Note note = Note.CreatePersisted(Id(1), Date, 0, "제목", "body").Value;

        Assert.IsFalse(note.IsFavorite);
        Assert.AreEqual(0, note.Tags.Count);
    }

    [TestMethod]
    public void CreatePersisted_carries_favorite_and_tags()
    {
        Note note = Note.CreatePersisted(Id(1), Date, 0, "제목", "body", isFavorite: true, tags: new[] { "회의", "기획" }).Value;

        Assert.IsTrue(note.IsFavorite);
        CollectionAssert.AreEqual(new[] { "회의", "기획" }, note.Tags.ToArray());
    }

    [TestMethod]
    public void Restore_preserves_favorite_and_tags_in_sort_order()
    {
        Note first = Note.CreatePersisted(Id(1), Date, 0, "a", "b", isFavorite: true, tags: new[] { "x" }).Value;
        Note second = Note.CreatePersisted(Id(2), Date, 1, "c", "d", isFavorite: false, tags: new[] { "y", "z" }).Value;

        DomainResult<NoteSet> restored = NoteSet.Restore(Date, new[] { second, first });

        Assert.IsTrue(restored.IsSuccess);
        Assert.IsTrue(restored.Value.Notes[0].IsFavorite);
        CollectionAssert.AreEqual(new[] { "x" }, restored.Value.Notes[0].Tags.ToArray());
        Assert.IsFalse(restored.Value.Notes[1].IsFavorite);
        CollectionAssert.AreEqual(new[] { "y", "z" }, restored.Value.Notes[1].Tags.ToArray());
    }

    private static NoteId Id(int suffix) =>
        NoteId.Create(Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}")).Value;
}
