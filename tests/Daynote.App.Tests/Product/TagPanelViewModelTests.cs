using Daynote.App.Notes;
using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Tests for the 태그 panel: distinct-tag aggregation, counts, sort, per-tag occurrences, and that a row
/// jump invokes the shell callback with the originating occurrence.
/// </summary>
[TestClass]
public sealed class TagPanelViewModelTests
{
    private static NoteSummary Note(string body, string title = "노트 1", string iso = "2026-07-21")
        => new(Guid.NewGuid(), LocalDate.Parse(iso).Value, title, body, 0, false);

    /// <summary>An in-memory repository that serves a fixed note list from the cross-date query.</summary>
    private sealed class StubNoteRepository(IReadOnlyList<NoteSummary> notes) : INoteRepository
    {
        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(notes);

        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(LocalDate from, LocalDate to, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(notes);

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

    private static TagPanelViewModel Create(IReadOnlyList<NoteSummary> notes, Func<TagOccurrence, Task>? onJump = null)
        => new(new StubNoteRepository(notes), onJump ?? (_ => Task.CompletedTask));

    [TestMethod]
    public async Task RefreshAsync_AggregatesDistinctTagsWithCountsAndSort()
    {
        var vm = Create([Note("#work #work #idea"), Note("#work #idea")]);
        await vm.RefreshAsync();

        Assert.IsFalse(vm.IsEmpty);
        Assert.AreEqual(2, vm.TagCount);
        Assert.AreEqual("#work", vm.Tags[0].Tag);
        Assert.AreEqual(3, vm.Tags[0].Count);
        Assert.AreEqual("#idea", vm.Tags[1].Tag);
        Assert.AreEqual(2, vm.Tags[1].Count);
    }

    [TestMethod]
    public async Task RefreshAsync_NoTags_IsEmpty()
    {
        var vm = Create([Note("plain body, no tags")]);
        await vm.RefreshAsync();

        Assert.IsTrue(vm.IsEmpty);
        Assert.AreEqual(0, vm.TagCount);
    }

    [TestMethod]
    public async Task RefreshAsync_BuildsOccurrencesPerTag()
    {
        var vm = Create([Note("#a here", title: "First"), Note("#a there", title: "Second")]);
        await vm.RefreshAsync();

        TagItemViewModel item = vm.Tags.Single();
        Assert.AreEqual(2, item.Occurrences.Count);
        Assert.AreEqual("First", item.Occurrences[0].NoteTitle);
    }

    [TestMethod]
    public async Task Jump_InvokesCallbackWithMatchingOccurrence()
    {
        TagOccurrence? jumped = null;
        var vm = Create([Note("go #target now", title: "T")], occ =>
        {
            jumped = occ;
            return Task.CompletedTask;
        });
        await vm.RefreshAsync();

        vm.Tags.Single().Occurrences.Single().JumpCommand.Execute(null);

        Assert.IsNotNull(jumped);
        Assert.AreEqual("target", jumped!.Value.Tag);
        Assert.AreEqual("T", jumped.Value.NoteTitle);
    }

    [TestMethod]
    public async Task ToggleExpand_FlipsIsExpanded()
    {
        var vm = Create([Note("#a")]);
        await vm.RefreshAsync();

        TagItemViewModel item = vm.Tags.Single();
        Assert.IsFalse(item.IsExpanded);

        item.ToggleExpandCommand.Execute(null);
        Assert.IsTrue(item.IsExpanded);
    }

    [TestMethod]
    public async Task TabLabel_ReflectsDistinctTagCount()
    {
        var vm = Create([Note("#a #b #c")]);
        await vm.RefreshAsync();

        StringAssert.Contains(vm.TabLabel, "3");
    }
}
