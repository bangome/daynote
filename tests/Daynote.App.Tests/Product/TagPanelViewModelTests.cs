using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The 태그 panel: the tags the user put on notes, grouped, counted and ordered, and a row that opens
/// the note it names.
/// </summary>
/// <remarks>
/// It used to read inline <c>#tag</c> tokens out of note bodies, which is a system the app no longer
/// has — the tags are the chips under the note title, in <c>note_tags</c>. These tests were rewritten
/// around that rather than deleted: the grouping, counting and ordering are the same job.
/// </remarks>
[TestClass]
public sealed class TagPanelViewModelTests
{
    private static readonly LocalDate Day = LocalDate.Parse("2026-07-21").Value;

    private static NoteSummary Note(Guid id, string title = "노트 1", string body = "본문", string iso = "2026-07-21")
        => new(id, LocalDate.Parse(iso).Value, title, body, 0, false);

    /// <summary>An in-memory repository serving fixed notes and fixed note_tags rows.</summary>
    private sealed class StubNoteRepository(IReadOnlyList<NoteSummary> notes, IReadOnlyList<NoteTagLink> links)
        : INoteRepository
    {
        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(notes);

        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(LocalDate from, LocalDate to, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(notes);

        public ValueTask<IReadOnlyList<NoteTagLink>> GetAllNoteTagsAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(links);

        public ValueTask<NoteSet> GetDayWorkspaceAsync(LocalDate localDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> GetDayWorkspaceStateAsync(LocalDate localDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> CreateNoteAsync(LocalDate localDate, NoteId projectionId, NoteId newNoteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> ReorderNotesAsync(LocalDate localDate, IReadOnlyList<NoteId> orderedIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> DeleteNoteAsync(LocalDate localDate, NoteId noteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<NoteSaveReceipt> SaveNoteAsync(NoteSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> ToggleFavoriteAsync(LocalDate localDate, NoteId noteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> SetTagsAsync(LocalDate localDate, NoteId noteId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<DateContentSummary>> GetMonthContentSummaryAsync(int year, int month, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static TagPanelViewModel Create(
        IReadOnlyList<NoteSummary> notes,
        IReadOnlyList<NoteTagLink> links,
        Func<TagOccurrence, Task>? onJump = null)
        => new(new StubNoteRepository(notes, links), onJump ?? (_ => Task.CompletedTask));

    [TestMethod]
    public async Task It_groups_tags_and_orders_by_how_many_notes_carry_them()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();

        TagPanelViewModel vm = Create(
            [Note(a), Note(b), Note(c)],
            [
                new NoteTagLink(a, "work", 0),
                new NoteTagLink(a, "idea", 1),
                new NoteTagLink(b, "work", 0),
                new NoteTagLink(c, "work", 0),
            ]);
        await vm.RefreshAsync();

        Assert.IsFalse(vm.IsEmpty);
        Assert.AreEqual(2, vm.TagCount);
        Assert.AreEqual("#work", vm.Tags[0].Tag);
        Assert.AreEqual(3, vm.Tags[0].Count);
        Assert.AreEqual("#idea", vm.Tags[1].Tag);
        Assert.AreEqual(1, vm.Tags[1].Count);
    }

    [TestMethod]
    public async Task A_note_with_no_tags_puts_nothing_in_the_panel()
    {
        TagPanelViewModel vm = Create([Note(Guid.NewGuid(), body: "태그 없는 본문 #해시는_이제_그냥_글자")], []);
        await vm.RefreshAsync();

        Assert.IsTrue(vm.IsEmpty);
        Assert.AreEqual(0, vm.TagCount);
    }

    [TestMethod]
    public async Task Each_row_lists_the_notes_that_carry_the_tag()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();

        TagPanelViewModel vm = Create(
            [Note(a, "회의록", "오전 회의 정리"), Note(b, "장부", "9월 정산")],
            [new NoteTagLink(a, "work", 0), new NoteTagLink(b, "work", 0)]);
        await vm.RefreshAsync();

        TagItemViewModel row = vm.Tags.Single();
        CollectionAssert.AreEquivalent(
            new[] { "회의록", "장부" },
            row.Occurrences.Select(o => o.NoteTitle).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "오전 회의 정리", "9월 정산" },
            row.Occurrences.Select(o => o.Preview).ToArray());
    }

    [TestMethod]
    public async Task Opening_a_row_hands_the_shell_the_note_it_names()
    {
        Guid id = Guid.NewGuid();
        TagOccurrence? jumped = null;

        TagPanelViewModel vm = Create(
            [Note(id, "회의록")],
            [new NoteTagLink(id, "work", 0)],
            occurrence =>
            {
                jumped = occurrence;
                return Task.CompletedTask;
            });
        await vm.RefreshAsync();
        await vm.Tags.Single().Occurrences.Single().JumpCommand.ExecuteAsync(null);

        Assert.IsNotNull(jumped);
        Assert.AreEqual(id, jumped.Value.NoteId);
        Assert.AreEqual(Day, jumped.Value.Date);
        Assert.AreEqual("work", jumped.Value.Tag);
    }

    [TestMethod]
    public async Task A_tag_on_a_note_that_is_gone_is_dropped()
    {
        // The notes and the links are two queries taken a moment apart; a row that cannot be opened
        // is worse than a missing one.
        TagPanelViewModel vm = Create([], [new NoteTagLink(Guid.NewGuid(), "work", 0)]);
        await vm.RefreshAsync();

        Assert.IsTrue(vm.IsEmpty);
    }
}
