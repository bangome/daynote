using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Notes;

[TestClass]
public sealed class NoteRepositoryUseCaseTests
{
    [TestMethod]
    public async Task Test_NoteRepository_use_cases_materialize_projection_and_propagate_revisions()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var repository = new SqliteNoteRepository(
            fixture.Database,
            () => DateTimeOffset.Parse("2026-07-15T00:00:00Z"));
        LocalDate date = LocalDate.Parse("2026-07-14").Value;
        NoteId first = Id(51);
        NoteId second = Id(52);
        var ids = new Queue<NoteId>([first, second]);
        var get = new GetDayWorkspace(repository);
        var create = new CreateNote(repository, ids.Dequeue);
        var reorder = new ReorderNotes(repository);
        var save = new SaveNote(repository);
        var delete = new DeleteNote(repository);

        DayWorkspace projected = await get.ExecuteAsync(date);
        Assert.IsTrue(projected.Notes.IsProjectionOnly, "A fresh day shows only the virtual projection.");
        // On an empty day the + button creates exactly ONE real "Note 1"; a second add appends "Note 2".
        await create.ExecuteAsync(date);
        DayWorkspace created = await create.ExecuteAsync(date);
        DayWorkspace reordered = await reorder.ExecuteAsync(date, [second, first]);
        Note secondNote = reordered.Notes.Notes[0];
        NoteSaveReceipt saved = await save.ExecuteAsync(
            new NoteSaveRequest(
                second,
                date,
                secondNote.Title,
                "use-case body",
                reordered.RevisionOf(second),
                IsNew: false,
                HasCustomTitle: secondNote.HasCustomTitle));
        DayWorkspace deleted = await delete.ExecuteAsync(date, first);
        DayWorkspace restored = await get.ExecuteAsync(date);

        CollectionAssert.AreEqual(new[] { first, second }, created.Notes.Notes.Select(static note => note.Id!.Value).ToArray());
        Assert.AreEqual(0, created.RevisionOf(first));
        Assert.AreEqual(0, created.RevisionOf(second));
        Assert.IsTrue(reordered.RevisionOf(second) > created.RevisionOf(second));
        Assert.IsTrue(saved.Revision > reordered.RevisionOf(second));
        Assert.AreEqual(1, deleted.Notes.Notes.Count);
        Assert.AreEqual(second, restored.Notes.Notes[0].Id);
        Assert.AreEqual("use-case body", restored.Notes.Notes[0].Body);
    }

    private static NoteId Id(int suffix) =>
        NoteId.Create(Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}")).Value;
}
