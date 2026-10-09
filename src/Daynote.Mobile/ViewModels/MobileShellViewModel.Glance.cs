using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Glance;
using Daynote.Core.Domain;
using Daynote.Mobile.Glance;

namespace Daynote.Mobile.ViewModels;

/// <summary>
/// The widgets and the watch: keeping their snapshot current, carrying out what was done on them,
/// and the links they open the app with (docs/APPLE_EXTENSIONS.md).
/// </summary>
public sealed partial class MobileShellViewModel
{
    /// <summary>Writes the snapshot and drains the queue, or null on a head without extensions. Set by composition.</summary>
    public GlanceCoordinator? Glance
    {
        get => _glance;
        init
        {
            _glance = value;
            if (value is not null)
            {
                // Posted, not run inline: the watch relay raises this from its own queue.
                value.ActionsArrived += (_, _) => Dispatcher.UIThread.Post(() => _ = RunQuietlyAsync(DrainGlanceAsync));
            }
        }
    }

    private readonly GlanceCoordinator? _glance;

    /// <summary>Rewrites the snapshot in the background, when anything in it may have changed.</summary>
    private void RefreshGlance(IReadOnlyList<Daynote.Core.Agenda.AgendaItem>? items = null, bool force = false)
    {
        if (Glance is { } glance && !_disposed)
        {
            _ = RunQuietlyAsync(() => glance.RefreshAsync(items, Account is { IsLocked: true }, force));
        }
    }

    /// <summary>
    /// Carries out what was done on a widget or the watch since the app last looked, then shows
    /// it: the lists are read again and the snapshot rewritten from the store, which replaces the
    /// widget's own guess at what its tap did.
    /// </summary>
    public async Task DrainGlanceAsync()
    {
        if (Glance is not { } glance || _disposed)
        {
            return;
        }

        var applier = new GlanceActionApplier(_agenda, AppendNoteLineAsync);
        IReadOnlyList<GlanceApplied> applied = await glance.DrainAsync(applier).ConfigureAwait(true);
        if (applied.Count == 0)
        {
            return;
        }

        if (applied.Any(static result => result.Changed))
        {
            await RefreshAfterStructureChangeAsync().ConfigureAwait(true);
        }

        RefreshGlance(force: true);
    }

    /// <summary>
    /// "노트에 한 줄" from the watch. The editor's draft is saved first and the day loaded again
    /// after, so the open note and the line written under it cannot end up as two revisions.
    /// </summary>
    private async Task<bool> AppendNoteLineAsync(string line, DateOnly date, CancellationToken cancellationToken)
    {
        if (!(await Notes.FlushAsync(Daynote.Core.Notes.FlushReason.NoteChange, cancellationToken).ConfigureAwait(true)).CanProceed)
        {
            return false;
        }

        bool appended = await GlanceNoteLine.AppendAsync(_repository, line, date, cancellationToken).ConfigureAwait(true);
        if (appended && LocalDates.ToDateOnly(SelectedDate) == date)
        {
            await Notes.LoadAsync(SelectedDate, cancellationToken).ConfigureAwait(true);
        }

        return appended;
    }

    /// <summary>
    /// Follows a <c>daynote://</c> link from a widget, a control or the watch.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>daynote://capture</c> — "+ 새 노트": today's note, opened. Today's first note
    /// rather than another empty one, because the control is called "오늘 노트" and a tap a day
    /// would otherwise leave a trail of blank notes.</item>
    /// <item><c>daynote://capture?at=1</c> — "@ 할 일·일정": the same note with a new line at its end
    /// and the <c>@</c> bar open on it.</item>
    /// <item><c>daynote://day?date=yyyy-MM-dd</c> — that day.</item>
    /// <item><c>daynote://note?date=yyyy-MM-dd&amp;id=…</c> — that note, as a reminder tap opens it.</item>
    /// <item><c>daynote://todos</c> — the to-do list.</item>
    /// </list>
    /// Anything else lands on today, which is where every widget is pointing anyway.
    /// </remarks>
    public async Task OpenLinkAsync(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        Dictionary<string, string> query = ParseQuery(link.Query);
        LocalDate today = LocalDates.Today(_clock);

        switch (link.Host)
        {
            case "note" when query.TryGetValue("id", out string? id) && Guid.TryParse(id, out Guid noteId)
                && query.TryGetValue("date", out string? noteDate) && LocalDate.Parse(noteDate) is { IsSuccess: true } day:
                await OpenReminderAsync(day.Value, noteId).ConfigureAwait(true);
                return;

            case "day" when query.TryGetValue("date", out string? date) && LocalDate.Parse(date) is { IsSuccess: true } chosen:
                await CloseEditorAsync().ConfigureAwait(true);
                GoToPage(MobilePage.Day);
                await SelectDateAsync(chosen.Value).ConfigureAwait(true);
                return;

            case "todos":
                await CloseEditorAsync().ConfigureAwait(true);
                GoToPage(MobilePage.Lists);
                ActiveList = Daynote.App.Shell.Product.RightTab.Todo;
                return;

            case "capture":
                await OpenTodayNoteAsync(today, withAt: query.TryGetValue("at", out string? at) && at == "1").ConfigureAwait(true);
                return;

            default:
                await CloseEditorAsync().ConfigureAwait(true);
                GoToPage(MobilePage.Day);
                await SelectDateAsync(today).ConfigureAwait(true);
                return;
        }
    }

    private async Task OpenTodayNoteAsync(LocalDate today, bool withAt)
    {
        await CloseEditorAsync().ConfigureAwait(true);
        if (IsEditorOpen)
        {
            return;
        }

        GoToPage(MobilePage.Day);
        if (!await SelectDateAsync(today).ConfigureAwait(true))
        {
            return;
        }

        if (Notes.Tabs.FirstOrDefault(static tab => !tab.IsProjection) is { } first)
        {
            await OpenNote(first).ConfigureAwait(true);
        }
        else
        {
            await NewNote().ConfigureAwait(true);
        }

        if (withAt && IsEditorOpen)
        {
            CaptureRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                values[Uri.UnescapeDataString(pair[..equals])] = Uri.UnescapeDataString(pair[(equals + 1)..]);
            }
        }

        return values;
    }
}
