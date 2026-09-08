using Daynote.Core.Domain;
using Daynote.Core.Notes;

namespace Daynote.App.Notes;

/// <summary>
/// One note carrying one tag: what the 태그 panel shows under an expanded tag.
/// </summary>
/// <remarks>
/// <see cref="Preview"/> is the start of the note body, kept so a row says something about the note
/// rather than only naming it.
/// </remarks>
public readonly record struct TagOccurrence(
    Guid NoteId,
    LocalDate Date,
    string NoteTitle,
    string Tag,
    string Preview);

/// <summary>A distinct tag, how many notes carry it, and which ones.</summary>
public readonly record struct TagSummary(string Tag, int Count, IReadOnlyList<TagOccurrence> Occurrences);

/// <summary>
/// Builds the 태그 panel's list from the tags the user put on notes.
/// </summary>
/// <remarks>
/// This replaced a parser that scanned every note body for <c>#tag</c> tokens. There were two tag
/// systems — the chips under the note title, stored in <c>note_tags</c>, and inline tokens in the
/// prose — and the panel only ever showed the second, so a note tagged with the chips appeared
/// nowhere. Now there is one system, and it is the one the user can see and edit.
/// <para>
/// Ordering is by note count descending then tag ascending, so the tags in most use come first and
/// the rest are alphabetical rather than arbitrary.
/// </para>
/// </remarks>
public static class NoteTagIndex
{
    private const int PreviewMaxLength = 80;

    /// <summary>
    /// Groups <paramref name="links"/> by tag, resolving each against <paramref name="notes"/>. A link
    /// whose note is not in the list is dropped: the two queries are taken a moment apart, and a note
    /// deleted in between should not turn into a row that cannot be opened.
    /// </summary>
    public static IReadOnlyList<TagSummary> Build(
        IEnumerable<NoteSummary> notes,
        IEnumerable<NoteTagLink> links)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(links);

        Dictionary<Guid, NoteSummary> byId = notes
            .GroupBy(note => note.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var grouped = new Dictionary<string, List<TagOccurrence>>(StringComparer.Ordinal);
        foreach (NoteTagLink link in links)
        {
            if (!byId.TryGetValue(link.NoteId, out NoteSummary note))
            {
                continue;
            }

            if (!grouped.TryGetValue(link.Tag, out List<TagOccurrence>? occurrences))
            {
                occurrences = [];
                grouped[link.Tag] = occurrences;
            }

            occurrences.Add(new TagOccurrence(
                note.Id,
                note.LocalDate,
                note.Title,
                link.Tag,
                Preview(note.Body)));
        }

        return
        [
            .. grouped
                .Select(pair => new TagSummary(pair.Key, pair.Value.Count, pair.Value))
                .OrderByDescending(summary => summary.Count)
                .ThenBy(summary => summary.Tag, StringComparer.CurrentCulture),
        ];
    }

    /// <summary>The body's first line, trimmed and ellipsized so a row stays one line.</summary>
    private static string Preview(string? body)
    {
        string text = (body ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return text.Length > PreviewMaxLength
            ? string.Concat(text.AsSpan(0, PreviewMaxLength), "…")
            : text;
    }
}
