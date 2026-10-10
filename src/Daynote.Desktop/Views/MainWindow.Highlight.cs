using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Daynote.App.Shell.Product;

namespace Daynote.Desktop.Views;

/// <summary>
/// The editor's highlight layer: the note body drawn again underneath, with the shapes the app acts
/// on picked out.
/// </summary>
/// <remarks>
/// A marker or URL is clickable. Marking them is how the note says "I understood that" as it is
/// typed. A <c>-[ ]</c> line is plain text: to-dos are not in the body. Split by the
/// shared <see cref="BodyHighlightSyntax"/> so neither shell can quietly disagree about what counts.
/// <para>
/// The editor draws its own text in <c>Transparent</c> and this sits directly behind it, so the two
/// have to break lines identically or the caret drifts off its glyph. Everything that decides a
/// break — font, size, line height, wrapping, padding — is set once on the <c>editor</c> class and
/// copied here, and <c>EditorHighlightTests</c> measures the two against each other.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private ScrollViewer? _editorScroll;

    /// <summary>
    /// Keeps the two layers in step: the highlight follows the editor's scrolling, and is rebuilt
    /// whenever the text changes.
    /// </summary>
    private void AttachHighlight()
    {
        Editor.TemplateApplied += (_, e) =>
        {
            _editorScroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            if (_editorScroll is not null)
            {
                _editorScroll.ScrollChanged += (_, _) => HighlightScroll.Offset = _editorScroll.Offset;
            }
        };

        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                RebuildHighlight(Editor.Text);
            }
        };

        // The runs hold a resolved brush, not a resource reference, so a theme switch has to rebuild
        // them or the marks keep the other theme's accent (navy on the dark page).
        ActualThemeVariantChanged += (_, _) => RebuildHighlight(Editor.Text);

        RebuildHighlight(Editor.Text);
    }

    /// <summary>Rebuilds the highlight inlines from the current body.</summary>
    internal void RebuildHighlight(string? body)
    {
        Highlight.Inlines?.Clear();
        if (Highlight.Inlines is not { } inlines)
        {
            return;
        }

        foreach (BodyHighlightSpan span in BodyHighlightSyntax.Split(body))
        {
            inlines.Add(Build(span));
        }

        // A trailing newline gives the editor one more empty line than the block would otherwise
        // draw, and the layers would disagree about the height from there down.
        if (body is { Length: > 0 } text && text[^1] == '\n')
        {
            inlines.Add(new Run(" "));
        }
    }

    private Run Build(BodyHighlightSpan span)
    {
        var run = new Run(span.Text);
        if (span.Kind == BodyHighlightKind.Plain)
        {
            return run;
        }

        // Colour, and nothing that changes a glyph's width. The editor lays the caret out from its
        // own text at the normal weight; a SemiBold run here is wider than the same characters there,
        // so every caret position after the first mark on a line was off by the difference. Only
        // non-metric properties may be set on these runs — EditorHighlightTests pins that.
        run.Foreground = Brush("Daynote.Product.Brush.Accent", Brushes.RoyalBlue);
        run.TextDecorations = TextDecorations.Underline;
        return run;
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush : fallback;
}
