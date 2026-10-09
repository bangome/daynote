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
/// (docs/APPLE_EXTENSIONS.md).
/// </summary>
/// <remarks>
/// Called from the same place the reminders are — after the to-do lists are rebuilt, which every
/// edit, tick, sync pull and language switch goes through — with the rows already read. Runs never
/// overlap; a call during a run is folded into one more run afterwards, as with reminders.
/// <para>
/// The snapshot is only published when something in it changed, not on every pass: a widget
/// reload costs the extension a slice of its daily budget, and the watch's context is a transfer.
/// The time it was written is left out of that comparison, or nothing would ever be the same.
/// </para>
/// </remarks>
public sealed class GlanceCoordinator
{
    private readonly IGlanceHost _host;
    private readonly IAgendaRepository _agenda;
    private readonly INoteRepository _notes;
    private readonly IClock _clock;
    private Task? _run;
    private bool _again;
    private IReadOnlyList<AgendaItem>? _latestItems;
    private bool _latestLocked;
    private string? _published;

    public GlanceCoordinator(IGlanceHost host, IAgendaRepository agenda, INoteRepository notes, IClock clock)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
        _notes = notes ?? throw new ArgumentNullException(nameof(notes));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Folder = host.Folder is { } root ? new GlanceFolder(root) : null;
    }

    /// <summary>The shared folder, or null when this build cannot reach one.</summary>
    public GlanceFolder? Folder { get; }

    /// <summary>Raised when the host put actions in the queue while the app was running.</summary>
    public event EventHandler? ActionsArrived
    {
        add => _host.ActionsArrived += value;
        remove => _host.ActionsArrived -= value;
    }

    /// <summary>
    /// Writes the snapshot if it changed.
    /// </summary>
    /// <param name="items">Every agenda row, when the caller has just read them; null reads them.</param>
    /// <param name="locked">The account's lock has the notes sealed.</param>
    /// <param name="force">Publish even if nothing changed: a widget may have written its own guess over the file.</param>
    public Task RefreshAsync(IReadOnlyList<AgendaItem>? items = null, bool locked = false, bool force = false)
    {
        if (Folder is null)
        {
            return Task.CompletedTask;
        }

        _latestItems = items;
        _latestLocked = locked;
        if (force)
        {
            _published = null;
        }

        if (_run is { IsCompleted: false } running)
        {
            _again = true;
            return running;
        }

        _run = RunLoopAsync();
        return _run;
    }

    /// <summary>
    /// Carries out every queued action, oldest first, and answers with what each changed. Each file
    /// is deleted once it has been applied, and a file that will not apply is deleted too rather
    /// than retried forever.
    /// </summary>
    public async Task<IReadOnlyList<GlanceApplied>> DrainAsync(GlanceActionApplier applier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applier);
        if (Folder is not { } folder)
        {
            return [];
        }

        var applied = new List<GlanceApplied>();
        foreach ((string path, GlanceAction? action) in folder.ReadActions())
        {
            if (action is not null)
            {
                try
                {
                    applied.Add(await applier.ApplyAsync(action, cancellationToken).ConfigureAwait(true));
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
                {
                    // A store that is failing now will fail the same way on every retry; the
                    // action is lost rather than the whole queue stuck behind it.
                    System.Diagnostics.Trace.TraceError($"Applying {action.Type} {action.Id} failed: {exception}");
                }
            }

            GlanceFolder.Delete(path);
        }

        return applied;
    }

    private async Task RunLoopAsync()
    {
        do
        {
            _again = false;
            IReadOnlyList<AgendaItem>? items = _latestItems;
            _latestItems = null;
            try
            {
                await RunOnceAsync(items, _latestLocked).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Nothing waits on a widget pass; the next change writes the file again.
                System.Diagnostics.Trace.TraceError($"Widget snapshot failed: {exception}");
            }
        }
        while (_again);
    }

    private async Task RunOnceAsync(IReadOnlyList<AgendaItem>? items, bool locked)
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
            locked);

        string content = GlanceSnapshotBuilder.Serialize(snapshot with { GeneratedUtc = string.Empty });
        if (string.Equals(content, _published, StringComparison.Ordinal))
        {
            return;
        }

        string json = GlanceSnapshotBuilder.Serialize(snapshot);
        Folder!.WriteSnapshot(json);
        _published = content;
        _host.Published(json);
    }
}
