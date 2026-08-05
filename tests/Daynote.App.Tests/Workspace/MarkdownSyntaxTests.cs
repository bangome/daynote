using Daynote.App.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class MarkdownSyntaxTests
{
    [TestMethod]
    public void ToggleBold_WrapsThenUnwrapsSelection()
    {
        MarkdownEdit wrapped = MarkdownSyntax.ToggleBold("word", 0, 4);
        Assert.AreEqual("**word**", wrapped.Text);

        MarkdownEdit unwrapped = MarkdownSyntax.ToggleBold(wrapped.Text, wrapped.SelectionStart, wrapped.SelectionLength);
        Assert.AreEqual("word", unwrapped.Text);
    }

    [TestMethod]
    public void ToggleItalic_UsesSingleAsterisk()
    {
        MarkdownEdit edit = MarkdownSyntax.ToggleItalic("hi", 0, 2);
        Assert.AreEqual("*hi*", edit.Text);
    }

    [TestMethod]
    public void ToggleBulletedList_PrefixesEachSelectedLine()
    {
        MarkdownEdit edit = MarkdownSyntax.ToggleBulletedList("one\ntwo", 0, 7);
        Assert.AreEqual("- one\n- two", edit.Text);
    }

    [TestMethod]
    public void ToggleNumberedList_NumbersSequentially()
    {
        MarkdownEdit edit = MarkdownSyntax.ToggleNumberedList("a\nb", 0, 3);
        Assert.AreEqual("1. a\n2. b", edit.Text);
    }

    [TestMethod]
    public void ToggleInlineCode_WrapsWithBacktick()
    {
        MarkdownEdit edit = MarkdownSyntax.ToggleInlineCode("x", 0, 1);
        Assert.AreEqual("`x`", edit.Text);
    }
}
