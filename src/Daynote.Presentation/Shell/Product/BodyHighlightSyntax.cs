using System.Text.RegularExpressions;

namespace Daynote.App.Shell.Product;

/// <summary>What kind of thing a highlighted stretch of the note body is.</summary>
public enum BodyHighlightKind
{
    /// <summary>Ordinary text, highlighted in no way.</summary>
    Plain,

    /// <summary>A <c>[[file:…]]</c> marker or a URL — something clickable.</summary>
    Link,
}

/// <summary>One run of the body: its text, and what it is.</summary>
public readonly record struct BodyHighlightSpan(string Text, BodyHighlightKind Kind);

/// <summary>
/// Splits a note body into the runs an editor should mark up.
/// </summary>
/// <remarks>
/// The point of the marking is feedback, not decoration: these are exactly the shapes the app acts
/// on elsewhere — a marker or URL is clickable. A <c>-[ ]</c> line or a <c>(9/7)</c> stamp is not
/// one of them: to-dos live in the to-do editor, and the body is just text.
/// <para>
/// The pattern was written in the WPF editor's code-behind and lived only there. It is here now
/// because both shells draw the same body and there is no version of this that should differ between
/// them — a shape the Avalonia editor did not highlight was a shape it silently disagreed with the
/// rest of the app about.
/// </para>
/// </remarks>
public static partial class BodyHighlightSyntax
{
    /// <summary>
    /// A file marker or a URL.
    /// </summary>
    /// <remarks>
    /// There were arms for <c>-[ ]</c> checkboxes, <c>(9/7)</c> due stamps and inline <c>#tag</c>
    /// tokens. To-dos are their own items and tags are the chips under the note title, so all of
    /// those are prose now.
    /// </remarks>
    public const string PatternText =
        @"(?<link>\[\[file:[^\]\r\n]+\]\]|" + UrlLinkSyntax.PatternText + ")";

    [GeneratedRegex(PatternText, RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();

    /// <summary>
    /// The body as a run of spans, in order, covering every character exactly once. A body with
    /// nothing to mark comes back as a single <see cref="BodyHighlightKind.Plain"/> span, and an
    /// empty body as no spans at all.
    /// </summary>
    public static IReadOnlyList<BodyHighlightSpan> Split(string? body)
    {
        string text = body ?? string.Empty;
        if (text.Length == 0)
        {
            return [];
        }

        var spans = new List<BodyHighlightSpan>();
        int last = 0;

        foreach (Match match in Pattern().Matches(text))
        {
            if (match.Index > last)
            {
                spans.Add(new BodyHighlightSpan(text[last..match.Index], BodyHighlightKind.Plain));
            }

            spans.Add(new BodyHighlightSpan(match.Value, BodyHighlightKind.Link));
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            spans.Add(new BodyHighlightSpan(text[last..], BodyHighlightKind.Plain));
        }

        return spans;
    }
}
