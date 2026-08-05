namespace Daynote.Core.Notes;

public sealed class SaveNote(INoteRepository repository)
{
    public ValueTask<NoteSaveReceipt> ExecuteAsync(
        NoteSaveRequest request,
        CancellationToken cancellationToken = default) =>
        repository.SaveNoteAsync(request.Validate(), cancellationToken);
}
