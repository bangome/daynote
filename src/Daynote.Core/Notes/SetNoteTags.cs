using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public sealed class SetNoteTags(INoteRepository repository)
{
    private readonly INoteRepository repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    /// <summary>
    /// Replaces a note's entire tag set. Input is normalized (trimmed, deduplicated, ordered, capped)
    /// before persistence, so the stored set matches the domain contract regardless of caller hygiene.
    /// </summary>
    public ValueTask<DayWorkspace> ExecuteAsync(
        LocalDate localDate,
        NoteId noteId,
        IEnumerable<string>? tags,
        CancellationToken cancellationToken = default)
    {
        if (!noteId.IsValid)
        {
            throw new ArgumentException("A note ID is required.", nameof(noteId));
        }

        DomainResult<IReadOnlyList<string>> normalized = NoteTags.Normalize(tags);
        if (!normalized.IsSuccess)
        {
            throw new ArgumentException(normalized.Error.Message, nameof(tags));
        }

        return repository.SetTagsAsync(localDate, noteId, normalized.Value, cancellationToken);
    }
}
