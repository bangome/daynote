using System.Collections.ObjectModel;

namespace Daynote.Core.Domain.Notes;

public sealed class NoteSet
{
    private readonly Note[] notes;
    private readonly ReadOnlyCollection<Note> view;

    private NoteSet(LocalDate localDate, Note[] notes)
    {
        LocalDate = localDate;
        this.notes = notes;
        view = Array.AsReadOnly(notes);
    }

    public LocalDate LocalDate { get; }

    public IReadOnlyList<Note> Notes => view;

    public bool IsProjectionOnly => notes.Length == 1 && notes[0].IsProjection;

    public static NoteSet Empty(LocalDate localDate) =>
        new(localDate, [Note.Projection(localDate)]);

    public static DomainResult<NoteSet> Restore(LocalDate localDate, IEnumerable<Note>? persistedNotes)
    {
        if (persistedNotes is null)
        {
            return DomainResult<NoteSet>.Failure(
                DomainErrorCode.NonContiguousSortOrder,
                "A persisted note snapshot is required.");
        }

        Note[] restored = persistedNotes.OrderBy(static note => note.SortOrder).ToArray();
        if (restored.Length == 0)
        {
            return DomainResult<NoteSet>.Success(Empty(localDate));
        }

        var ids = new HashSet<NoteId>();
        for (int index = 0; index < restored.Length; index++)
        {
            Note note = restored[index];
            if (note.IsProjection || !note.IsPersistable || note.Id is not NoteId id)
            {
                return DomainResult<NoteSet>.Failure(
                    DomainErrorCode.ProjectionCannotBePersisted,
                    "Projection notes cannot appear in persisted snapshots.");
            }

            if (note.LocalDate != localDate)
            {
                return DomainResult<NoteSet>.Failure(
                    DomainErrorCode.DateMismatch,
                    "Every note in a set must have the selected local date.");
            }

            if (!ids.Add(id))
            {
                return DomainResult<NoteSet>.Failure(
                    DomainErrorCode.DuplicateNoteId,
                    "Note IDs must be unique within a date.");
            }

            if (note.SortOrder != index)
            {
                return DomainResult<NoteSet>.Failure(
                    DomainErrorCode.NonContiguousSortOrder,
                    "Persisted note orders must be contiguous and zero-based.");
            }
        }

        return DomainResult<NoteSet>.Success(new NoteSet(localDate, restored));
    }

    public DomainResult<NoteSet> EditBody(int sortOrder, string body, NoteId? materializedProjectionId = null)
    {
        DomainResult<int> indexResult = FindOrder(sortOrder);
        if (!indexResult.IsSuccess)
        {
            return DomainResult<NoteSet>.Failure(indexResult.Error.Code, indexResult.Error.Message);
        }

        int index = indexResult.Value;
        Note target = notes[index];
        if (target.IsProjection)
        {
            if (body == string.Empty)
            {
                return DomainResult<NoteSet>.Success(this);
            }

            DomainResult<Note> materialized = MaterializeProjection(materializedProjectionId, body, null);
            return materialized.IsSuccess
                ? DomainResult<NoteSet>.Success(new NoteSet(LocalDate, [materialized.Value]))
                : DomainResult<NoteSet>.Failure(materialized.Error.Code, materialized.Error.Message);
        }

        Note[] changed = [.. notes];
        changed[index] = target.WithBody(body);
        return DomainResult<NoteSet>.Success(new NoteSet(LocalDate, changed));
    }

    public DomainResult<NoteSet> RenameTitle(
        int sortOrder,
        string title,
        NoteId? materializedProjectionId = null)
    {
        DomainResult<int> indexResult = FindOrder(sortOrder);
        if (!indexResult.IsSuccess)
        {
            return DomainResult<NoteSet>.Failure(indexResult.Error.Code, indexResult.Error.Message);
        }

        int index = indexResult.Value;
        Note target = notes[index];
        if (target.IsProjection)
        {
            if (string.Equals(title, target.Title, StringComparison.Ordinal))
            {
                return DomainResult<NoteSet>.Success(this);
            }

            DomainResult<Note> materialized = MaterializeProjection(materializedProjectionId, string.Empty, title);
            return materialized.IsSuccess
                ? DomainResult<NoteSet>.Success(new NoteSet(LocalDate, [materialized.Value]))
                : DomainResult<NoteSet>.Failure(materialized.Error.Code, materialized.Error.Message);
        }

        Note[] changed = [.. notes];
        changed[index] = target.WithCustomTitle(title);
        return DomainResult<NoteSet>.Success(new NoteSet(LocalDate, changed));
    }

    public DomainResult<NoteSet> Add(NoteId newNoteId, NoteId? materializedProjectionId = null)
    {
        if (!newNoteId.IsValid)
        {
            return DomainResult<NoteSet>.Failure(DomainErrorCode.InvalidNoteId, "A note ID cannot be empty.");
        }

        if (IsProjectionOnly)
        {
            DomainResult<Note> first = MaterializeProjection(materializedProjectionId, string.Empty, null);
            if (!first.IsSuccess)
            {
                return DomainResult<NoteSet>.Failure(first.Error.Code, first.Error.Message);
            }

            if (first.Value.Id == newNoteId)
            {
                return DomainResult<NoteSet>.Failure(
                    DomainErrorCode.DuplicateNoteId,
                    "The materialized and added note IDs must be different.");
            }

            Note second = Note.CreatePersisted(newNoteId, LocalDate, 1, null, string.Empty).Value;
            return DomainResult<NoteSet>.Success(new NoteSet(LocalDate, [first.Value, second]));
        }

        if (notes.Any(note => note.Id == newNoteId))
        {
            return DomainResult<NoteSet>.Failure(
                DomainErrorCode.DuplicateNoteId,
                "Note IDs must be unique within a date.");
        }

        Note added = Note.CreatePersisted(newNoteId, LocalDate, notes.Length, null, string.Empty).Value;
        return DomainResult<NoteSet>.Success(new NoteSet(LocalDate, [.. notes, added]));
    }

    public DomainResult<NoteSet> Delete(NoteId noteId)
    {
        if (!noteId.IsValid)
        {
            return DomainResult<NoteSet>.Failure(DomainErrorCode.InvalidNoteId, "A note ID cannot be empty.");
        }

        int index = Array.FindIndex(notes, note => note.Id == noteId);
        if (index < 0)
        {
            return DomainResult<NoteSet>.Failure(DomainErrorCode.NoteNotFound, "The note was not found.");
        }

        if (notes.Length == 1)
        {
            return DomainResult<NoteSet>.Success(Empty(LocalDate));
        }

        Note[] compacted = notes
            .Where(note => note.Id != noteId)
            .Select(static (note, order) => note.WithSortOrder(order))
            .ToArray();
        return DomainResult<NoteSet>.Success(new NoteSet(LocalDate, compacted));
    }

    public DomainResult<NoteSet> Reorder(IReadOnlyList<NoteId>? orderedIds)
    {
        if (IsProjectionOnly || orderedIds is null || orderedIds.Count != notes.Length)
        {
            return InvalidReorder();
        }

        var existing = notes.ToDictionary(static note => note.Id!.Value);
        var seen = new HashSet<NoteId>();
        var reordered = new Note[notes.Length];
        for (int index = 0; index < orderedIds.Count; index++)
        {
            NoteId id = orderedIds[index];
            if (!id.IsValid || !seen.Add(id) || !existing.TryGetValue(id, out Note? note))
            {
                return InvalidReorder();
            }

            reordered[index] = note.WithSortOrder(index);
        }

        return DomainResult<NoteSet>.Success(new NoteSet(LocalDate, reordered));
    }

    private static DomainResult<NoteSet> InvalidReorder() =>
        DomainResult<NoteSet>.Failure(
            DomainErrorCode.InvalidReorder,
            "A reorder must contain every current note ID exactly once.");

    private DomainResult<int> FindOrder(int sortOrder) =>
        sortOrder < 0 || sortOrder >= notes.Length || notes[sortOrder].SortOrder != sortOrder
            ? DomainResult<int>.Failure(
                DomainErrorCode.InvalidSortOrder,
                "The requested sort order does not exist in this note set.")
            : DomainResult<int>.Success(sortOrder);

    private DomainResult<Note> MaterializeProjection(
        NoteId? materializedProjectionId,
        string body,
        string? customTitle)
    {
        if (materializedProjectionId is not NoteId id || !id.IsValid)
        {
            return DomainResult<Note>.Failure(
                DomainErrorCode.ProjectionIdentityRequired,
                "Materializing the projection requires a nonempty note ID.");
        }

        return Note.CreatePersisted(id, LocalDate, 0, customTitle, body);
    }
}
