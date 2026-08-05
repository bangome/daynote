using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;

namespace Daynote.Infrastructure.Tests.Search;

internal sealed class SearchFixture : IAsyncDisposable
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-15").Value;
    private static readonly DateTimeOffset Utc = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
    private readonly bool ownsRoot;

    private SearchFixture(string root, bool ownsRoot)
    {
        Root = root;
        this.ownsRoot = ownsRoot;
        Database = new SqliteDatabase(new(Path.Combine(root, "daynote.db")));
        Database.Initialize();
        Notes = new SqliteNoteRepository(Database, () => Utc);
        Search = new SearchService(new SqliteSearchRepository(Database));
    }

    public string Root { get; }
    public SqliteDatabase Database { get; }
    public SqliteNoteRepository Notes { get; }
    public SearchService Search { get; }

    public static SearchFixture Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-task7-search", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new SearchFixture(root, ownsRoot: true);
    }

    public static SearchFixture Create(string root) => new(root, ownsRoot: false);

    public ValueTask<NoteSaveReceipt> SaveNoteAsync(Guid id, string title, string body) =>
        SaveNoteAsync(id, Date, title, body);

    public ValueTask<NoteSaveReceipt> SaveNoteAsync(Guid id, LocalDate date, string title, string body) =>
        Notes.SaveNoteAsync(new NoteSaveRequest(
            NoteId.Create(id).Value, date, title, body, 0, IsNew: true, HasCustomTitle: true));

    public async ValueTask<NoteSaveReceipt> AppendNoteAsync(Guid id, string title, string body)
        => await AppendNoteAsync(id, Date, title, body);

    public async ValueTask<NoteSaveReceipt> AppendNoteAsync(Guid id, LocalDate date, string title, string body)
    {
        NoteId noteId = NoteId.Create(id).Value;
        await Notes.CreateNoteAsync(date, NoteId.Create(Id(999)).Value, noteId);
        return await Notes.SaveNoteAsync(new NoteSaveRequest(
            noteId, date, title, body, 0, IsNew: false, HasCustomTitle: true));
    }

    public void AssertIndexCounts(long expected)
    {
        DatabaseIntegrityResult integrity = Database.CheckIntegrity();
        Assert.IsTrue(integrity.IsValid);
        Assert.AreEqual(expected, integrity.SourceDocumentCount);
        Assert.AreEqual(expected, integrity.FtsDocumentCount);
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        if (ownsRoot && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");
}
