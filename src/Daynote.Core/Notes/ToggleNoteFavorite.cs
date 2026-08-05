using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public sealed class ToggleNoteFavorite(INoteRepository repository)
{
    private readonly INoteRepository repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    public ValueTask<DayWorkspace> ExecuteAsync(
        LocalDate localDate,
        NoteId noteId,
        CancellationToken cancellationToken = default)
    {
        if (!noteId.IsValid)
        {
            throw new ArgumentException("A note ID is required.", nameof(noteId));
        }

        return repository.ToggleFavoriteAsync(localDate, noteId, cancellationToken);
    }
}
