using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Tests.Persistence;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Notes;

[TestClass]
public sealed class AutosaveTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-14").Value;

    [TestMethod]
    public async Task Test_Autosave_debounce_coalesces_without_wall_clock_sleep()
    {
        var repository = new RecordingRepository();
        var scheduler = new ManualDebounceScheduler();
        await using var coordinator = new AutosaveCoordinator(repository, scheduler, TimeSpan.FromMilliseconds(500));
        NoteId id = Id(10);

        coordinator.MarkDirty(new NoteSaveRequest(id, Date, "Note 1", "가", 0, IsNew: true, HasCustomTitle: false));
        coordinator.MarkDirty(new NoteSaveRequest(id, Date, "Note 1", "가\n나", 0, IsNew: true, HasCustomTitle: false));
        Assert.AreEqual(0, repository.Requests.Count);

        await scheduler.AdvanceOneAsync();
        await coordinator.WaitForPendingSaveAsync();

        Assert.AreEqual(1, repository.Requests.Count);
        Assert.AreEqual("가\n나", repository.Requests[0].Body);
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), scheduler.Delays[0]);
        Assert.IsFalse(coordinator.IsDirty);
    }

    [TestMethod]
    public async Task Test_Autosave_explicit_date_note_hide_and_quit_flushes()
    {
        foreach (FlushReason reason in Enum.GetValues<FlushReason>())
        {
            var repository = new RecordingRepository();
            var scheduler = new ManualDebounceScheduler();
            await using var coordinator = new AutosaveCoordinator(repository, scheduler);
            coordinator.MarkDirty(new NoteSaveRequest(Id(11), Date, "Note 1", reason.ToString(), 0, IsNew: true, HasCustomTitle: false));

            FlushResult result = await coordinator.FlushAsync(reason);

            Assert.IsTrue(result.CanProceed, reason.ToString());
            Assert.AreEqual(1, repository.Requests.Count, reason.ToString());
        }
    }

    [TestMethod]
    public async Task Test_Autosave_storage_failure_blocks_transition_retains_exact_utf8_and_retries()
    {
        var repository = new RecordingRepository { Failure = new RecoverableNoteException(NoteFailureCode.StorageUnavailable) };
        var scheduler = new ManualDebounceScheduler();
        await using var coordinator = new AutosaveCoordinator(repository, scheduler);
        const string exact = "# 제목\n\n🙂 `코드`\n끝";
        coordinator.MarkDirty(new NoteSaveRequest(Id(12), Date, "개인 제목", exact, 0, IsNew: true, HasCustomTitle: true));

        FlushResult blocked = await coordinator.FlushAsync(FlushReason.Quit);

        Assert.IsFalse(blocked.CanProceed);
        Assert.AreEqual(NoteFailureCode.StorageUnavailable, blocked.Error!.Value.Code);
        Assert.IsFalse(blocked.Error.Value.Message.Contains(exact, StringComparison.Ordinal));
        Assert.IsTrue(coordinator.IsDirty);
        Assert.AreEqual(exact, coordinator.DirtyRequest!.Value.Body);

        repository.Failure = null;
        FlushResult retried = await coordinator.FlushAsync(FlushReason.Quit);
        Assert.IsTrue(retried.CanProceed);
        Assert.AreEqual(exact, repository.Requests[^1].Body);
        Assert.IsFalse(coordinator.IsDirty);
    }

    [TestMethod]
    public async Task Test_Autosave_revision_conflict_blocks_navigation_and_keeps_dirty_revision()
    {
        var repository = new RecordingRepository { Failure = new RecoverableNoteException(NoteFailureCode.RevisionConflict) };
        await using var coordinator = new AutosaveCoordinator(repository, new ManualDebounceScheduler());
        var request = new NoteSaveRequest(Id(13), Date, "Note 1", "dirty", 7, IsNew: false, HasCustomTitle: false);
        coordinator.MarkDirty(request);

        FlushResult result = await coordinator.FlushAsync(FlushReason.DateChange);

        Assert.IsFalse(result.CanProceed);
        Assert.AreEqual(7, coordinator.DirtyRequest!.Value.Revision);
        Assert.AreEqual("dirty", coordinator.DirtyRequest.Value.Body);
    }

    [TestMethod]
    public async Task Test_Autosave_debounce_failure_emits_payload_free_recoverable_state()
    {
        var repository = new RecordingRepository { Failure = new RecoverableNoteException(NoteFailureCode.StorageUnavailable) };
        var scheduler = new ManualDebounceScheduler();
        await using var coordinator = new AutosaveCoordinator(repository, scheduler);
        RecoverableNoteError? emitted = null;
        coordinator.RecoverableError += error => emitted = error;
        coordinator.MarkDirty(new NoteSaveRequest(Id(17), Date, "Note 1", "sensitive body", 0, IsNew: true, HasCustomTitle: false));

        await scheduler.AdvanceOneAsync();
        await coordinator.WaitForPendingSaveAsync();

        Assert.AreEqual(NoteFailureCode.StorageUnavailable, coordinator.LastRecoverableError!.Value.Code);
        Assert.AreEqual(coordinator.LastRecoverableError, emitted);
        Assert.IsFalse(emitted!.Value.Message.Contains("sensitive body", StringComparison.Ordinal));
        Assert.IsTrue(coordinator.IsDirty);
    }

    [TestMethod]
    public async Task Test_Autosave_real_repository_injected_full_like_failure_blocks_hide_then_quit_retry()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var fault = new OneShotStorageFault();
        var repository = new SqliteNoteRepository(
            fixture.Database,
            () => DateTimeOffset.Parse("2026-07-15T00:00:00Z"),
            fault);
        await using var coordinator = new AutosaveCoordinator(repository, new ManualDebounceScheduler());
        const string exact = "# 보존\n\nUTF-8 🙂";
        coordinator.MarkDirty(new NoteSaveRequest(Id(14), Date, "Note 1", exact, 0, IsNew: true, HasCustomTitle: false));

        FlushResult hide = await coordinator.FlushAsync(FlushReason.Hide);

        Assert.IsFalse(hide.CanProceed);
        Assert.AreEqual(NoteFailureCode.StorageUnavailable, hide.Error!.Value.Code);
        using (SqliteConnection connection = fixture.Database.OpenReadConnection())
        {
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM notes;"));
            Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM search_documents;"));
        }
        FlushResult quit = await coordinator.FlushAsync(FlushReason.Quit);
        Assert.IsTrue(quit.CanProceed);
        Assert.AreEqual(exact, repository.GetDayWorkspaceAsync(Date).Result.Notes[0].Body);
    }

    [TestMethod]
    public async Task Test_Autosave_consecutive_edits_advance_revision_without_stale_conflict()
    {
        var repository = new RecordingRepository();
        await using var coordinator = new AutosaveCoordinator(repository, new ManualDebounceScheduler());
        NoteId id = Id(15);
        coordinator.MarkDirty(new NoteSaveRequest(id, Date, "Note 1", "first", 0, IsNew: true, HasCustomTitle: false));
        Assert.IsTrue((await coordinator.FlushAsync(FlushReason.NoteChange)).CanProceed);

        coordinator.MarkDirty(new NoteSaveRequest(id, Date, "Note 1", "second", 0, IsNew: true, HasCustomTitle: false));
        Assert.IsTrue((await coordinator.FlushAsync(FlushReason.Quit)).CanProceed);

        Assert.AreEqual(2, repository.Requests.Count);
        Assert.IsFalse(repository.Requests[1].IsNew);
        Assert.AreEqual(0, repository.Requests[1].Revision);
    }

    [TestMethod]
    public async Task Test_Autosave_projection_edit_revert_noop_then_later_edit_still_inserts()
    {
        var repository = new ProjectionAwareRecordingRepository();
        await using var coordinator = new AutosaveCoordinator(repository, new ManualDebounceScheduler());
        NoteId id = Id(16);
        coordinator.MarkDirty(new NoteSaveRequest(id, Date, "Note 1", "typed", 0, IsNew: true, HasCustomTitle: false));
        coordinator.MarkDirty(new NoteSaveRequest(id, Date, "Note 1", string.Empty, 0, IsNew: true, HasCustomTitle: false));
        Assert.IsTrue((await coordinator.FlushAsync(FlushReason.DateChange)).CanProceed);

        coordinator.MarkDirty(new NoteSaveRequest(id, Date, "Note 1", "later real edit", 0, IsNew: true, HasCustomTitle: false));
        Assert.IsTrue((await coordinator.FlushAsync(FlushReason.Quit)).CanProceed);

        Assert.IsTrue(repository.Requests[1].IsNew);
        Assert.AreEqual("later real edit", repository.Requests[1].Body);
    }

    private static NoteId Id(int suffix) => NoteId.Create(Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}")).Value;

    private class RecordingRepository : INoteRepository
    {
        public List<NoteSaveRequest> Requests { get; } = [];
        public RecoverableNoteException? Failure { get; set; }

        public ValueTask<NoteSet> GetDayWorkspaceAsync(LocalDate localDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> GetDayWorkspaceStateAsync(LocalDate localDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> CreateNoteAsync(LocalDate localDate, NoteId projectionId, NoteId newNoteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> ReorderNotesAsync(LocalDate localDate, IReadOnlyList<NoteId> orderedIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> DeleteNoteAsync(LocalDate localDate, NoteId noteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> ToggleFavoriteAsync(LocalDate localDate, NoteId noteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DayWorkspace> SetTagsAsync(LocalDate localDate, NoteId noteId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<DateContentSummary>> GetMonthContentSummaryAsync(int year, int month, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<NoteSummary>> GetAllNotesAsync(LocalDate from, LocalDate to, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public virtual ValueTask<NoteSaveReceipt> SaveNoteAsync(NoteSaveRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Failure is null
                ? ValueTask.FromResult(new NoteSaveReceipt(request.IsNew ? 0 : request.Revision + 1))
                : ValueTask.FromException<NoteSaveReceipt>(Failure);
        }

    }

    private sealed class ManualDebounceScheduler : IAutosaveScheduler
    {
        private readonly Queue<TaskCompletionSource> _pending = [];
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetCanceled(), source);
            _pending.Enqueue(source);
            return source.Task;
        }

        public async Task AdvanceOneAsync()
        {
            while (_pending.Count > 1) _pending.Dequeue().TrySetResult();
            _pending.Dequeue().TrySetResult();
            await Task.Yield();
            await Task.Yield();
        }
    }

    private sealed class ProjectionAwareRecordingRepository : RecordingRepository
    {
        public override ValueTask<NoteSaveReceipt> SaveNoteAsync(NoteSaveRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(request.IsNew && request.Body.Length == 0 && request.Title == "Note 1"
                ? new NoteSaveReceipt(0, IsPersisted: false)
                : new NoteSaveReceipt(request.IsNew ? 0 : request.Revision + 1));
        }
    }

    private sealed class OneShotStorageFault : INoteWriteInterceptor
    {
        private bool _pending = true;

        public void BeforeWrite(NoteWriteOperation operation)
        {
        }

        public void AfterSourceWrite(NoteWriteOperation operation)
        {
            if (operation == NoteWriteOperation.Save && _pending)
            {
                _pending = false;
                throw new SqliteException("sensitive SQLITE_FULL-like detail", 13);
            }
        }
    }
}
