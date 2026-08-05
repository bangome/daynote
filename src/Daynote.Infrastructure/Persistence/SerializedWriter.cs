using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Persistence;

public sealed class SerializedWriter : IAsyncDisposable
{
    private readonly Channel<IWriteWork> _channel;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly Task _pump;
    private readonly object _completionLock = new();
    private bool _accepting = true;
    private int _activeEnqueues;
    private Exception? _terminalFailure;

    public SerializedWriter(SqliteConnectionFactory connectionFactory, int capacity)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        _channel = Channel.CreateBounded<IWriteWork>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        _pump = Task.Run(PumpAsync);
    }

    public int Capacity { get; }

    public Task Completion => _pump;

    public async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_completionLock)
        {
            if (!_accepting)
            {
                if (_terminalFailure is { } terminalFailure)
                {
                    throw terminalFailure;
                }

                throw new ObjectDisposedException(nameof(SerializedWriter));
            }

            _activeEnqueues++;
        }

        WriteWork<TResult> work;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            work = new WriteWork<TResult>(operation, cancellationToken);
            try
            {
                await _channel.Writer.WriteAsync(work, cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                work.Dispose();
                lock (_completionLock)
                {
                    if (_terminalFailure is { } terminalFailure)
                    {
                        throw terminalFailure;
                    }
                }

                throw new ObjectDisposedException(nameof(SerializedWriter));
            }
            catch (OperationCanceledException)
            {
                work.Dispose();
                throw;
            }
        }
        finally
        {
            lock (_completionLock)
            {
                _activeEnqueues--;
                if (!_accepting && _activeEnqueues == 0)
                {
                    _channel.Writer.TryComplete();
                }
            }
        }

        return await work.Completion.ConfigureAwait(false);
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (_completionLock)
        {
            if (_accepting)
            {
                _accepting = false;
                if (_activeEnqueues == 0)
                {
                    _channel.Writer.TryComplete();
                }
            }
        }

        await _pump.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => CompleteAsync();

    private async Task PumpAsync()
    {
        IWriteWork? currentWork = null;
        try
        {
            using var connection = _connectionFactory.OpenConnection();
            await foreach (var work in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                currentWork = work;
                work.Execute(connection);
                currentWork = null;
            }
        }
        catch (Exception exception)
        {
            currentWork?.Fail(exception);
            lock (_completionLock)
            {
                _accepting = false;
                _terminalFailure = exception;
                _channel.Writer.TryComplete(exception);
            }

            while (_channel.Reader.TryRead(out var work))
            {
                work.Fail(exception);
            }

            throw;
        }
    }

    private interface IWriteWork
    {
        void Execute(SqliteConnection connection);

        void Fail(Exception exception);
    }

    private sealed class WriteWork<TResult> : IWriteWork, IDisposable
    {
        private readonly Func<SqliteConnection, SqliteTransaction, CancellationToken, TResult> _operation;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<TResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        private int _state;

        public WriteWork(
            Func<SqliteConnection, SqliteTransaction, CancellationToken, TResult> operation,
            CancellationToken cancellationToken)
        {
            _operation = operation;
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.Register(CancelWhileQueued, this);
        }

        public Task<TResult> Completion => _completion.Task;

        public void Execute(SqliteConnection connection)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                Dispose();
                return;
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var result = _operation(connection, transaction, _cancellationToken);
                transaction.Commit();
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                transaction.Rollback();
                _completion.TrySetCanceled(_cancellationToken);
            }
            catch (Exception exception)
            {
                transaction.Rollback();
                _completion.TrySetException(exception);
            }
            finally
            {
                Interlocked.Exchange(ref _state, 2);
                Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            Interlocked.Exchange(ref _state, 2);
            _completion.TrySetException(exception);
            Dispose();
        }

        public void Dispose() => _registration.Dispose();

        private static void CancelWhileQueued(object? state)
        {
            var work = (WriteWork<TResult>)state!;
            if (Interlocked.CompareExchange(ref work._state, 2, 0) == 0)
            {
                work._completion.TrySetCanceled(work._cancellationToken);
            }
        }
    }
}
