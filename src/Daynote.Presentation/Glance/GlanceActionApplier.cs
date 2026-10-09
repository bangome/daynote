using System.Globalization;
using Daynote.App.Notes;
using Daynote.Core.Agenda;

namespace Daynote.App.Glance;

/// <summary>What carrying out one action changed, for the caller to show.</summary>
/// <param name="Made">The item a capture made, or null.</param>
/// <param name="NoteLine">A capture went into today's note as a line instead.</param>
/// <param name="Retry">
/// Nothing was done, but it could be later — the note would not save just now. The action stays in
/// the queue. Everything else, including "the row has gone", is final.
/// </param>
public readonly record struct GlanceApplied(bool Changed, AgendaItem? Made = null, bool NoteLine = false, bool Retry = false);

/// <summary>
/// Carries out what a widget or the watch queued, through the app's own store, so it syncs and
/// reminds like anything done in the app (docs/APPLE_EXTENSIONS.md §4).
/// </summary>
/// <remarks>
/// <b>Every action is safe to apply twice.</b> The queue is drained by deleting files after they
/// are applied, and a crash between the two applies the same file again on the next launch. A
/// completion therefore completes rather than toggles — something already done stays done — and a
/// capture makes its item under the action's own id, so a second pass finds it there.
/// </remarks>
/// <param name="beforeComplete">
/// Told the row a completion is about to tick, so a shell holding its own tick on that row (motion
/// M3's wait before the write) can drop it rather than write a second tick after this one.
/// </param>
public sealed class GlanceActionApplier(
    IAgendaRepository agenda,
    Func<string, DateOnly, CancellationToken, Task<bool>> appendNoteLine,
    Func<DateTimeOffset>? utcNow = null,
    Action<AgendaDayRow>? beforeComplete = null)
{
    private readonly IAgendaRepository agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));

    private readonly Func<string, DateOnly, CancellationToken, Task<bool>> appendNoteLine =
        appendNoteLine ?? throw new ArgumentNullException(nameof(appendNoteLine));

    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<GlanceApplied> ApplyAsync(GlanceAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.Type switch
        {
            GlanceActionTypes.Complete => await CompleteAsync(action, cancellationToken).ConfigureAwait(true),
            GlanceActionTypes.Capture => await CaptureAsync(action, cancellationToken).ConfigureAwait(true),
            // A newer widget's action this build does not know: dropped rather than guessed at.
            _ => new GlanceApplied(false),
        };
    }

    /// <summary>
    /// Ticks the row the widget showed, found again the way the day panel finds it.
    /// </summary>
    /// <remarks>
    /// Looked up rather than trusted: between the tap and the drain the row may have been deleted,
    /// moved, or ticked on another device. A one-off is matched by its id; an occurrence by its
    /// series and <c>RECURRENCE-ID</c>, because the override that will carry the tick does not
    /// exist yet and the widget could not have known its id.
    /// </remarks>
    private async Task<GlanceApplied> CompleteAsync(GlanceAction action, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(action.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            || !Guid.TryParse(action.ItemId, out Guid itemId))
        {
            return new GlanceApplied(false);
        }

        Guid? seriesId = Guid.TryParse(action.SeriesId, out Guid series) ? series : null;
        WallClock? occurrence = action.Occurrence is { Length: > 0 } text ? WallClock.Parse(text) : null;

        IReadOnlyList<AgendaItem> items = await agenda.GetAllAsync(cancellationToken).ConfigureAwait(true);
        AgendaDayView day = AgendaDay.For(date, items);
        // An occurrence is matched by which occurrence it is, never by id alone: every occurrence
        // of a rule carries the series' id until it has an override, so a rule that fires twice a
        // day has two rows with that id and only the RECURRENCE-ID tells them apart.
        AgendaDayRow? row = day.Open.Concat(day.Done).Cast<AgendaDayRow?>().FirstOrDefault(candidate =>
            candidate is { } r
            && (occurrence is { } o
                ? r.RecurrenceId == o && (r.Item.Id == itemId || r.Item.SeriesId == itemId
                    || (seriesId is { } s && (r.Item.Id == s || r.Item.SeriesId == s)))
                : r.Item.Id == itemId && r.RecurrenceId is null));

        if (row is not { IsDone: false } open)
        {
            return new GlanceApplied(false);
        }

        beforeComplete?.Invoke(open);
        await new ToggleAgendaItem(agenda, utcNow).ToggleAsync(open, cancellationToken).ConfigureAwait(true);
        return new GlanceApplied(true);
    }

    /// <summary>
    /// A dictated sentence, made into what the watch's readback offered.
    /// </summary>
    /// <remarks>
    /// Read again here with the app's parser, against the moment it was said rather than the
    /// moment the phone got round to it — "오늘 5시" said at 23:50 is still today. The watch read
    /// it back with its port of the same parser; the two are held to one table of readings
    /// (tests/fixtures/agenda-phrase-vectors.json), and this one is the reading that is kept.
    /// A sentence the parser finds no date in can only have been offered as a line in the note.
    /// </remarks>
    private async Task<GlanceApplied> CaptureAsync(GlanceAction action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Text) || !Guid.TryParse(action.Id, out Guid itemId))
        {
            return new GlanceApplied(false);
        }

        DateTime said = DateTime.TryParseExact(
            action.CapturedLocal, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : DateTime.Now;

        AgendaKind? kind = action.Kind switch
        {
            "task" => AgendaKind.Task,
            "event" => AgendaKind.Event,
            _ => null,
        };

        if (kind is { } chosen && AgendaPhraseParser.ParseTrailing(action.Text, said) is { } read)
        {
            if (await agenda.GetAsync(itemId, cancellationToken).ConfigureAwait(true) is not null)
            {
                return new GlanceApplied(false);
            }

            var state = new AgendaCaptureState(0, 0, read.Title, action.Text, read.Phrase);
            AgendaItem made = AgendaCapture.Compose(state, chosen, Guid.Empty, itemId, utcNow()) with
            {
                // Said into a watch, not typed into a note: there is nothing to jump back to.
                SourceNoteId = null,
            };
            await agenda.SaveAsync(made, cancellationToken).ConfigureAwait(true);
            return new GlanceApplied(true, made);
        }

        // False means the note would not save; the line is not lost, it is tried again.
        bool appended = await appendNoteLine(action.Text.Trim(), DateOnly.FromDateTime(said), cancellationToken).ConfigureAwait(true);
        return new GlanceApplied(appended, NoteLine: appended, Retry: !appended);
    }
}
