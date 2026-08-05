using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Search;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Notes;

[TestClass]
public sealed class NoteFavoritesAndTagsTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-15").Value;
    private static readonly DateTimeOffset Utc =
        DateTimeOffset.Parse("2026-07-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

    [TestMethod]
    public async Task Toggling_favorite_persists_across_a_restart()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => Utc);
        NoteId id = NoteId.Create(Id(1)).Value;
        await repository.SaveNoteAsync(new NoteSaveRequest(id, Date, "제목", "본문", 0, IsNew: true, HasCustomTitle: true));

        DayWorkspace afterToggle = await repository.ToggleFavoriteAsync(Date, id);
        Assert.IsTrue(Single(afterToggle, id).IsFavorite);

        var restarted = new SqliteNoteRepository(fixture.Database, () => Utc.AddDays(1));
        DayWorkspace reloaded = await restarted.GetDayWorkspaceStateAsync(Date);
        Assert.IsTrue(Single(reloaded, id).IsFavorite);

        DayWorkspace afterSecond = await restarted.ToggleFavoriteAsync(Date, id);
        Assert.IsFalse(Single(afterSecond, id).IsFavorite);
    }

    [TestMethod]
    public async Task Setting_tags_replaces_the_whole_set_and_persists_order_and_deduplication()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => Utc);
        var setTags = new SetNoteTags(repository);
        NoteId id = NoteId.Create(Id(2)).Value;
        await repository.SaveNoteAsync(new NoteSaveRequest(id, Date, "제목", "본문", 0, IsNew: true, HasCustomTitle: true));

        DayWorkspace first = await setTags.ExecuteAsync(Date, id, new[] { " 회의 ", "기획", "회의", "" });
        CollectionAssert.AreEqual(new[] { "회의", "기획" }, Single(first, id).Tags.ToArray());

        DayWorkspace second = await setTags.ExecuteAsync(Date, id, new[] { "배포" });
        CollectionAssert.AreEqual(new[] { "배포" }, Single(second, id).Tags.ToArray());

        var restarted = new SqliteNoteRepository(fixture.Database, () => Utc.AddDays(1));
        DayWorkspace reloaded = await restarted.GetDayWorkspaceStateAsync(Date);
        CollectionAssert.AreEqual(new[] { "배포" }, Single(reloaded, id).Tags.ToArray());
    }

    [TestMethod]
    public async Task Set_tags_rejects_a_set_over_the_caps()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => Utc);
        var setTags = new SetNoteTags(repository);
        NoteId id = NoteId.Create(Id(3)).Value;
        await repository.SaveNoteAsync(new NoteSaveRequest(id, Date, "제목", "본문", 0, IsNew: true, HasCustomTitle: true));

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await setTags.ExecuteAsync(Date, id, new[] { new string('x', NoteTags.MaxLength + 1) }));
    }

    [TestMethod]
    public async Task Tags_are_searchable_and_clearing_them_removes_the_match_without_polluting_snippets()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(fixture.Database, () => Utc);
        var setTags = new SetNoteTags(repository);
        var search = new SearchService(new SqliteSearchRepository(fixture.Database));
        NoteId id = NoteId.Create(Id(4)).Value;
        await repository.SaveNoteAsync(new NoteSaveRequest(id, Date, "회의록", "본문내용", 0, IsNew: true, HasCustomTitle: true));

        await setTags.ExecuteAsync(Date, id, new[] { "스프린트태그" });

        SearchResult tagHit = (await search.SearchAsync("스프린트태그")).Results.Single();
        Assert.AreEqual(id.Value, tagHit.SourceId);
        Assert.AreEqual(SearchSourceType.Note, tagHit.SourceType);
        // The tag folds into the search index only; the visible snippet stays the pure note body.
        Assert.IsFalse(tagHit.Snippet.Contains("스프린트태그", StringComparison.Ordinal));
        Assert.AreEqual("본문내용", tagHit.Snippet);

        await setTags.ExecuteAsync(Date, id, Array.Empty<string>());
        Assert.IsEmpty((await search.SearchAsync("스프린트태그")).Results);
        Assert.HasCount(1, (await search.SearchAsync("본문내용")).Results);
        Assert.IsTrue(fixture.Database.CheckIntegrity().IsValid);
    }

    private static Note Single(DayWorkspace workspace, NoteId id) =>
        workspace.Notes.Notes.Single(note => note.Id == id);

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");
}
