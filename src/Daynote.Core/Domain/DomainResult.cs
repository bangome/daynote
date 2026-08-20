namespace Daynote.Core.Domain;

public enum DomainErrorCode
{
    None = 0,
    InvalidLocalDate,
    InvalidNoteId,
    InvalidClipboardItemId,
    InvalidClipboardKind,
    InvalidSequenceNumber,
    InvalidClockSnapshot,
    InvalidSortOrder,
    InvalidNoteBody,
    DuplicateNoteId,
    NoteNotFound,
    InvalidReorder,
    NonContiguousSortOrder,
    DateMismatch,
    ProjectionIdentityRequired,
    ProjectionCannotBePersisted,
    InvalidNoteTag,
    TooManyNoteTags,
    InvalidRecoveryKey,
    InvalidKdfParameters,
    MalformedCiphertext,
    CiphertextAuthenticationFailed,
    InvalidSyncTimestamp,
}

public readonly record struct DomainError(DomainErrorCode Code, string Message)
{
    public static DomainError None { get; } = new(DomainErrorCode.None, string.Empty);
}

public readonly struct DomainResult<T>
{
    private readonly T? value;

    private DomainResult(T value)
    {
        this.value = value;
        IsSuccess = true;
        Error = DomainError.None;
    }

    private DomainResult(DomainError error)
    {
        value = default;
        IsSuccess = false;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T Value => IsSuccess
        ? value!
        : throw new InvalidOperationException($"A failed domain result has no value ({Error.Code}).");

    public DomainError Error { get; }

    public static DomainResult<T> Success(T value) => new(value);

    public static DomainResult<T> Failure(DomainErrorCode code, string message) =>
        new(new DomainError(code, message));
}
