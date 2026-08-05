namespace Daynote.Infrastructure.Notes;

public enum NoteWriteOperation
{
    Create,
    Reorder,
    Delete,
    Save,
}

public interface INoteWriteInterceptor
{
    void BeforeWrite(NoteWriteOperation operation);

    void AfterSourceWrite(NoteWriteOperation operation)
    {
    }
}
