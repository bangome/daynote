using System.Text.Json;
using Daynote.App.Glance;

namespace Daynote.Desktop.Platform;

/// <summary>
/// The part of <see cref="MacWidgetBridge"/> that decides when to write the snapshot and how to
/// drain the queue, with the app and WidgetKit passed in so a test can drive it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing until <see cref="StartAsync"/>.</b> Before the shell has read the day, the rows it would
/// publish are empty, and an empty snapshot would blank every widget on the desktop until the
/// next edit.
/// </para>
/// <para>
/// <b>A drain asked for during a drain runs again.</b> The watcher fires per file, and a tick
/// landing while another is being applied is not in the listing already taken; dropping that
/// request would leave the tick queued and the forced publish after the drain would un-tick it on
/// the widget.
/// </para>
/// <para>
/// <b>An action is deleted when it is done or can never be done</b> — applied, unreadable, or
/// naming a row that no longer exists, which the applier answers without throwing. One that
/// throws is kept for the next drain: a busy database or a store not ready yet will be fine in a
/// moment. Kept for at most <see cref="RetryFor"/>, so one that always throws cannot sit there
/// forever.
/// </para>
/// <para>
/// <b>Compared with the file, not with what was last written.</b> The widget edits the snapshot
/// too (its optimistic tick, a read-modify-write), and may land after the app's newer one; the
/// app only notices if it compares what it would write with what is actually there. The stamp
/// is left out of the comparison, or nothing would ever be the same.
/// </para>
/// </remarks>
public sealed class GlanceRelay(
    GlanceFolder folder,
    Func<Task<GlanceSnapshot>> build,
    Func<GlanceAction, Task> apply,
    Func<Task> afterDrain,
    Action reload,
    Func<DateTime>? utcNow = null)
{
    /// <summary>How long an action that keeps throwing is retried before it is given up on.</summary>
    public static readonly TimeSpan RetryFor = TimeSpan.FromDays(1);

    private readonly Func<DateTime> utcNow = utcNow ?? (() => DateTime.UtcNow);
    private bool draining;
    private bool drainRequested;

    public GlanceFolder Folder { get; } = folder ?? throw new ArgumentNullException(nameof(folder));

    public bool IsStarted { get; private set; }

    /// <summary>The shell has read the day: drain what waited, and publish what it read.</summary>
    public async Task StartAsync()
    {
        IsStarted = true;
        await DrainAsync().ConfigureAwait(true);
        await PublishAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Writes the snapshot when what would be written differs from the file, or always when
    /// <paramref name="force"/>. Answers whether it wrote.
    /// </summary>
    public async Task<bool> PublishAsync(bool force = false)
    {
        if (!IsStarted)
        {
            return false;
        }

        GlanceSnapshot snapshot = await build().ConfigureAwait(true);
        string content = Unstamped(snapshot);
        if (!force && string.Equals(content, OnDisk(), StringComparison.Ordinal))
        {
            return false;
        }

        Folder.WriteSnapshot(GlanceSnapshotBuilder.Serialize(snapshot));
        reload();
        return true;
    }

    /// <summary>
    /// Carries out every queued action, oldest first, then refreshes and republishes — the store's
    /// answer replaces the widget's guess. Asked for again while running, it runs again.
    /// </summary>
    public async Task DrainAsync()
    {
        if (!IsStarted)
        {
            return;
        }

        // Everything runs on the UI thread, so a flag is enough to fold reentrant calls in.
        if (draining)
        {
            drainRequested = true;
            return;
        }

        draining = true;
        try
        {
            do
            {
                drainRequested = false;
                if (await ApplyQueuedAsync().ConfigureAwait(true))
                {
                    await afterDrain().ConfigureAwait(true);
                    await PublishAsync(force: true).ConfigureAwait(true);
                }
            }
            while (drainRequested);
        }
        finally
        {
            draining = false;
        }
    }

    /// <summary>One pass over the queue. True when any file was taken off it.</summary>
    private async Task<bool> ApplyQueuedAsync()
    {
        bool any = false;
        foreach ((string path, GlanceAction? action) in Folder.ReadActions())
        {
            if (action is not null)
            {
                try
                {
                    await apply(action).ConfigureAwait(true);
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
                {
                    if (utcNow() - File.GetLastWriteTimeUtc(path) < RetryFor)
                    {
                        System.Diagnostics.Trace.TraceWarning($"Applying {action.Type} {action.Id} failed, kept for retry: {exception.Message}");
                        continue;
                    }

                    System.Diagnostics.Trace.TraceError($"Applying {action.Type} {action.Id} kept failing, dropped: {exception}");
                }
            }

            GlanceFolder.Delete(path);
            any = true;
        }

        return any;
    }

    /// <summary>The file as this app would have written it, stamp aside; null when absent or unreadable.</summary>
    private string? OnDisk()
    {
        try
        {
            if (!File.Exists(Folder.SnapshotPath))
            {
                return null;
            }

            // Read back and written again, so the widget's encoder (key order, escaping) is not a
            // difference; only the content is.
            GlanceSnapshot? existing = JsonSerializer.Deserialize(
                File.ReadAllText(Folder.SnapshotPath), GlanceJson.Default.GlanceSnapshot);
            return existing is null ? null : Unstamped(existing);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string Unstamped(GlanceSnapshot snapshot) =>
        GlanceSnapshotBuilder.Serialize(snapshot with { GeneratedUtc = string.Empty });
}
