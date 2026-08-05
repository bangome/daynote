using Daynote.App.Shell.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

[TestClass]
public sealed class FileLinkSyntaxTests
{
    [TestMethod]
    public void BuildMarker_WrapsTheDisplayName()
    {
        Assert.AreEqual("[[file:스크린샷.png]]", FileLinkSyntax.BuildMarker("스크린샷.png"));
        Assert.ThrowsExactly<ArgumentException>(() => FileLinkSyntax.BuildMarker(" "));
    }

    [TestMethod]
    public void Pattern_MatchesMarkersAndExtractsTheName()
    {
        string text = "앞 [[file:a.png]] 사이 [[file:보고서 (2).docx]] 뒤";

        var matches = FileLinkSyntax.Pattern().Matches(text);

        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual("a.png", matches[0].Groups["name"].Value);
        Assert.AreEqual("보고서 (2).docx", matches[1].Groups["name"].Value);
    }

    [TestMethod]
    public void Pattern_DoesNotMatchAcrossLinesOrEmptyNames()
    {
        Assert.AreEqual(0, FileLinkSyntax.Pattern().Matches("[[file:]]").Count);
        Assert.AreEqual(0, FileLinkSyntax.Pattern().Matches("[[file:a\nb]]").Count);
    }

    [TestMethod]
    public void TryGetLinkAt_ResolvesInsideTheMarkerOnly()
    {
        string text = "앞 [[file:a.png]] 뒤";
        int start = text.IndexOf("[[", StringComparison.Ordinal);
        int end = text.IndexOf("]]", StringComparison.Ordinal) + 2;

        Assert.IsTrue(FileLinkSyntax.TryGetLinkAt(text, start, out string name), "First bracket is inside.");
        Assert.AreEqual("a.png", name);
        Assert.IsTrue(FileLinkSyntax.TryGetLinkAt(text, end - 1, out _), "Last bracket is inside.");
        Assert.IsFalse(FileLinkSyntax.TryGetLinkAt(text, start - 1, out _), "Before the marker is outside.");
        Assert.IsFalse(FileLinkSyntax.TryGetLinkAt(text, end, out _), "After the marker is outside.");
    }

    [TestMethod]
    public void TryGetLinkAt_PicksTheMarkerUnderTheIndexAmongSeveral()
    {
        string text = "[[file:a.png]] [[file:b.png]]";

        Assert.IsTrue(FileLinkSyntax.TryGetLinkAt(text, text.Length - 2, out string name));
        Assert.AreEqual("b.png", name);
    }

    [TestMethod]
    public void TryGetLinkAt_HandlesEmptyTextAndOutOfRangeIndexes()
    {
        Assert.IsFalse(FileLinkSyntax.TryGetLinkAt("", 0, out _));
        Assert.IsFalse(FileLinkSyntax.TryGetLinkAt("[[file:a.png]]", -1, out _));
        Assert.IsFalse(FileLinkSyntax.TryGetLinkAt("[[file:a.png]]", 999, out _));
    }
}
