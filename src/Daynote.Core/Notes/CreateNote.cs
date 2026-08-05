using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public sealed class CreateNote(INoteRepository repository, Func<NoteId> nextId)
{
    public ValueTask<DayWorkspace> ExecuteAsync(
        LocalDate localDate,
        CancellationToken cancellationToken = default)
    {
        // The empty-day projection is a single virtual "Note 1"; the + button turns it into exactly ONE
        // real note (never Note 1 + Note 2). Passing an invalid projection id tells the repository to
        // create just one note on an empty day, and to plainly append on a day that already has notes.
        return repository.CreateNoteAsync(localDate, default, nextId(), cancellationToken);
    }
}
