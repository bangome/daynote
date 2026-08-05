using System.Text.RegularExpressions;
using Daynote.Core.Domain;
using Daynote.Core.Notes;

namespace Daynote.App.Notes;

/// <summary>
/// One inline hashtag occurrence found in a note body. Distinct from the per-note tag chips: this is a
/// <c>#프로젝트</c> token typed inline in the body. <see cref="Tag"/> is the tag text WITHOUT the leading
/// '#'; <see cref="CharIndex"/> is the absolute index of the '#' within the full note body (so the editor
/// can select/scroll to it); <see cref="LineText"/> is the trimmed source line kept for a preview.
/// </summary>
public readonly record struct TagOccurrence(
    Guid NoteId,
    LocalDate Date,
    string NoteTitle,
    string Tag,
    int LineIndex,
    int CharIndex,
    string LineText);

/// <summary>A distinct inline tag with its total occurrence <see cref="Count"/> and every occurrence.</summary>
public readonly record struct TagSummary(string Tag, int Count, IReadOnlyList<TagOccurrence> Occurrences);

/// <summary>
/// Pure parser for inline body hashtags, mirroring <see cref="TodoParsing"/>. A tag is a '#' immediately
/// followed by one or more letters/digits/underscores (<c>\p{L}</c> matches Hangul), with a lookbehind that
/// prevents matching a '#' embedded in a word or URL such as <c>foo#bar</c>. <see cref="Parse"/> walks every
/// note body line-by-line, tracking a running character offset so each occurrence carries the absolute body
/// index of its '#'; <see cref="Aggregate"/> groups occurrences into distinct tags (ordinal, case-sensitive)
/// sorted by count desc then tag asc.
/// </summary>
public static partial class TagParsing
{
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])#([\p{L}\p{N}_]+)")]
    private static partial Regex TagPattern();

    /// <summary>Emits one <see cref="TagOccurrence"/> per inline hashtag across the given notes, in discovery order.</summary>
    public static IReadOnlyList<TagOccurrence> Parse(IEnumerable<NoteSummary> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var occurrences = new List<TagOccurrence>();
        foreach (NoteSummary note in notes)
        {
            string body = note.Body ?? string.Empty;
            string[] lines = body.Split('\n');
            int offset = 0;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                foreach (Match match in TagPattern().Matches(line))
                {
                    occurrences.Add(new TagOccurrence(
                        note.Id,
                        note.LocalDate,
                        note.Title,
                        match.Groups[1].Value,
                        index,
                        offset + match.Index,
                        line.Trim()));
                }

                // Advance past this line and the '\n' that Split consumed so CharIndex stays absolute.
                offset += line.Length + 1;
            }
        }

        return occurrences;
    }

    /// <summary>Groups occurrences into distinct tags, ordered by occurrence count desc then tag asc (ordinal).</summary>
    public static IReadOnlyList<TagSummary> Aggregate(IReadOnlyList<TagOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        return [.. occurrences
            .GroupBy(o => o.Tag, StringComparer.Ordinal)
            .Select(group => new TagSummary(group.Key, group.Count(), [.. group]))
            .OrderByDescending(summary => summary.Count)
            .ThenBy(summary => summary.Tag, StringComparer.Ordinal)];
    }
}
