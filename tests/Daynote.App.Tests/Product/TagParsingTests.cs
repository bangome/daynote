using Daynote.App.Notes;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>Pure tests for the inline-hashtag grammar, occurrence indexing, and count aggregation.</summary>
[TestClass]
public sealed class TagParsingTests
{
    private static NoteSummary Note(string body, string title = "노트 1", string iso = "2026-07-21") =>
        new(Guid.NewGuid(), LocalDate.Parse(iso).Value, title, body, 0, false);

    [TestMethod]
    public void Parse_DetectsMultipleTagsPerLine()
    {
        IReadOnlyList<TagOccurrence> tags = TagParsing.Parse([Note("planning #alpha and #beta today")]);

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, tags.Select(t => t.Tag).ToArray());
    }

    [TestMethod]
    public void Parse_MatchesHangulTags()
    {
        IReadOnlyList<TagOccurrence> tags = TagParsing.Parse([Note("#프로젝트 회의")]);

        Assert.AreEqual(1, tags.Count);
        Assert.AreEqual("프로젝트", tags[0].Tag);
    }

    [TestMethod]
    public void Parse_IgnoresHashInsideWord()
    {
        IReadOnlyList<TagOccurrence> tags = TagParsing.Parse([Note("visit foo#bar and http://x/y#frag")]);

        Assert.AreEqual(0, tags.Count);
    }

    [TestMethod]
    public void Parse_IgnoresBareHash()
    {
        IReadOnlyList<TagOccurrence> tags = TagParsing.Parse([Note("# heading\nplain # text")]);

        Assert.AreEqual(0, tags.Count);
    }

    [TestMethod]
    public void Parse_CharIndexPointsAtHashInFullBody()
    {
        var body = "line one\nsecond #tag here";
        TagOccurrence tag = TagParsing.Parse([Note(body)]).Single();

        Assert.AreEqual(1, tag.LineIndex);
        // The substring at CharIndex must start with '#' + the tag.
        StringAssert.StartsWith(body[tag.CharIndex..], "#tag");
    }

    [TestMethod]
    public void Parse_TrimsLineTextForPreview()
    {
        TagOccurrence tag = TagParsing.Parse([Note("   #tag surrounded   ")]).Single();

        Assert.AreEqual("#tag surrounded", tag.LineText);
    }

    [TestMethod]
    public void Aggregate_CountsAndSortsByCountThenTag()
    {
        var notes = new[]
        {
            Note("#work #work #idea", iso: "2026-07-21"),
            Note("#idea #work", iso: "2026-07-20"),
        };

        IReadOnlyList<TagSummary> summaries = TagParsing.Aggregate(TagParsing.Parse(notes));

        // work=3, idea=2 -> sorted by count desc.
        CollectionAssert.AreEqual(new[] { "work", "idea" }, summaries.Select(s => s.Tag).ToArray());
        Assert.AreEqual(3, summaries[0].Count);
        Assert.AreEqual(2, summaries[1].Count);
    }

    [TestMethod]
    public void Aggregate_SortsEqualCountsByTagAscending()
    {
        IReadOnlyList<TagSummary> summaries = TagParsing.Aggregate(TagParsing.Parse([Note("#beta #alpha")]));

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, summaries.Select(s => s.Tag).ToArray());
    }

    [TestMethod]
    public void Aggregate_TreatsCaseAsDistinct()
    {
        IReadOnlyList<TagSummary> summaries = TagParsing.Aggregate(TagParsing.Parse([Note("#Tag #tag")]));

        Assert.AreEqual(2, summaries.Count);
    }

    [TestMethod]
    public void Aggregate_KeepsOccurrencesInDiscoveryOrder()
    {
        var notes = new[]
        {
            Note("#a first", title: "N1", iso: "2026-07-21"),
            Note("#a second", title: "N2", iso: "2026-07-20"),
        };

        TagSummary summary = TagParsing.Aggregate(TagParsing.Parse(notes)).Single();

        Assert.AreEqual(2, summary.Count);
        Assert.AreEqual("N1", summary.Occurrences[0].NoteTitle);
        Assert.AreEqual("N2", summary.Occurrences[1].NoteTitle);
    }
}
