namespace Daynote.Core.Domain.Notes;

public readonly record struct NoteId
{
    private NoteId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public bool IsValid => Value != Guid.Empty;

    public static DomainResult<NoteId> Create(Guid value) => value == Guid.Empty
        ? DomainResult<NoteId>.Failure(DomainErrorCode.InvalidNoteId, "A note ID cannot be empty.")
        : DomainResult<NoteId>.Success(new NoteId(value));

    public override string ToString() => Value.ToString("D");
}
