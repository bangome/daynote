using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace Daynote.Motion;

/// <summary>
/// The ticks that have been drawn but not yet written: M3 holds each one for
/// <see cref="Choreography.CheckSettleMs"/> so a mistaken tap can be taken back.
/// </summary>
/// <remarks>
/// <para>
/// Held by the shell, keyed by the row's key, not by the checkbox. The lists are rebuilt whole on
/// every change, so a second tick inside the wait used to lose its checkbox - and with it the
/// command - the moment the first tick's write rebuilt the list. Here the command is captured when
/// the tap happens, a rebuilt row asks whether its key is pending and draws itself ticked, and a
/// tap on it cancels the one pending tick instead of writing a second.
/// </para>
/// <para>
/// Whatever is pending is written at once when the app flushes: going to the background, quitting,
/// switching profile.
/// </para>
/// </remarks>
public sealed class PendingTicks
{
    private sealed record Entry(Func<Task> Commit, CancellationTokenSource Wait);

    private readonly Dictionary<string, Entry> _pending = [];

    /// <summary>The wait itself, as a seam: a test should not depend on the wall clock.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>How long a tick is held before it is written.</summary>
    public static TimeSpan Settle => MotionEnvironment.Instant ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Choreography.CheckSettleMs);

    public int Count => _pending.Count;

    public bool IsPending(string? key) => key is not null && _pending.ContainsKey(key);

    /// <summary>Holds a tick on <paramref name="key"/>; <paramref name="toggle"/> is captured now and run when the wait ends.</summary>
    public void Begin(string key, ICommand? toggle)
    {
        ArgumentNullException.ThrowIfNull(key);
        Cancel(key);
        var entry = new Entry(() => Run(toggle), new CancellationTokenSource());
        _pending[key] = entry;
        _ = WaitAsync(key, entry);
    }

    /// <summary>Takes a held tick back. False when there was none to take.</summary>
    public bool Cancel(string? key)
    {
        if (key is null || !_pending.Remove(key, out Entry? entry))
        {
            return false;
        }

        entry.Wait.Cancel();
        entry.Wait.Dispose();
        return true;
    }

    /// <summary>Writes every held tick now.</summary>
    public Task CommitAll()
    {
        if (_pending.Count == 0)
        {
            return Task.CompletedTask;
        }

        var commits = new List<Task>();
        foreach (string key in _pending.Keys.ToList())
        {
            commits.Add(Commit(key));
        }

        return Task.WhenAll(commits);
    }

    private async Task WaitAsync(string key, Entry entry)
    {
        try
        {
            await Delay(Settle, entry.Wait.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_pending.TryGetValue(key, out Entry? current) && ReferenceEquals(current, entry))
        {
            await Commit(key).ConfigureAwait(true);
        }
    }

    private Task Commit(string key)
    {
        if (!_pending.Remove(key, out Entry? entry))
        {
            return Task.CompletedTask;
        }

        entry.Wait.Cancel();
        entry.Wait.Dispose();
        return entry.Commit();
    }

    private static Task Run(ICommand? toggle)
    {
        if (toggle is IAsyncRelayCommand async)
        {
            return async.ExecuteAsync(null);
        }

        toggle?.Execute(null);
        return Task.CompletedTask;
    }
}

/// <summary>What owns the ticks a list's checkboxes hold: the shell view model behind the screen.</summary>
public interface IPendingTickOwner
{
    PendingTicks Ticks { get; }
}
