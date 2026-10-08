using Daynote.Core.Agenda;

namespace Daynote.App.Notes;

/// <summary>What the <c>@</c> under the caret currently means.</summary>
/// <param name="AtIndex">Where the <c>@</c> is in the body.</param>
/// <param name="CaretIndex">Where the caret is. The phrase is everything between the two.</param>
/// <param name="Title">
/// The line to the left of the <c>@</c>, trimmed. §7: the title is already typed, so the popup
/// shows it rather than asking for it again.
/// </param>
/// <param name="Phrase">The raw text after the <c>@</c>, as typed.</param>
/// <param name="Reading">
/// What it was understood to mean, or null when nothing has been typed yet — which is the popup's
/// opening state, not a failure.
/// </param>
public readonly record struct AgendaCaptureState(
    int AtIndex,
    int CaretIndex,
    string Title,
    string Phrase,
    AgendaPhrase? Reading)
{
    /// <summary>True in the state that shows the four example chips instead of two readback lines.</summary>
    public bool IsPrompting => Reading is null;

    /// <summary>The span the editor turns into a chip once the item is made: the <c>@</c> and what it read.</summary>
    public int ChipLength => Reading is { } reading ? 1 + reading.Length : 1;
}

/// <summary>
/// Where the <c>@</c> popup is, what it says, and what pressing Enter makes (docs/TODOS.md §7).
/// </summary>
/// <remarks>
/// Pure, and deliberately not a view model: the same two decisions — is a popup open, and what
/// does the item look like — are made identically by the desktop popup and the phone's keyboard
/// bar, and the only thing that differs between them is how it is drawn.
/// <para>
/// <b>The trigger is the character, not a mode.</b> <c>@</c> stays an ordinary character: it opens
/// the popup only at the start of a word, and the popup closes again the moment what follows stops
/// reading as a date. "자료는 @지원 님께 전달" therefore never shows one.
/// </para>
/// </remarks>
public static class AgendaCapture
{
    /// <summary>
    /// The popup's state for a body and a caret, or null when there should be no popup.
    /// </summary>
    public static AgendaCaptureState? Detect(string? text, int caret, DateTime now)
    {
        if (string.IsNullOrEmpty(text) || caret < 0 || caret > text.Length)
        {
            return null;
        }

        int at = LastTriggerBefore(text, caret);
        if (at < 0)
        {
            return null;
        }

        string phrase = text[(at + 1)..caret];
        AgendaPhrase? reading = AgendaPhraseParser.Parse(phrase, now);

        // Typed something that is not a date: the popup goes away and the text is just text.
        if (reading is null && phrase.Trim().Length > 0)
        {
            return null;
        }

        return new AgendaCaptureState(at, caret, TitleBefore(text, at), phrase, reading);
    }

    /// <summary>
    /// The item Enter creates. <paramref name="kind"/> is whichever of the two readback lines is
    /// selected; they describe the same reading, so switching is only ever a different shape.
    /// </summary>
    public static AgendaItem Compose(
        AgendaCaptureState state,
        AgendaKind kind,
        Guid noteId,
        Guid itemId,
        DateTimeOffset now)
    {
        if (state.Reading is not { } reading)
        {
            throw new InvalidOperationException("Nothing has been read yet.");
        }

        bool repeats = reading.Rrule is not null;

        return new AgendaItem(
            itemId,
            // §7: choosing a list is never required to capture something.
            AgendaList.DefaultId,
            kind,
            state.Title,
            string.Empty,
            AgendaZone.Local(),
            // A repeating to-do anchors on DTSTART, the way a VTODO with an RRULE does; a one-off
            // one carries only DUE, so the day panel finds it on the day it is owed rather than
            // the day it was typed.
            StartsAt: kind == AgendaKind.Event || repeats ? reading.At : null,
            EndsAt: kind == AgendaKind.Event ? AgendaPhraseParser.EventEnd(reading) : null,
            DueAt: kind == AgendaKind.Task && !repeats ? reading.At : null,
            HasDueTime: kind == AgendaKind.Task && !repeats && reading.HasTime,
            reading.Rrule,
            SeriesId: null,
            RecurrenceId: null,
            AgendaStatus.NeedsAction,
            CompletedUtc: null,
            Priority: 0,
            TimelineVisibility.Auto,
            // The one link back, and one-way. Allowed to dangle: deleting the note later must not
            // take the task with it.
            SourceNoteId: noteId,
            ExceptionDates: [],
            // Mobile §06: a dated item starts with one alert at its own time, which the user can
            // remove. The @ command always supplies a date, so a to-do made this way always has
            // one. An event does not: a block of time is not something to be nagged about unless
            // the user asks for it.
            AlarmLeadMinutes: kind == AgendaKind.Task ? AgendaAlert.Default : AgendaAlert.None,
            now,
            now);
    }

    /// <summary>
    /// The nearest <c>@</c> before the caret that could have opened a popup, or -1.
    /// </summary>
    /// <remarks>
    /// Only at the start of a word, which is what keeps an email address out of it: the <c>@</c>
    /// of "jiwon@aegisep.com" has a letter in front of it and is never a trigger. A newline counts
    /// as whitespace, so the first character of a line qualifies.
    /// </remarks>
    private static int LastTriggerBefore(string text, int caret)
    {
        for (int index = caret - 1; index >= 0; index -= 1)
        {
            if (text[index] == '\n')
            {
                // A phrase does not span lines: pressing Enter in the body ends the thought.
                return -1;
            }

            if (text[index] == '@' && (index == 0 || char.IsWhiteSpace(text[index - 1])))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The line to the left of the trigger, trimmed. Empty is allowed — the user may fill it in after.</summary>
    private static string TitleBefore(string text, int at)
    {
        int start = text.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1;
        return at <= start ? string.Empty : text[start..at].Trim();
    }
}
