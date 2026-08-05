using Daynote.App.Shell.Product;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Unit tests for the read-only <see cref="TimelineViewModel"/>: newest-date-first grouping into
/// date-header + note rows, infinite-scroll paging that never duplicates a header when a single date
/// straddles a page boundary, the per-card expand/collapse toggle, and the open-in-editor callback.
/// </summary>
[TestClass]
public sealed class TimelineViewModelTests
{
    private static LocalDate D(string iso) => LocalDate.Parse(iso).Value;

    private static NoteSummary Note(LocalDate date, string title, string body, int sortOrder = 0, bool favorite = false)
        => new(Guid.NewGuid(), date, title, body, sortOrder, favorite);

    /// <summary>An in-memory repository that serves a fixed note list from the cross-date queries only.</summary>
    private sealed class StubNoteRepository(IReadOnlyList<NoteSummary> notes) : INoteRepository
    {
        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(notes);

        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(LocalDate from, LocalDate to, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(notes); // range overload is unused by these tests

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

    private static TimelineViewModel Create(IReadOnlyList<NoteSummary> notes, Func<Guid, LocalDate, Task>? open = null)
        => new(new StubNoteRepository(notes), open ?? ((_, _) => Task.CompletedTask));

    [TestMethod]
    public async Task LoadAsync_GroupsByDate_NewestFirst_WithHeaderCounts()
    {
        // Repository contract: notes arrive newest-date-first (DESC), sort order ASC within a date.
        var aug3 = D("2026-08-03");
        var aug1 = D("2026-08-01");
        var notes = new[]
        {
            Note(aug3, "A1", "first"),
            Note(aug3, "A2", "second", sortOrder: 1),
            Note(aug1, "B1", "third"),
        };

        var vm = Create(notes);
        await vm.LoadAsync();

        Assert.IsFalse(vm.IsEmpty);
        Assert.IsFalse(vm.HasMore);
        Assert.AreEqual(5, vm.Rows.Count); // header, A1, A2, header, B1

        var h0 = vm.Rows[0] as TimelineDateHeaderRow;
        Assert.IsNotNull(h0);
        Assert.AreEqual(aug3, h0!.Date);
        StringAssert.Contains(h0.CountText, "2");

        Assert.AreEqual("A1", ((TimelineNoteRow)vm.Rows[1]).Title);
        Assert.AreEqual("A2", ((TimelineNoteRow)vm.Rows[2]).Title);

        var h1 = vm.Rows[3] as TimelineDateHeaderRow;
        Assert.IsNotNull(h1);
        Assert.AreEqual(aug1, h1!.Date);
        StringAssert.Contains(h1.CountText, "1");

        Assert.AreEqual("B1", ((TimelineNoteRow)vm.Rows[4]).Title);
    }

    [TestMethod]
    public async Task LoadAsync_NoNotes_IsEmpty()
    {
        var vm = Create([]);
        await vm.LoadAsync();

        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.HasMore);
        Assert.AreEqual(0, vm.Rows.Count);
    }

    [TestMethod]
    public async Task Paging_SameDateAcrossPageBoundary_DoesNotDuplicateHeader()
    {
        // 25 notes on ONE date; page size is 20, so the date straddles the page boundary.
        var date = D("2026-08-03");
        var notes = Enumerable.Range(0, 25).Select(i => Note(date, $"N{i}", "x", sortOrder: i)).ToArray();

        var vm = Create(notes);
        await vm.LoadAsync();

        // First page: exactly one header + 20 notes.
        Assert.AreEqual(1, vm.Rows.OfType<TimelineDateHeaderRow>().Count());
        Assert.AreEqual(20, vm.Rows.OfType<TimelineNoteRow>().Count());
        Assert.IsTrue(vm.HasMore);

        vm.LoadMoreCommand.Execute(null);

        // Still exactly one header (no duplicate for the same date), all 25 notes present.
        Assert.AreEqual(1, vm.Rows.OfType<TimelineDateHeaderRow>().Count());
        Assert.AreEqual(25, vm.Rows.OfType<TimelineNoteRow>().Count());
        Assert.IsFalse(vm.HasMore);
    }

    [TestMethod]
    public async Task NoteRow_ToggleExpand_SwitchesBetweenSummaryAndFullBody()
    {
        var longBody = new string('x', 400); // exceeds NoteBodySummary maxChars, so it is truncated
        var vm = Create([Note(D("2026-08-03"), "Long", longBody)]);
        await vm.LoadAsync();

        var row = vm.Rows.OfType<TimelineNoteRow>().Single();
        Assert.IsTrue(row.IsTruncated);
        Assert.AreEqual(row.Summary, row.DisplayBody);
        string collapsedLabel = row.ExpandLabel;

        row.ToggleExpandCommand.Execute(null);

        Assert.IsTrue(row.IsExpanded);
        Assert.AreEqual(row.FullBody, row.DisplayBody);
        Assert.AreNotEqual(collapsedLabel, row.ExpandLabel);
    }

    [TestMethod]
    public async Task NoteRow_ShortBody_IsNotTruncated_ShowsFullBody()
    {
        var vm = Create([Note(D("2026-08-03"), "Short", "just a little text")]);
        await vm.LoadAsync();

        var row = vm.Rows.OfType<TimelineNoteRow>().Single();
        Assert.IsFalse(row.IsTruncated);
        Assert.AreEqual(row.FullBody, row.DisplayBody);
    }

    [TestMethod]
    public async Task NoteRow_Open_InvokesCallbackWithIdAndDate()
    {
        var date = D("2026-08-02");
        var note = Note(date, "Open me", "body");
        Guid? openedId = null;
        LocalDate? openedDate = null;

        var vm = Create([note], (id, d) =>
        {
            openedId = id;
            openedDate = d;
            return Task.CompletedTask;
        });
        await vm.LoadAsync();

        var row = vm.Rows.OfType<TimelineNoteRow>().Single();
        row.OpenCommand.Execute(null);

        Assert.AreEqual(note.Id, openedId);
        Assert.AreEqual(date, openedDate);
    }
}
