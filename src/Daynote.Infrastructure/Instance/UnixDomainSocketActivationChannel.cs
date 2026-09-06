using System.Net.Sockets;

namespace Daynote.Infrastructure.Instance;

/// <summary>
/// The portable activation channel: a Unix domain socket at a path inside the per-user runtime
/// directory. Secondary launches connect, write one activation byte, and leave; the primary raises its
/// listener per signal. Mirrors <see cref="NamedPipeActivationChannel"/> byte for byte.
/// </summary>
public sealed class UnixDomainSocketActivationChannel : IActivationChannel
{
    private const byte ActivationByte = 0x1;
    private readonly string _socketPath;
    private readonly CancellationTokenSource _stopping = new();
    private Socket? _listener;
    private Task? _listenLoop;
    private Action? _onActivation;
    private bool _disposed;

    public UnixDomainSocketActivationChannel(string socketPath)
    {
        _socketPath = string.IsNullOrWhiteSpace(socketPath)
            ? throw new ArgumentException("Socket path required.", nameof(socketPath))
            : socketPath;
    }

    public void StartListening(Action onActivation)
    {
        _onActivation = onActivation ?? throw new ArgumentNullException(nameof(onActivation));

        // A previous primary that died without cleanup leaves the socket file behind; the lock file
        // already proved nobody is listening on it, so removing it is safe.
        if (File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_socketPath)!);
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(backlog: 4);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        _listenLoop = Task.Run(() => ListenAsync(_listener, _stopping.Token));
    }

    public async Task<bool> SignalAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), timeoutSource.Token).ConfigureAwait(false);
            await client.SendAsync(new[] { ActivationByte }, SocketFlags.None, timeoutSource.Token).ConfigureAwait(false);
            client.Shutdown(SocketShutdown.Send);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ListenAsync(Socket listener, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using Socket connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                int read = await connection.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                if (read == 1 && buffer[0] == ActivationByte)
                {
                    _onActivation?.Invoke();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // A client that vanished mid-handshake; keep listening.
            }
            catch (ObjectDisposedException)
            {
                return;
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
        _listener?.Dispose();

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

        try
        {
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }
        }
        catch (IOException)
        {
        }

        _stopping.Dispose();
    }
}
