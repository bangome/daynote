using System.Text.RegularExpressions;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Bare <c>http(s)://…</c> URLs typed into the body. The editor is plain text with a mirrored highlight
/// layer, so — exactly like the <c>[[file:…]]</c> markers — a URL is recognized in place by pattern and a
/// click is resolved by hit-testing the caret index against the matched span. A trailing sentence mark
/// (<c>. , ; : ! ?</c>) or closing bracket is left out of the match so "봤어? https://x.com." still opens
/// the clean URL.
/// </summary>
public static partial class UrlLinkSyntax
{
    /// <summary>
    /// The URL pattern, shared with the editor's highlight layer so the highlighted span is exactly the
    /// span a click opens. The scheme is followed by any non-delimiter run that must END on a non-trailing
    /// character, which drops sentence punctuation and closing brackets from the tail of the match.
    /// </summary>
    public const string PatternText = @"https?://[^\s<>\[\]{}""'()]*[^\s<>\[\]{}""'().,;:!?]";

    /// <summary>Matches one bare URL; the whole match is the URL.</summary>
    [GeneratedRegex(PatternText, RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();

    /// <summary>
    /// True when <paramref name="charIndex"/> falls inside a URL; returns that URL. Both edges count as
    /// inside so a click anywhere on the rendered link resolves.
    /// </summary>
    public static bool TryGetUrlAt(string text, int charIndex, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrEmpty(text) || charIndex < 0 || charIndex >= text.Length)
        {
            return false;
        }

        foreach (Match match in Pattern().Matches(text))
        {
            if (charIndex >= match.Index && charIndex < match.Index + match.Length)
            {
                url = match.Value;
                return true;
            }

            if (match.Index > charIndex)
            {
                break;
            }
        }

        return false;
    }
}
