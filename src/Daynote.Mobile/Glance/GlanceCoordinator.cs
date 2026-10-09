using System.Text.Json;
using Daynote.App.Glance;
using Daynote.App.Localization;
using Daynote.Core.Agenda;
using Daynote.Core.Notes;
using Daynote.Core.Time;
using Daynote.Mobile.Platform;

namespace Daynote.Mobile.Glance;

/// <summary>
/// Keeps the widgets and the watch in step with the app: writes the snapshot when the day's
/// to-dos, events or notes change, and carries out what was done on them in the meantime
/// (docs/APPLE_EXTENSIONS.md). The Mac's <c>GlanceRelay</c> follows the same rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing until <see cref="StartAsync"/>.</b> The shell starts it once the day has loaded and
/// the account has been read. Before that the rows are empty, which would blank every widget, and
/// whether the account lock has the notes sealed is not known yet, which would put titles in a
/// file the lock says must not have them.
/// </para>
/// <para>
/// <b>One drain at a time, and a drain asked for during one runs again.</b> Resume, a watch
/// transfer and the start can all ask at once; two passes over the same listing would apply an
/// action twice — a second override on a repeating to-do, a line written into the note twice —
/// and dropping the request instead would leave a tick queued behind a publish that un-ticks it.
/// </para>
/// <para>
/// <b>An action is deleted when it is done or can never be done:</b> applied, unreadable, of a
/// type this build does not know, or naming a row that has gone. One that throws, or that asks to
/// be tried again (a note that would not save), is kept for the next drain, for at most
/// <see cref="RetryFor"/>.
/// </para>
/// <para>
/// <b>Compared with the file, not with what was last written.</b> A widget writes its own guess
/// into the snapshot with a read-modify-write that can land after the app's newer one; comparing
/// with the file is what notices. The stamp is left out, or nothing would ever be the same.
/// </para>
/// </remarks>
public sealed class GlanceCoordinator
{
    /// <summary>How long an action that keeps failing is retried; the Mac keeps the same day.</summary>
    public static readonly TimeSpan RetryFor = TimeSpan.FromDays(1);

    private readonly IGlanceHost _host;
    private readonly IAgendaRepository _agenda;
    private readonly INoteRepository _notes;
    private readonly IClock _clock;
    private readonly Func<DateTime> _utcNow;
    private Func<GlanceAction, Task<GlanceApplied>>? _apply;
    private Func<Task>? _afterDrain;
    private Func<bool> _locked = static () => true;
    private Task? _run;
    private bool _again;
    private bool _forceNext;
    private IReadOnlyList<AgendaItem>? _latestItems;
    private bool _draining;
    private bool _drainRequested;

    public GlanceCoordinator(
        IGlanceHost host, IAgendaRepository agenda, INoteRepository notes, IClock clock, Func<DateTime>? utcNow = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
        _notes = notes ?? throw new ArgumentNullException(nameof(notes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        Folder = host.Folder is { } root ? new GlanceFolder(root) : null;
    }

    /// <summary>The shared folder, or null when this build cannot reach one.</summary>
    public GlanceFolder? Folder { get; }

    public bool IsStarted { get; private set; }

    /// <summary>Raised when the host put actions in the queue while the app was running.</summary>
    public event EventHandler? ActionsArrived
    {
        add => _host.ActionsArrived += value;
        remove => _host.ActionsArrived -= value;
    }

    /// <summary>
    /// The day and the account are known: drain what waited, then publish.
    /// </summary>
    /// <param name="apply">Carries out one action through the app's store.</param>
    /// <param name="afterDrain">Re-reads what the screens show once a drain changed something.</param>
    /// <param name="locked">Whether the account lock has the notes sealed, asked at every publish.</param>
    public async Task StartAsync(Func<GlanceAction, Task<GlanceApplied>> apply, Func<Task> afterDrain, Func<bool> locked)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _afterDrain = afterDrain ?? throw new ArgumentNullException(nameof(afterDrain));
        _locked = locked ?? throw new ArgumentNullException(nameof(locked));
        if (Folder is null)
        {
            return;
        }

        IsStarted = true;
        await DrainAsync().ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Writes the snapshot if it differs from the file.
    /// </summary>
    /// <param name="items">Every agenda row, when the caller has just read them; null reads them.</param>
    /// <param name="force">Write even if it does not differ — the lock just changed, say.</param>
    public Task RefreshAsync(IReadOnlyList<AgendaItem>? items = null, bool force = false)
    {
        if (!IsStarted)
        {
            return Task.CompletedTask;
        }

        _latestItems = items;
        _forceNext |= force;
        if (_run is { IsCompleted: false } running)
        {
            _again = true;
            return running;
        }

        _run = RunLoopAsync();
        return _run;
    }

    /// <summary>
    /// Carries out every queued action, oldest first, then has the screens and the snapshot read
    /// the store again — the store's answer replaces the widget's guess.
    /// </summary>
    /// <remarks>All calls are on the UI thread, so a flag is enough to fold a reentrant one in.</remarks>
    public async Task DrainAsync()
    {
        if (!IsStarted)
        {
            return;
        }

        if (_draining)
        {
            _drainRequested = true;
            return;
        }

        _draining = true;
        try
        {
            do
            {
                _drainRequested = false;
                if (await ApplyQueuedAsync().ConfigureAwait(true))
                {
                    await _afterDrain!().ConfigureAwait(true);
                    await RefreshAsync(force: true).ConfigureAwait(true);
                }
            }
            while (_drainRequested);
        }
        finally
        {
            _draining = false;
        }
    }

    /// <summary>One pass over the queue. True when any file was taken off it.</summary>
    private async Task<bool> ApplyQueuedAsync()
    {
        bool any = false;
        foreach ((string path, GlanceAction? action) in Folder!.ReadActions())
        {
            if (action is not null)
            {
                string? retry;
                try
                {
                    GlanceApplied applied = await _apply!(action).ConfigureAwait(true);
                    retry = applied.Retry ? "it asked to be tried again" : null;
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
                {
                    retry = exception.Message;
                }

                if (retry is not null)
                {
                    if (_utcNow() - File.GetLastWriteTimeUtc(path) < RetryFor)
                    {
                        System.Diagnostics.Trace.TraceWarning($"Applying {action.Type} {action.Id} kept for retry: {retry}");
                        continue;
                    }

                    System.Diagnostics.Trace.TraceError($"Applying {action.Type} {action.Id} kept failing, dropped: {retry}");
                }
            }

            GlanceFolder.Delete(path);
            any = true;
        }

        return any;
    }

    private async Task RunLoopAsync()
    {
        do
        {
            _again = false;
            IReadOnlyList<AgendaItem>? items = _latestItems;
            _latestItems = null;
            bool force = _forceNext;
            _forceNext = false;
            try
            {
                await PublishAsync(items, force).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Nothing waits on a widget pass; the next change writes the file again.
                System.Diagnostics.Trace.TraceError($"Widget snapshot failed: {exception}");
            }
        }
        while (_again);
    }

    private async Task PublishAsync(IReadOnlyList<AgendaItem>? items, bool force)
    {
        items ??= await _agenda.GetAllAsync().ConfigureAwait(true);
        IReadOnlyList<AgendaList> lists = await _agenda.GetListsAsync().ConfigureAwait(true);
        IReadOnlyList<NoteSummary> notes = await _notes.GetAllNotesAsync().ConfigureAwait(true);

        ClockSnapshot clock = _clock.Read();
        DateTimeOffset utcNow = clock.UtcInstant;
        GlanceSnapshot snapshot = GlanceSnapshotBuilder.Build(
            items,
            lists,
            notes,
            utcNow.ToOffset(clock.LocalUtcOffset).DateTime,
            utcNow,
            AgendaZone.Local(),
            LocalizationService.Instance.Language,
            _locked());

        if (!force && string.Equals(Unstamped(snapshot), OnDisk(), StringComparison.Ordinal))
        {
            return;
        }

        string json = GlanceSnapshotBuilder.Serialize(snapshot);
        Folder!.WriteSnapshot(json);
        _host.Published(json);
    }

    /// <summary>The file as this app would have written it, stamp aside; null when absent or unreadable.</summary>
    private string? OnDisk()
    {
        try
        {
            if (!File.Exists(Folder!.SnapshotPath))
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
