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
public sealed partial class SingleInstanceCoordinator : IAsyncDisposable
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
