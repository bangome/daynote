using System.Globalization;
using Daynote.App.Localization;
using Daynote.Core.Agenda;

namespace Daynote.App.Notes;

/// <summary>How much room the readback has.</summary>
/// <remarks>
/// The phone's bar sits in the keyboard accessory slot and shortens the same sentences to fit
/// (docs/design-renewal/Daynote Mobile B Tasks - Events.dc.html §01). Same readings, fewer
/// characters — never a different answer, which is the thing that would make the two shells
/// disagree in front of the same user.
/// </remarks>
public enum ReadbackWidth
{
    /// <summary>The desktop popup: "10월 9일 (금) 오후 3:00 마감".</summary>
    Full,

    /// <summary>The phone bar: "10/9 (금) 오후 3:00 마감".</summary>
    Compact,
}

/// <summary>
/// The two lines the <c>@</c> popup reads back, and the note under them.
/// </summary>
/// <param name="Task">The 할 일 line.</param>
/// <param name="Event">The 일정 line.</param>
/// <param name="Note">
/// The sentence under both, or empty. Only ever says something the user is likely to disagree
/// with — a time read as tomorrow, or a repeat that will not remind yet.
/// </param>
public readonly record struct AgendaReadbackLines(string Task, string Event, string Note);

/// <summary>
/// Turns a parsed <c>@</c> phrase into the sentences the popup shows (docs/TODOS.md §7).
/// </summary>
/// <remarks>
/// Separate from <see cref="AgendaPhraseParser"/> and one layer up, because reading is a fact
/// about the text and saying it back is a fact about the language it is read in. The parser takes
/// both languages at once; this one speaks whichever the UI is set to.
/// <para>
/// <b>Why a sentence and not fields.</b> §7 asks for a readback so a misreading is visible before
/// Enter, and the parser does guess — a bare "3시" is three in the afternoon. A row of filled-in
/// date and time boxes reads as confirmation; a sentence reads as a claim you can disagree with.
/// </para>
/// <para>
/// <b>Every piece of punctuation and ordering lives in the format strings.</b> Korean joins a date
/// to a time with a space and puts the meridiem first — "10월 9일 (금) 오후 3:00–4:00"; English
/// joins with "·" and puts it last — "Fri, Oct 9 · 3:00–4:00 PM". Composing those in code would
/// mean a switch on the language in six places, so instead each format is handed every part it
/// could want and uses the ones its language needs.
/// </para>
/// </remarks>
public static class AgendaReadback
{
    /// <summary>
    /// What the popup shows before anything has been typed after the <c>@</c>: an invitation and
    /// four examples, not an error.
    /// </summary>
    public static string EmptyPrompt(ReadbackWidth width) => width == ReadbackWidth.Compact
        ? AppStrings.AgendaPhraseEmptyPickable
        : AppStrings.AgendaPhraseEmpty;

    /// <summary>The examples beside that prompt. One of each kind the parser understands.</summary>
    public static IReadOnlyList<string> Examples =>
    [
        AppStrings.AgendaPhraseExampleToday,
        AppStrings.AgendaPhraseExampleTomorrow,
        AppStrings.AgendaPhraseExampleWeekdayTime,
        AppStrings.AgendaPhraseExampleRepeat,
    ];

    public static AgendaReadbackLines Describe(AgendaPhrase phrase, ReadbackWidth width) => new(
        TaskLine(phrase, width),
        EventLine(phrase, width),
        NoteLine(phrase, width));

    private static string TaskLine(AgendaPhrase phrase, ReadbackWidth width)
    {
        if (phrase.Rrule is not null)
        {
            // No "due" on a rule: a repeating to-do has a next occurrence, not a deadline.
            return phrase.HasTime
                ? Format(AppStrings.AgendaReadbackAtTime, Every(phrase, width), Time(phrase))
                : Every(phrase, width);
        }

        string when = phrase.HasTime
            ? Format(AppStrings.AgendaReadbackDateAtTime, Prefixed(phrase, width), Time(phrase))
            : Prefixed(phrase, width);

        return Format(AppStrings.AgendaReadbackDue, when);
    }

    private static string EventLine(AgendaPhrase phrase, ReadbackWidth width)
    {
        if (phrase.Rrule is not null)
        {
            return phrase.HasTime
                ? Format(AppStrings.AgendaReadbackDateAndRange, Every(phrase, width), Range(phrase, width))
                : Every(phrase, width);
        }

        return phrase.HasTime
            ? Format(AppStrings.AgendaReadbackDateAndRange, Prefixed(phrase, width), Range(phrase, width))
            : Format(AppStrings.AgendaReadbackAllDay, Prefixed(phrase, width));
    }

    /// <summary>
    /// The one thing worth saying out loud under the two lines. A time that had already passed was
    /// read as tomorrow, and a repeating to-do does not remind yet — both are the user's business
    /// before Enter rather than after.
    /// </summary>
    private static string NoteLine(AgendaPhrase phrase, ReadbackWidth width)
    {
        if (phrase.RolledToTomorrow)
        {
            return Format(
                width == ReadbackWidth.Compact
                    ? AppStrings.AgendaReadbackRolledNoteShort
                    : AppStrings.AgendaReadbackRolledNote,
                At(phrase.At.Value, AppStrings.AgendaReadbackHour),
                // What to type instead. Korean counts to 24 ("21시") and English says pm ("9pm"),
                // which is a pattern over the same instant rather than a different number.
                At(phrase.At.Value.AddHours(12), AppStrings.AgendaReadbackEveningHint));
        }

        // Not built: expanding RRULE into occurrences (docs/TODOS.md §12 step 4, §13). Saying so
        // here costs one line and is cheaper than a user discovering it by missing something.
        return phrase.Rrule is not null ? AppStrings.AgendaReadbackRepeatNoAlert : string.Empty;
    }

    /// <summary>"매주 월요일" / "Every Mon", or the daily form.</summary>
    private static string Every(AgendaPhrase phrase, ReadbackWidth width)
    {
        if (phrase.Rrule?.Contains("BYDAY=", StringComparison.Ordinal) != true)
        {
            return AppStrings.AgendaReadbackEveryDay;
        }

        _ = width;

        // Korean spells the weekday out and English abbreviates it, at either width — a habit of
        // the language, not a shortage of room, so the pattern is the translated part.
        return Format(
            AppStrings.AgendaReadbackEveryWeekday,
            phrase.At.Value.ToString(AppStrings.AgendaReadbackWeekday, CultureInfo.CurrentCulture));
    }

    /// <summary>The date, with "내일 " in front when the reading rolled onto it.</summary>
    private static string Prefixed(AgendaPhrase phrase, ReadbackWidth width)
    {
        // "Tomorrow, Thu, Oct 8" has one comma too many, so English drops the date's own when
        // something is already in front of it. Korean punctuates the date the same either way.
        string pattern = width == ReadbackWidth.Compact
            ? AppStrings.AgendaReadbackDateShort
            : phrase.RolledToTomorrow
                ? AppStrings.AgendaReadbackDateLongPrefixed
                : AppStrings.AgendaReadbackDateLong;

        string date = phrase.At.Value.ToString(pattern, CultureInfo.CurrentCulture);

        // The surprise belongs in the sentence, not only in the note under it: the eye reads the
        // bold line first and might never reach the explanation.
        return phrase.RolledToTomorrow
            ? Format(AppStrings.AgendaReadbackTomorrowPrefix, date)
            : date;
    }

    private static string Time(AgendaPhrase phrase) => TimeFull(phrase.At.Value);

    /// <summary>
    /// "오후 3:00–4:00" / "3:00–4:00 PM". The meridiem is said once, on whichever side the language
    /// puts it — unless the hour straddles noon, where saying it once would be a lie.
    /// </summary>
    private static string Range(AgendaPhrase phrase, ReadbackWidth width)
    {
        DateTime start = phrase.At.Value;
        DateTime end = start + AgendaPhraseParser.DefaultEventLength;

        bool sameHalf = (start.Hour < 12) == (end.Hour < 12);
        if (!sameHalf)
        {
            return Format(AppStrings.AgendaReadbackRange, TimeFull(start), TimeFull(end));
        }

        // Each language says how it writes the two ends, so one join covers both: Korean puts the
        // meridiem on the start, English on the end, and the arguments stay {0} and {1} in both.
        return width == ReadbackWidth.Compact
            ? Format(
                AppStrings.AgendaReadbackRangeShort,
                At(start, AppStrings.AgendaReadbackRangeStartHour),
                At(end, AppStrings.AgendaReadbackRangeEndHour))
            : Format(
                AppStrings.AgendaReadbackRange,
                At(start, AppStrings.AgendaReadbackRangeStart),
                At(end, AppStrings.AgendaReadbackRangeEnd));
    }

    private static string TimeFull(DateTime at) => At(at, AppStrings.AgendaReadbackTime);

    private static string At(DateTime at, string pattern) =>
        at.ToString(pattern, CultureInfo.CurrentCulture);

    private static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
