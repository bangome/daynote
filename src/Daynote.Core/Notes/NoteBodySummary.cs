namespace Daynote.Core.Notes;

/// <summary>
/// Deterministic, culture-agnostic truncation of a note body for the read-only timeline view. This is a
/// plain character/line cap — never an AI summary — matching Daynote's local-only, no-network contract.
/// </summary>
public static class NoteBodySummary
{
    /// <summary>
    /// Produces a truncated preview of <paramref name="body"/> capped at <paramref name="maxLines"/> lines
    /// and <paramref name="maxChars"/> characters. Returns <c>("", false)</c> for a null/blank body. When
    /// either cap forces a cut, trailing whitespace is trimmed and an ellipsis is appended, and
    /// <c>IsTruncated</c> is <see langword="true"/>; otherwise the full trimmed body is returned untruncated.
    /// </summary>
    public static (string Summary, bool IsTruncated) Summarize(string? body, int maxChars = 220, int maxLines = 4)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (string.Empty, false);
        }

        string trimmed = body.Trim();

        string[] lines = trimmed.Split('\n');
        bool cutByLines = lines.Length > maxLines;
        string candidate = cutByLines ? string.Join('\n', lines.Take(maxLines)) : trimmed;

        bool cutByChars = candidate.Length > maxChars;
        if (cutByChars)
        {
            candidate = candidate[..maxChars];
        }

        if (cutByLines || cutByChars)
        {
            return (candidate.TrimEnd() + "…", true);
        }

        return (candidate, false);
    }
}
