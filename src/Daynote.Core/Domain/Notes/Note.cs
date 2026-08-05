namespace Daynote.Core.Domain.Notes;

public sealed class Note
{
    private readonly string? customTitle;
    private readonly string[] tags;

    private Note(
        NoteId? id,
        LocalDate localDate,
        int sortOrder,
        string? customTitle,
        string body,
        bool isProjection,
        bool isFavorite,
        string[] tags)
    {
        Id = id;
        LocalDate = localDate;
        SortOrder = sortOrder;
        this.customTitle = customTitle;
        Body = body;
        IsProjection = isProjection;
        IsFavorite = isFavorite;
        this.tags = tags;
    }

    public NoteId? Id { get; }

    public LocalDate LocalDate { get; }

    public int SortOrder { get; }

    public int DisplayNumber => SortOrder + 1;

    public string Title => customTitle ?? UntitledNote.TitleFor(DisplayNumber);

    public string Body { get; }

    public bool HasCustomTitle => customTitle is not null;

    public bool IsProjection { get; }

    public bool IsPersistable => !IsProjection;

    public bool IsIndexable => !IsProjection;

    public bool IsFavorite { get; }

    public IReadOnlyList<string> Tags => tags;

    public static DomainResult<Note> CreatePersisted(
        NoteId id,
        LocalDate localDate,
        int sortOrder,
        string? customTitle,
        string body,
        bool isFavorite = false,
        IReadOnlyList<string>? tags = null)
    {
        if (!id.IsValid)
        {
            return DomainResult<Note>.Failure(DomainErrorCode.InvalidNoteId, "A note ID cannot be empty.");
        }

        if (sortOrder < 0)
        {
            return DomainResult<Note>.Failure(
                DomainErrorCode.InvalidSortOrder,
                "A note sort order must be zero or greater.");
        }

        if (body is null)
        {
            return DomainResult<Note>.Failure(
                DomainErrorCode.InvalidNoteBody,
                "A persisted note body cannot be null.");
        }

        return DomainResult<Note>.Success(
            new Note(id, localDate, sortOrder, customTitle, body, isProjection: false, isFavorite, ToArray(tags)));
    }

    internal static Note Projection(LocalDate localDate) =>
        new(null, localDate, 0, null, string.Empty, isProjection: true, isFavorite: false, []);

    internal Note WithBody(string body) =>
        new(Id, LocalDate, SortOrder, customTitle, body, IsProjection, IsFavorite, tags);

    internal Note WithCustomTitle(string title) =>
        new(Id, LocalDate, SortOrder, title, Body, IsProjection, IsFavorite, tags);

    internal Note WithSortOrder(int sortOrder) =>
        new(Id, LocalDate, sortOrder, customTitle, Body, IsProjection, IsFavorite, tags);

    private static string[] ToArray(IReadOnlyList<string>? tags) =>
        tags is null || tags.Count == 0 ? [] : [.. tags];
}
