using System.Text.RegularExpressions;

namespace Daynote.App.Shell.Product;

/// <summary>What kind of thing a highlighted stretch of the note body is.</summary>
public enum BodyHighlightKind
{
    /// <summary>Ordinary text, highlighted in no way.</summary>
    Plain,

    /// <summary>A checkbox, <c>-[ ]</c> or <c>-[x]</c>, which the todo panel picks up.</summary>
    Todo,

    /// <summary>A due date, <c>(9/7)</c> or <c>(9/7 15:00)</c>, which the todo panel reads.</summary>
    Due,

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
/// on elsewhere. A checkbox becomes a row in the todo panel, a date next to it becomes that row's
/// due date, a marker or URL is clickable. Highlighting them is how the note says "I understood
/// that" while it is being typed.
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
    /// Checkbox, then due date, then file marker or URL.
    /// </summary>
    /// <remarks>
    /// There was a fifth arm for inline <c>#tag</c> tokens. Tags are the chips under the note title
    /// now — one system, the one the user can see and edit — so a <c>#</c> in the prose is prose.
    /// </remarks>
    public const string PatternText =
        @"(?<todo>-\s?\[(?: |x|X)?\])"
        + @"|(?<due>\(\d{1,2}/\d{1,2}(?:\s+\d{1,2}:\d{2})?\))"
        + @"|(?<link>\[\[file:[^\]\r\n]+\]\]|" + UrlLinkSyntax.PatternText + ")";

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

            spans.Add(new BodyHighlightSpan(match.Value, KindOf(match)));
            last = match.Index + match.Length;
        }

        if (last < text.Length)
        {
            spans.Add(new BodyHighlightSpan(text[last..], BodyHighlightKind.Plain));
        }

        return spans;
    }

    private static BodyHighlightKind KindOf(Match match)
    {
        if (match.Groups["todo"].Success)
        {
            return BodyHighlightKind.Todo;
        }

        if (match.Groups["due"].Success)
        {
            return BodyHighlightKind.Due;
        }

        return BodyHighlightKind.Link;
    }
}
