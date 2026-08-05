using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Tests;

[TestClass]
public sealed class NoteTagsTests
{
    [TestMethod]
    public void Normalize_trims_drops_empty_deduplicates_and_preserves_first_occurrence_order()
    {
        DomainResult<IReadOnlyList<string>> result = NoteTags.Normalize(
            new[] { "  회의 ", "기획", "", "   ", "회의", "\tbuild\t", "기획" });

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { "회의", "기획", "build" }, result.Value.ToArray());
    }

    [TestMethod]
    public void Normalize_null_yields_empty_set()
    {
        DomainResult<IReadOnlyList<string>> result = NoteTags.Normalize(null);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, result.Value.Count);
    }

    [TestMethod]
    public void Normalize_rejects_a_tag_longer_than_the_length_cap()
    {
        DomainResult<IReadOnlyList<string>> result = NoteTags.Normalize(
            new[] { new string('t', NoteTags.MaxLength + 1) });

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidNoteTag, result.Error.Code);
    }

    [TestMethod]
    public void Normalize_accepts_a_tag_at_the_length_cap()
    {
        DomainResult<IReadOnlyList<string>> result = NoteTags.Normalize(
            new[] { new string('t', NoteTags.MaxLength) });

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value.Count);
    }

    [TestMethod]
    public void Normalize_rejects_more_than_the_count_cap_after_deduplication()
    {
        string[] tooMany = Enumerable.Range(0, NoteTags.MaxCount + 1)
            .Select(static index => $"tag{index}").ToArray();

        DomainResult<IReadOnlyList<string>> result = NoteTags.Normalize(tooMany);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(DomainErrorCode.TooManyNoteTags, result.Error.Code);
    }

    [TestMethod]
    public void Normalize_accepts_exactly_the_count_cap()
    {
        string[] atCap = Enumerable.Range(0, NoteTags.MaxCount)
            .Select(static index => $"tag{index}").ToArray();

        DomainResult<IReadOnlyList<string>> result = NoteTags.Normalize(atCap);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(NoteTags.MaxCount, result.Value.Count);
    }
}
