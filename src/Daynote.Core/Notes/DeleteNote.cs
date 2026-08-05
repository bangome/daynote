using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public sealed class DeleteNote(INoteRepository repository)
{
    public ValueTask<DayWorkspace> ExecuteAsync(
        LocalDate localDate,
        NoteId noteId,
        CancellationToken cancellationToken = default) =>
        repository.DeleteNoteAsync(localDate, noteId, cancellationToken);
}
