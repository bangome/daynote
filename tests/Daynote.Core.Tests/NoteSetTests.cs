using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Tests;

[TestClass]
public sealed class NoteSetTests
{
    private static readonly LocalDate SelectedDate = LocalDate.Parse("2026-07-14").Value;

    [TestMethod]
    public void AuthoredNoteUsesExplicitHistoricalSelectionInsteadOfCurrentClockDate()
    {
        var clockNow = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        NoteId id = Id("00000000-0000-0000-0000-000000000001");
        NoteSet empty = NoteSet.Empty(SelectedDate);

        DomainResult<NoteSet> edited = empty.EditBody(0, "historical", id);

        Assert.IsTrue(edited.IsSuccess);
        Assert.AreEqual(15, clockNow.Day);
        Assert.AreEqual("2026-07-14", edited.Value.Notes[0].LocalDate.ToString());
        Assert.AreNotEqual("2026-07-15", edited.Value.Notes[0].LocalDate.ToString());
    }

    [TestMethod]
    public void EmptyDateExposesExplicitNonpersistableNonindexableNoteOneProjection()
    {
        NoteSet empty = NoteSet.Empty(SelectedDate);

        Assert.AreEqual(1, empty.Notes.Count);
        Note note = empty.Notes[0];
        Assert.IsTrue(note.IsProjection);
        Assert.IsFalse(note.IsPersistable);
        Assert.IsFalse(note.IsIndexable);
        Assert.IsNull(note.Id);
        Assert.AreEqual(0, note.SortOrder);
        Assert.AreEqual(1, note.DisplayNumber);
        Assert.AreEqual("노트 1", note.Title);
    }

    [TestMethod]
    public void FirstEditMaterializesProjectionWithStableIdentity()
    {
        NoteId id = Id("00000000-0000-0000-0000-000000000011");

        NoteSet changed = NoteSet.Empty(SelectedDate).EditBody(0, "first", id).Value;

        Assert.AreEqual(1, changed.Notes.Count);
        Assert.AreEqual(id, changed.Notes[0].Id);
        Assert.IsFalse(changed.Notes[0].IsProjection);
        Assert.IsTrue(changed.Notes[0].IsPersistable);
        Assert.IsTrue(changed.Notes[0].IsIndexable);
        Assert.AreEqual("first", changed.Notes[0].Body);
    }

    [TestMethod]
    public void EmptyBodyEditKeepsTheEmptyProjectionUnmaterialized()
    {
        NoteId unusedId = Id("00000000-0000-0000-0000-000000000012");

        NoteSet unchanged = NoteSet.Empty(SelectedDate).EditBody(0, string.Empty, unusedId).Value;

        Assert.IsTrue(unchanged.IsProjectionOnly);
        Assert.IsNull(unchanged.Notes[0].Id);
        Assert.IsFalse(unchanged.Notes[0].IsPersistable);
        Assert.IsFalse(unchanged.Notes[0].IsIndexable);
    }

    [TestMethod]
    public void DefaultTitleRenameKeepsTheEmptyProjectionUnmaterialized()
    {
        NoteId unusedId = Id("00000000-0000-0000-0000-000000000013");

        NoteSet unchanged = NoteSet.Empty(SelectedDate).RenameTitle(0, "노트 1", unusedId).Value;

        Assert.IsTrue(unchanged.IsProjectionOnly);
        Assert.IsNull(unchanged.Notes[0].Id);
        Assert.IsFalse(unchanged.Notes[0].IsPersistable);
        Assert.IsFalse(unchanged.Notes[0].IsIndexable);
    }

    [TestMethod]
    public void AddOnEmptyMaterializesNoteOneAndAddsNoteTwo()
    {
        NoteId first = Id("00000000-0000-0000-0000-000000000021");
        NoteId second = Id("00000000-0000-0000-0000-000000000022");

        NoteSet changed = NoteSet.Empty(SelectedDate).Add(second, first).Value;

        CollectionAssert.AreEqual(new[] { first, second }, changed.Notes.Select(static note => note.Id!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, changed.Notes.Select(static note => note.SortOrder).ToArray());
        CollectionAssert.AreEqual(new[] { "노트 1", "노트 2" }, changed.Notes.Select(static note => note.Title).ToArray());
    }

    [TestMethod]
    public void DeleteLastAndRestartWithNoRowsReturnToProjection()
    {
        NoteId id = Id("00000000-0000-0000-0000-000000000031");
        NoteSet materialized = NoteSet.Empty(SelectedDate).EditBody(0, "temporary", id).Value;

        NoteSet deleted = materialized.Delete(id).Value;
        NoteSet restarted = NoteSet.Restore(SelectedDate, []).Value;

        Assert.IsTrue(deleted.Notes.Single().IsProjection);
        Assert.IsTrue(restarted.Notes.Single().IsProjection);
        Assert.AreEqual("노트 1", restarted.Notes.Single().Title);
    }

    [TestMethod]
    public void ReorderAndDeleteCompactOrdersWhilePreservingIds()
    {
        NoteId first = Id("00000000-0000-0000-0000-000000000041");
        NoteId second = Id("00000000-0000-0000-0000-000000000042");
        NoteId third = Id("00000000-0000-0000-0000-000000000043");
        NoteSet set = NoteSet.Empty(SelectedDate)
            .Add(second, first).Value
            .Add(third).Value;

        NoteSet reordered = set.Reorder([third, first, second]).Value;
        NoteSet deleted = reordered.Delete(first).Value;

        CollectionAssert.AreEqual(new[] { third, second }, deleted.Notes.Select(static note => note.Id!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, deleted.Notes.Select(static note => note.SortOrder).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, deleted.Notes.Select(static note => note.DisplayNumber).ToArray());
    }

    [TestMethod]
    public void ReorderRenumbersDefaultTitlesButNeverMutatesCustomTitles()
    {
        NoteId first = Id("00000000-0000-0000-0000-000000000051");
        NoteId second = Id("00000000-0000-0000-0000-000000000052");
        NoteSet set = NoteSet.Empty(SelectedDate)
            .Add(second, first).Value
            .RenameTitle(1, "Pinned idea").Value;

        NoteSet reordered = set.Reorder([second, first]).Value;

        Assert.AreEqual("Pinned idea", reordered.Notes[0].Title);
        Assert.IsTrue(reordered.Notes[0].HasCustomTitle);
        Assert.AreEqual("노트 2", reordered.Notes[1].Title);
        Assert.IsFalse(reordered.Notes[1].HasCustomTitle);
    }

    [TestMethod]
    public void InvalidAndDuplicateIdsAndInvalidOrdersReturnTypedErrors()
    {
        DomainResult<NoteId> emptyId = NoteId.Create(Guid.Empty);
        NoteId duplicate = Id("00000000-0000-0000-0000-000000000061");
        DomainResult<Note> invalidOrder = Note.CreatePersisted(duplicate, SelectedDate, -1, null, "");
        DomainResult<NoteSet> duplicateAdd = NoteSet.Empty(SelectedDate)
            .EditBody(0, "body", duplicate).Value
            .Add(duplicate);

        Assert.IsFalse(emptyId.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidNoteId, emptyId.Error.Code);
        Assert.IsFalse(invalidOrder.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidSortOrder, invalidOrder.Error.Code);
        Assert.IsFalse(duplicateAdd.IsSuccess);
        Assert.AreEqual(DomainErrorCode.DuplicateNoteId, duplicateAdd.Error.Code);
    }

    [TestMethod]
    public void NullPersistedBodyReturnsTypedFailure()
    {
        NoteId id = Id("00000000-0000-0000-0000-000000000062");

        DomainResult<Note> result = Note.CreatePersisted(id, SelectedDate, 0, null, null!);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreNotEqual(DomainErrorCode.None, result.Error.Code);
    }

    [TestMethod]
    public void RestoreAndReorderRejectGapsDuplicatesAndIncompleteOrderings()
    {
        NoteId first = Id("00000000-0000-0000-0000-000000000071");
        NoteId second = Id("00000000-0000-0000-0000-000000000072");
        Note firstAtZero = Note.CreatePersisted(first, SelectedDate, 0, null, "").Value;
        Note secondAtTwo = Note.CreatePersisted(second, SelectedDate, 2, null, "").Value;
        DomainResult<NoteSet> gap = NoteSet.Restore(SelectedDate, [firstAtZero, secondAtTwo]);
        NoteSet valid = NoteSet.Empty(SelectedDate).Add(second, first).Value;
        DomainResult<NoteSet> duplicateOrder = valid.Reorder([first, first]);
        DomainResult<NoteSet> incompleteOrder = valid.Reorder([first]);

        Assert.IsFalse(gap.IsSuccess);
        Assert.AreEqual(DomainErrorCode.NonContiguousSortOrder, gap.Error.Code);
        Assert.IsFalse(duplicateOrder.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidReorder, duplicateOrder.Error.Code);
        Assert.IsFalse(incompleteOrder.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidReorder, incompleteOrder.Error.Code);
    }

    private static NoteId Id(string value) => NoteId.Create(Guid.Parse(value)).Value;
}
