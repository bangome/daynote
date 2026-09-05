using System.Text.RegularExpressions;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The body-text file-link marker: <c>[[file:표시이름]]</c>. The editor is plain text with a mirrored
/// highlight layer, so a link must live IN the text as a marker (like the <c>-[]</c> todo markers) and
/// clicks are resolved by hit-testing the caret index against marker spans. Names resolve against the
/// note date's file list (newest match wins); a dangling name just opens the files tab.
/// </summary>
public static partial class FileLinkSyntax
{
    /// <summary>The drag-data format a 파일-tab card offers so the editor can drop it as a link.</summary>
    public const string DragFormat = "daynote/day-file";

    /// <summary>Matches one marker; group "name" is the display name (no brackets or newlines inside).</summary>
    [GeneratedRegex(@"\[\[file:(?<name>[^\]\r\n]+)\]\]", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();

    /// <summary>Builds the marker for a stored file's display name.</summary>
    public static string BuildMarker(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return $"[[file:{displayName}]]";
    }

    /// <summary>
    /// True when <paramref name="charIndex"/> falls inside a marker; returns that marker's file name.
    /// Both marker edges count as inside so a click anywhere on the rendered link resolves.
    /// </summary>
    public static bool TryGetLinkAt(string text, int charIndex, out string displayName)
    {
        displayName = string.Empty;
        if (string.IsNullOrEmpty(text) || charIndex < 0 || charIndex >= text.Length)
        {
            return false;
        }

        foreach (Match match in Pattern().Matches(text))
        {
            if (charIndex >= match.Index && charIndex < match.Index + match.Length)
            {
                displayName = match.Groups["name"].Value;
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
