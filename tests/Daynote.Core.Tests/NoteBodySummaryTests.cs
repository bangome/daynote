using Daynote.Core.Notes;

namespace Daynote.Core.Tests;

[TestClass]
public sealed class NoteBodySummaryTests
{
    [TestMethod]
    public void Short_body_is_returned_whole_and_untruncated()
    {
        (string summary, bool truncated) = NoteBodySummary.Summarize("A quick note.");

        Assert.AreEqual("A quick note.", summary);
        Assert.IsFalse(truncated);
    }

    [TestMethod]
    public void Long_single_line_is_capped_at_max_chars_with_an_ellipsis()
    {
        string body = new('x', 500);

        (string summary, bool truncated) = NoteBodySummary.Summarize(body, maxChars: 220, maxLines: 4);

        Assert.IsTrue(truncated);
        Assert.IsTrue(summary.EndsWith('…'));
        Assert.AreEqual(221, summary.Length);
    }

    [TestMethod]
    public void More_lines_than_max_lines_are_truncated()
    {
        string body = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}"));

        (string summary, bool truncated) = NoteBodySummary.Summarize(body, maxChars: 220, maxLines: 4);

        Assert.IsTrue(truncated);
        Assert.IsTrue(summary.EndsWith('…'));
        Assert.IsFalse(summary.Contains("line 5", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Null_body_yields_empty_and_untruncated()
    {
        (string summary, bool truncated) = NoteBodySummary.Summarize(null);

        Assert.AreEqual(string.Empty, summary);
        Assert.IsFalse(truncated);
    }

    [TestMethod]
    public void Whitespace_only_body_yields_empty_and_untruncated()
    {
        (string summary, bool truncated) = NoteBodySummary.Summarize("   \n\t  ");

        Assert.AreEqual(string.Empty, summary);
        Assert.IsFalse(truncated);
    }

    [TestMethod]
    public void Body_at_exactly_the_char_and_line_boundary_is_not_truncated()
    {
        // Four lines whose joined length is exactly maxChars: 4 lines of 54 chars + 3 newlines = 219.
        string line = new('x', 54);
        string body = string.Join('\n', Enumerable.Repeat(line, 4));

        (string summary, bool truncated) = NoteBodySummary.Summarize(body, maxChars: body.Length, maxLines: 4);

        Assert.AreEqual(body, summary);
        Assert.IsFalse(truncated);
    }
}
