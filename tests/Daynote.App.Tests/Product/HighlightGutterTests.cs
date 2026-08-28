using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Daynote.App.Shell.Product;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The editor body is a transparent-foreground <c>TextBox</c> over a highlight <c>TextBlock</c>. The
/// illusion only holds while both layers break lines in the same places.
/// </summary>
/// <remarks>
/// These exist because they didn't, and the bug they describe was reported from real use: typing
/// appeared one line above the caret, on some notes and not others. Identical <c>Padding</c> is not
/// enough — WPF's TextBox template gives its inner text view a 2px horizontal margin, so the editor
/// wraps 4px earlier than a TextBlock of the same width. Whether that changes a break point depends
/// on where the text happens to fall, which is why it looked document-specific rather than
/// systematic.
/// </remarks>
[TestClass]
public sealed class HighlightGutterTests
{
    private static readonly FontFamily Mono = new("Consolas, Cascadia Mono, Malgun Gothic");
    private const double FontSize = 13.5;
    private static readonly Thickness EditorPadding = new(16, 12, 16, 12);

    /// <summary>
    /// Korean meeting notes, the shape that reported the bug. Hangul breaks between any two
    /// characters, so a few pixels of width difference is enough to move a break point.
    /// </summary>
    private const string KoreanNotes = """
        1. 기관 로고 관련 이슈로 미열람 팝업(뷰어)의 차주 화요일 정기배포가 어려운 상황
         - XpERP 리뉴얼로 인해 미열람 공문 리스트, 팝업이 제외된 이후로 전자공문 열람율이 기존 10%대에서 3%대까지 하락한 상태. 미열람 공문 팝업을 도입했으나 열람율 회복이 충분히 되고 있지 않아 미열람  팝업(뷰어)의 빠른 배포가 필요한 상태.
          - 현재 API 개발은 되었으나 기관 로고가 ERP에서 자체적으로 보내지고 있는 상황으로, 공문발송서비스에서 보내기 위한 작업 필요. 해당 작업을 위해 해야 하는 업무들 존재하나, 목&금 AI교육으로 인해 하지인 대리 업무 어려운 상황으로 금주 배포가 어려움.
        -

        2. 엔딩 팝업 작업 진행 관련
         - 사장님 의견으로 도출되었던 사항으로, 업무 시작 시에는 팝업이 일제히 열리고 가장 마지막에 공문 팝업이 열리기 때문에 습관적으로 팝업을 모두 닫는 습관이 있는 것으로 추정하는 바, 업무 마감에 가까운 특정 시간(약 4시)에 미열람 공문 팝업을 띄워 열람율 향상에 기여하고자 함.
          - 해당 업무가 제대로 진행되고 있는지 확인 필요.
        """;

    /// <summary>
    /// One offscreen window, reused for every measurement and closed once. Text metrics need a
    /// realized visual tree, and a window per width both dominated the runtime and left enough live
    /// windows to break unrelated rendering tests.
    /// </summary>
    private Window window = null!;
    private Grid layers = null!;

    [TestInitialize]
    public void CreateHost()
    {
        layers = new Grid { VerticalAlignment = VerticalAlignment.Top };
        window = new Window
        {
            Width = 1100,
            Height = 1400,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -20000,
            Top = -20000,
            Content = new Grid { Children = { layers } },
        };
        window.Show();
    }

    [TestCleanup]
    public void CloseHost() => window.Close();

    [STATestMethod]
    public void TextViewGutter_IsSymmetricAndHorizontalOnly()
    {
        var box = NewEditor("x");
        Lay(box, NewHighlight("x", EditorPadding), 600);

        Thickness gutter = EditorCardView.TextViewGutter(box);

        // Not an assertion about WPF's chosen number so much as about its shape: the vertical layers
        // already line up, and a lopsided gutter would shift the highlight sideways rather than only
        // changing where it wraps.
        Assert.AreEqual(gutter.Left, gutter.Right, "A lopsided gutter would offset the highlight horizontally.");
        Assert.AreEqual(0d, gutter.Top);
        Assert.AreEqual(0d, gutter.Bottom);
        Assert.IsGreaterThan(0d, gutter.Left, "Without a gutter there would be nothing to compensate for.");
    }

    [STATestMethod]
    public void AnEquallyPaddedHighlight_IsWiderThanTheEditorsText()
    {
        // The mechanism, stated directly. If WPF ever drops the gutter this fails and the
        // compensation can go.
        var box = NewEditor(KoreanNotes);
        var block = NewHighlight(KoreanNotes, EditorPadding);
        Lay(box, block, 700);

        Assert.IsLessThan(
            block.ActualWidth - block.Padding.Left - block.Padding.Right,
            EditorTextWidth(box),
            "Matching Padding was expected to leave the editor narrower; that difference is the bug.");
    }

    [STATestMethod]
    public void WithoutTheGutter_TheCaretAndTheGlyphDisagree()
    {
        // Guards the test below from passing vacuously: if the measurement could not see drift at
        // all, it would report success no matter what the code did.
        int widthsThatDrift = 0;
        foreach (double width in Widths())
        {
            var box = NewEditor(KoreanNotes);
            var block = NewHighlight(KoreanNotes, EditorPadding);
            Lay(box, block, width);
            if (CountDriftingCharacters(box, block) > 0)
            {
                widthsThatDrift++;
            }
        }

        Assert.IsGreaterThan(0, widthsThatDrift, "The uncompensated layers were expected to disagree somewhere.");
    }

    [STATestMethod]
    public void WithTheGutter_EveryCharacterRendersOnItsCaretsLine()
    {
        // Many widths, because the bug is a boundary effect: any single width can pass by luck.
        foreach (double width in Widths())
        {
            var box = NewEditor(KoreanNotes);
            Thickness gutter = EditorCardView.TextViewGutter(NewEditorInTree());
            var block = NewHighlight(
                KoreanNotes,
                new Thickness(
                    EditorPadding.Left + gutter.Left,
                    EditorPadding.Top + gutter.Top,
                    EditorPadding.Right + gutter.Right,
                    EditorPadding.Bottom + gutter.Bottom));
            Lay(box, block, width);

            int drifted = CountDriftingCharacters(box, block);
            Assert.AreEqual(
                0,
                drifted,
                $"At {width:F0}px, {drifted} characters render on a different line than their caret.");
        }
    }

    private static IEnumerable<double> Widths()
    {
        for (double width = 380; width <= 1000; width += 40)
        {
            yield return width;
        }
    }

    private System.Windows.Controls.TextBox NewEditorInTree()
    {
        var probe = NewEditor("x");
        Lay(probe, NewHighlight("x", EditorPadding), 600);
        return probe;
    }

    private int CountDriftingCharacters(System.Windows.Controls.TextBox box, TextBlock block)
    {
        double lineHeight = LineHeight(box);
        var run = (Run)block.Inlines.FirstInline;
        int drifted = 0;
        for (int i = 0; i <= KoreanNotes.Length; i++)
        {
            TextPointer? pointer = run.ContentStart.GetPositionAtOffset(i);
            if (pointer is null)
            {
                break;
            }

            double delta = box.GetRectFromCharacterIndex(i).Top
                - pointer.GetCharacterRect(LogicalDirection.Forward).Top;
            if (Math.Abs(delta) > lineHeight / 2)
            {
                drifted++;
            }
        }

        return drifted;
    }

    private void Lay(System.Windows.Controls.TextBox box, TextBlock block, double width)
    {
        layers.Children.Clear();
        layers.Width = width;
        layers.Children.Add(block);
        layers.Children.Add(box);
        layers.UpdateLayout();
    }

    private static double LineHeight(System.Windows.Controls.TextBox box)
    {
        double first = box.GetRectFromCharacterIndex(box.GetCharacterIndexFromLineIndex(0)).Top;
        double second = box.GetRectFromCharacterIndex(box.GetCharacterIndexFromLineIndex(1)).Top;
        return second - first;
    }

    private static double EditorTextWidth(System.Windows.Controls.TextBox box)
    {
        Thickness gutter = EditorCardView.TextViewGutter(box);
        return box.ActualWidth - box.Padding.Left - box.Padding.Right - gutter.Left - gutter.Right;
    }

    private static System.Windows.Controls.TextBox NewEditor(string text) => new()
    {
        Text = text.Replace("\r\n", "\n"),
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        AcceptsTab = true,
        BorderThickness = new Thickness(0),
        Padding = EditorPadding,
        FontFamily = Mono,
        FontSize = FontSize,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalContentAlignment = VerticalAlignment.Top,
    };

    private static TextBlock NewHighlight(string text, Thickness padding)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Padding = padding,
            FontFamily = Mono,
            FontSize = FontSize,
        };
        block.Inlines.Add(new Run(text.Replace("\r\n", "\n")));
        return block;
    }
}
