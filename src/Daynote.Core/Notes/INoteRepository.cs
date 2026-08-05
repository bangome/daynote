using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public interface INoteRepository
{
    ValueTask<NoteSet> GetDayWorkspaceAsync(LocalDate localDate, CancellationToken cancellationToken = default);

    ValueTask<DayWorkspace> GetDayWorkspaceStateAsync(LocalDate localDate, CancellationToken cancellationToken = default);

    ValueTask<DayWorkspace> CreateNoteAsync(
        LocalDate localDate,
        NoteId projectionId,
        NoteId newNoteId,
        CancellationToken cancellationToken = default);

    ValueTask<DayWorkspace> ReorderNotesAsync(
        LocalDate localDate,
        IReadOnlyList<NoteId> orderedIds,
        CancellationToken cancellationToken = default);

    ValueTask<DayWorkspace> DeleteNoteAsync(
        LocalDate localDate,
        NoteId noteId,
        CancellationToken cancellationToken = default);

    ValueTask<NoteSaveReceipt> SaveNoteAsync(
        NoteSaveRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DayWorkspace> ToggleFavoriteAsync(
        LocalDate localDate,
        NoteId noteId,
        CancellationToken cancellationToken = default);

    ValueTask<DayWorkspace> SetTagsAsync(
        LocalDate localDate,
        NoteId noteId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates, in one read-only query, the per-date content rollup for every date in the given month
    /// that holds a note, clipboard item, or file. Ordered by date ascending; empty dates are omitted.
    /// </summary>
    ValueTask<IReadOnlyList<DateContentSummary>> GetMonthContentSummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates every persisted note across all dates, ordered by local date descending then sort order
    /// ascending. Tag-free projection for cross-date consumers (e.g. the todo panel).
    /// </summary>
    ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="GetAllNotesAsync(CancellationToken)"/> but bounded to the inclusive local-date range
    /// [<paramref name="from"/>, <paramref name="to"/>].
    /// </summary>
    ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(
        LocalDate from,
        LocalDate to,
        CancellationToken cancellationToken = default);
}

public readonly record struct NoteSaveRequest(
    NoteId Id,
    LocalDate LocalDate,
    string Title,
    string Body,
    int Revision,
    bool IsNew,
    bool HasCustomTitle)
{
    public NoteSaveRequest Validate()
    {
        if (!Id.IsValid) throw new ArgumentException("A note ID is required.", nameof(Id));
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentNullException.ThrowIfNull(Body);
        if (Revision < 0) throw new ArgumentOutOfRangeException(nameof(Revision));
        return this;
    }
}

public readonly record struct NoteSaveReceipt(int Revision, bool IsPersisted = true);

public enum NoteFailureCode
{
    RevisionConflict,
    StorageUnavailable,
}

public readonly record struct RecoverableNoteError(NoteFailureCode Code, string Message);

public sealed class RecoverableNoteException : Exception
{
    public RecoverableNoteException(NoteFailureCode code)
        : base(code == NoteFailureCode.RevisionConflict
            ? "The note changed elsewhere. Retry after resolving the conflict."
            : "The note could not be saved. Retry when storage is available.")
    {
        Code = code;
    }

    public NoteFailureCode Code { get; }

    public RecoverableNoteError ToError() => new(Code, Message);
}
