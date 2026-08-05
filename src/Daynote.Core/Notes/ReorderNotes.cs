using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public sealed class ReorderNotes(INoteRepository repository)
{
    public ValueTask<DayWorkspace> ExecuteAsync(
        LocalDate localDate,
        IReadOnlyList<NoteId> orderedIds,
        CancellationToken cancellationToken = default) =>
        repository.ReorderNotesAsync(localDate, orderedIds, cancellationToken);
}
