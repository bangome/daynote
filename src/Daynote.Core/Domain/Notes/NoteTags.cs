namespace Daynote.Core.Domain.Notes;

/// <summary>
/// Normalizes a user-supplied tag list into the canonical replace-set form persisted for a note:
/// each tag is trimmed, empties are dropped, duplicates are removed keeping first occurrence, and
/// the surviving order is preserved. Bounds keep a single note's tag set small and indexable.
/// </summary>
public static class NoteTags
{
    public const int MaxCount = 20;
    public const int MaxLength = 50;

    public static DomainResult<IReadOnlyList<string>> Normalize(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return DomainResult<IReadOnlyList<string>>.Success([]);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>();
        foreach (string raw in tags)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.Length > MaxLength)
            {
                return DomainResult<IReadOnlyList<string>>.Failure(
                    DomainErrorCode.InvalidNoteTag,
                    "A note tag must not exceed the maximum length.");
            }

            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        if (normalized.Count > MaxCount)
        {
            return DomainResult<IReadOnlyList<string>>.Failure(
                DomainErrorCode.TooManyNoteTags,
                "A note must not exceed the maximum number of tags.");
        }

        return DomainResult<IReadOnlyList<string>>.Success(normalized);
    }
}
