using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Threading;

namespace Daynote.Infrastructure.Instance;

public enum SingleInstanceRole
{
    Primary,
    Secondary,
}

/// <summary>Claims the single "primary" slot. The real claim is a per-user named mutex.</summary>
public interface IPrimaryClaim : IDisposable
{
    bool TryClaim();
}

/// <summary>Carries an activation signal from a secondary launch to the primary process.</summary>
public interface IActivationChannel : IAsyncDisposable
{
    void StartListening(Action onActivation);

    Task<bool> SignalAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Enforces a single primary process per Windows user: the first launch claims the mutex and listens
/// on a current-user-only named pipe; every later launch signals that primary to activate its window
/// and then exits. Exactly one primary ever runs (plan Todo 10; DESIGN Section 1).
/// </summary>
public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly IPrimaryClaim _claim;
    private readonly IActivationChannel _channel;
    private bool _isPrimary;
    private bool _disposed;

    public SingleInstanceCoordinator(IPrimaryClaim claim, IActivationChannel channel)
    {
        _claim = claim ?? throw new ArgumentNullException(nameof(claim));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>Raised on the primary when a secondary launch requests activation.</summary>
    public event EventHandler? ActivationRequested;

    [SupportedOSPlatform("windows")]
    public static SingleInstanceCoordinator ForCurrentUser(string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        string sid = CurrentUserPipeSecurity.CurrentUserSid();
        return new SingleInstanceCoordinator(
            new MutexPrimaryClaim($@"Local\{baseName}-{sid}"),
            new NamedPipeActivationChannel($"{baseName}-{sid}"));
    }

    /// <summary>Claims the primary slot; a secondary returns without listening.</summary>
    public SingleInstanceRole Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_claim.TryClaim())
        {
            _isPrimary = true;
            _channel.StartListening(() => ActivationRequested?.Invoke(this, EventArgs.Empty));
            return SingleInstanceRole.Primary;
        }

        return SingleInstanceRole.Secondary;
    }

    /// <summary>From a secondary launch, asks the primary to activate. Returns whether it was signaled.</summary>
    public Task<bool> ActivatePrimaryAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _channel.SignalAsync(timeout, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_isPrimary)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        _claim.Dispose();
    }
}

/// <summary>A per-user named-mutex primary claim. Ownership is held for the process lifetime.</summary>
[SupportedOSPlatform("windows")]
public sealed class MutexPrimaryClaim : IPrimaryClaim
{
    private readonly string _name;
    private Mutex? _mutex;
    private bool _owned;

    public MutexPrimaryClaim(string name)
    {
        _name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Name required.", nameof(name)) : name;
    }

    public bool TryClaim()
    {
        _mutex ??= new Mutex(initiallyOwned: false, _name);
        try
        {
            _owned = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // A prior primary exited without releasing; ownership transfers to this process.
            _owned = true;
        }

        return _owned;
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
        _mutex = null;
    }
}

/// <summary>
/// A named-pipe activation channel whose server ACL is restricted to the current user SID. Secondary
/// launches connect and write a single activation byte; the primary raises its listener per signal.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NamedPipeActivationChannel : IActivationChannel
{
    private const byte ActivationByte = 0x1;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _listenLoop;
    private Action? _onActivation;
    private bool _disposed;

    public NamedPipeActivationChannel(string pipeName)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? throw new ArgumentException("Pipe name required.", nameof(pipeName))
            : pipeName;
    }

    public void StartListening(Action onActivation)
    {
        _onActivation = onActivation ?? throw new ArgumentNullException(nameof(onActivation));
        _listenLoop = Task.Run(() => ListenAsync(_stopping.Token));
    }

    public async Task<bool> SignalAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            await client.ConnectAsync((int)timeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            await client.WriteAsync(new[] { ActivationByte }, cancellationToken).ConfigureAwait(false);
            await client.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
                    _pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    CurrentUserPipeSecurity.Create());

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[1];
                int read = await server.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 1 && buffer[0] == ActivationByte)
                {
                    _onActivation?.Invoke();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A broken connection is expected when a client disconnects; keep listening.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel();

        // Unblock a pending WaitForConnectionAsync by connecting once from this process.
        try
        {
            using var nudge = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            await nudge.ConnectAsync(50).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }

        if (_listenLoop is not null)
        {
            try
            {
                await _listenLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stopping.Dispose();
    }
}
