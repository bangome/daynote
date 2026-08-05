using Daynote.App.Shell.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

[TestClass]
public sealed class UrlLinkSyntaxTests
{
    [TestMethod]
    public void Pattern_MatchesHttpAndHttpsUrls()
    {
        string text = "앞 http://a.com 사이 https://ko.wikipedia.org/wiki/한글 뒤";

        var matches = UrlLinkSyntax.Pattern().Matches(text);

        Assert.AreEqual(2, matches.Count);
        Assert.AreEqual("http://a.com", matches[0].Value);
        Assert.AreEqual("https://ko.wikipedia.org/wiki/한글", matches[1].Value);
    }

    [TestMethod]
    public void Pattern_KeepsQueryAndFragmentButDropsTrailingPunctuation()
    {
        Assert.AreEqual(
            "https://x.com/path?a=1&b=2#frag",
            UrlLinkSyntax.Pattern().Match("https://x.com/path?a=1&b=2#frag.").Value);
        Assert.AreEqual("https://x.com", UrlLinkSyntax.Pattern().Match("끝? https://x.com!").Value);
        Assert.AreEqual("https://x.com", UrlLinkSyntax.Pattern().Match("(https://x.com)").Value);
    }

    [TestMethod]
    public void Pattern_DoesNotMatchBareOrOtherSchemes()
    {
        Assert.AreEqual(0, UrlLinkSyntax.Pattern().Matches("example.com").Count);
        Assert.AreEqual(0, UrlLinkSyntax.Pattern().Matches("ftp://a.com").Count);
        Assert.AreEqual(0, UrlLinkSyntax.Pattern().Matches("just http:// text").Count);
    }

    [TestMethod]
    public void TryGetUrlAt_ResolvesInsideTheUrlOnly()
    {
        string text = "앞 https://x.com 뒤";
        int start = text.IndexOf("https", StringComparison.Ordinal);
        int end = start + "https://x.com".Length;

        Assert.IsTrue(UrlLinkSyntax.TryGetUrlAt(text, start, out string url), "First char is inside.");
        Assert.AreEqual("https://x.com", url);
        Assert.IsTrue(UrlLinkSyntax.TryGetUrlAt(text, end - 1, out _), "Last char is inside.");
        Assert.IsFalse(UrlLinkSyntax.TryGetUrlAt(text, start - 1, out _), "Before the URL is outside.");
        Assert.IsFalse(UrlLinkSyntax.TryGetUrlAt(text, end, out _), "After the URL is outside.");
    }

    [TestMethod]
    public void TryGetUrlAt_PicksTheUrlUnderTheIndexAmongSeveral()
    {
        string text = "http://a.com http://b.com";

        Assert.IsTrue(UrlLinkSyntax.TryGetUrlAt(text, text.Length - 2, out string url));
        Assert.AreEqual("http://b.com", url);
    }

    [TestMethod]
    public void TryGetUrlAt_HandlesEmptyTextAndOutOfRangeIndexes()
    {
        Assert.IsFalse(UrlLinkSyntax.TryGetUrlAt("", 0, out _));
        Assert.IsFalse(UrlLinkSyntax.TryGetUrlAt("http://a.com", -1, out _));
        Assert.IsFalse(UrlLinkSyntax.TryGetUrlAt("http://a.com", 999, out _));
    }
}
