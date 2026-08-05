using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public enum FlushReason
{
    DateChange,
    NoteChange,
    Hide,
    Quit,
}

public readonly record struct FlushResult(bool CanProceed, RecoverableNoteError? Error)
{
    public static FlushResult Proceed { get; } = new(true, null);

    public static FlushResult Block(RecoverableNoteError error) => new(false, error);
}

public interface IAutosaveScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemAutosaveScheduler : IAutosaveScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class AutosaveCoordinator : IAsyncDisposable
{
    private readonly INoteRepository _repository;
    private readonly IAutosaveScheduler _scheduler;
    private readonly TimeSpan _debounce;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<NoteId, int> _savedRevisions = [];
    private CancellationTokenSource? _scheduledSave;
    private Task _scheduledTask = Task.CompletedTask;
    private NoteSaveRequest? _dirty;
    private long _generation;
    private bool _disposed;
    private RecoverableNoteError? _lastRecoverableError;

    public AutosaveCoordinator(
        INoteRepository repository,
        IAutosaveScheduler? scheduler = null,
        TimeSpan? debounce = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _scheduler = scheduler ?? new SystemAutosaveScheduler();
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        if (_debounce <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
    }

    public bool IsDirty
    {
        get { lock (_stateGate) return _dirty is not null; }
    }

    public NoteSaveRequest? DirtyRequest
    {
        get { lock (_stateGate) return _dirty; }
    }

    public RecoverableNoteError? LastRecoverableError
    {
        get { lock (_stateGate) return _lastRecoverableError; }
    }

    public event Action<RecoverableNoteError>? RecoverableError;

    public async ValueTask WaitForPendingSaveAsync(CancellationToken cancellationToken = default)
    {
        Task scheduled;
        lock (_stateGate) scheduled = _scheduledTask;
        await scheduled.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void MarkDirty(NoteSaveRequest request)
    {
        request.Validate();
        CancellationTokenSource schedule;
        long generation;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_savedRevisions.TryGetValue(request.Id, out int savedRevision) && request.Revision <= savedRevision)
            {
                request = request with { Revision = savedRevision, IsNew = false };
            }

            _dirty = request;
            generation = ++_generation;
            _scheduledSave?.Cancel();
            _scheduledSave?.Dispose();
            schedule = new CancellationTokenSource();
            _scheduledSave = schedule;
            _scheduledTask = RunScheduledSaveAsync(generation, schedule.Token);
        }
    }

    public async ValueTask<FlushResult> FlushAsync(
        FlushReason reason,
        CancellationToken cancellationToken = default)
    {
        _ = reason;
        CancelScheduledSave();
        return await SaveDirtyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task scheduled;
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            scheduled = _scheduledTask;
        }

        CancelScheduledSave();
        await scheduled.ConfigureAwait(false);
        await _flushGate.WaitAsync().ConfigureAwait(false);
        _flushGate.Release();
        _flushGate.Dispose();
    }

    private async Task RunScheduledSaveAsync(long generation, CancellationToken cancellationToken)
    {
        try
        {
            await _scheduler.DelayAsync(_debounce, cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                if (generation != _generation) return;
            }

            await SaveDirtyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask<FlushResult> SaveDirtyAsync(CancellationToken cancellationToken)
    {
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            NoteSaveRequest request;
            long generation;
            lock (_stateGate)
            {
                if (_dirty is not { } dirty) return FlushResult.Proceed;
                request = dirty;
                generation = _generation;
            }

            try
            {
                NoteSaveReceipt receipt = await _repository.SaveNoteAsync(request, cancellationToken).ConfigureAwait(false);
                if (receipt.IsPersisted)
                {
                    lock (_stateGate)
                    {
                        _savedRevisions[request.Id] = receipt.Revision;
                        if (_dirty is { } current && current.Id == request.Id && current.Revision <= receipt.Revision)
                        {
                            _dirty = current with { Revision = receipt.Revision, IsNew = false };
                        }
                    }
                }
            }
            catch (RecoverableNoteException exception)
            {
                RecoverableNoteError error = exception.ToError();
                lock (_stateGate) _lastRecoverableError = error;
                RecoverableError?.Invoke(error);
                return FlushResult.Block(error);
            }

            lock (_stateGate)
            {
                _lastRecoverableError = null;
                if (generation == _generation) _dirty = null;
            }

            return FlushResult.Proceed;
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private void CancelScheduledSave()
    {
        lock (_stateGate)
        {
            _scheduledSave?.Cancel();
            _scheduledSave?.Dispose();
            _scheduledSave = null;
        }
    }
}
