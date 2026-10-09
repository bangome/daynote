using System.Globalization;

namespace Daynote.Core.Agenda;

/// <summary>
/// What the text after <c>@</c> was understood to mean (docs/TODOS.md §7).
/// </summary>
/// <param name="At">
/// The resolved wall clock. For a rule this is the next occurrence, which is what the popup reads
/// back as "다음 10/12".
/// </param>
/// <param name="HasTime">
/// False when only a day was given. A to-do is then due on that date with no time, and an event is
/// all day — the two readback lines the popup shows side by side.
/// </param>
/// <param name="Rrule">The recurrence, as RRULE, or null for a one-off.</param>
/// <param name="RolledToTomorrow">
/// True when a bare time had already passed today and was read as tomorrow. The popup says so out
/// loud, because it is the one reading a user is most likely to disagree with.
/// </param>
/// <param name="Length">
/// How many characters after the <c>@</c> were consumed. The editor turns exactly that span into a
/// chip once the item is created (§7, "what stays in the body is exactly what was typed" — the chip
/// is a rendering of it, not a rewrite).
/// </param>
public readonly record struct AgendaPhrase(
    WallClock At,
    bool HasTime,
    string? Rrule,
    bool RolledToTomorrow,
    int Length);

/// <summary>
/// The natural-language half of the <c>@</c> command: date, time and recurrence only
/// (docs/TODOS.md §7).
/// </summary>
/// <remarks>
/// <b>Both languages at once, deliberately.</b> A Korean UI does not stop someone typing
/// <c>tomorrow</c>, and the vocabulary is small enough that recognising both costs nothing. The
/// alternative — parsing in the UI language — fails the bilingual user in the one place where
/// failing means the popup silently does not appear.
/// <para>
/// <b>Nothing here is a title.</b> §7 is explicit that the title comes from the line to the left of
/// the <c>@</c>, so anything this cannot read is left alone rather than swept into a name.
/// </para>
/// <para>
/// <b>The longest readable prefix wins, and trailing text is ignored.</b> Requiring the whole
/// remainder to parse would make the popup flicker away on every half-typed word — "내일 오" is not
/// a date and "내일 오전 9시" is. What the first token cannot start, though, is no match at all:
/// that is what keeps "자료는 @지원 님께" an ordinary sentence with no popup.
/// </para>
/// </remarks>
public static class AgendaPhraseParser
{
    /// <summary>Where an event ends when only a start was given. One hour, as the popup reads back.</summary>
    public static readonly TimeSpan DefaultEventLength = TimeSpan.FromHours(1);

    /// <summary>
    /// Reads <paramref name="text"/> — everything after the <c>@</c> — against <paramref name="now"/>.
    /// </summary>
    public static AgendaPhrase? Parse(string? text, DateTime now)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var scan = new Scanner(text);
        string? rrule = ReadRecurrence(ref scan, out DayOfWeek? ruleDay);
        DateOnly? date = rrule is null ? ReadDate(ref scan, now) : null;
        TimeOnly? time = ReadTime(ref scan);

        if (rrule is null && date is null && time is null)
        {
            return null;
        }

        bool rolled = false;
        DateOnly day;

        if (rrule is not null)
        {
            // A rule has no date of its own; what the popup shows is the next time it fires.
            day = NextOccurrence(now, ruleDay, time);
            if (ruleDay is { } weekday)
            {
                rrule += ";BYDAY=" + IcalDay(weekday);
            }
        }
        else if (date is { } given)
        {
            day = given;
        }
        else
        {
            // A bare time. Today if it is still ahead, otherwise tomorrow — and say so, because
            // "@9시" typed at half past two is the reading most worth disagreeing with.
            DateOnly today = DateOnly.FromDateTime(now);
            day = today;
            if (time is { } at && today.ToDateTime(at) <= now)
            {
                day = today.AddDays(1);
                rolled = true;
            }
        }

        return new AgendaPhrase(
            new WallClock(day.ToDateTime(time ?? TimeOnly.MinValue)),
            time is not null,
            rrule,
            rolled,
            scan.Consumed);
    }

    /// <summary>
    /// When an event made from <paramref name="phrase"/> ends: an hour after it starts, or nothing
    /// at all when only a day was given, which is an all-day event.
    /// </summary>
    public static WallClock? EventEnd(AgendaPhrase phrase) => phrase.HasTime
        ? new WallClock(phrase.At.Value + DefaultEventLength)
        : null;

    /// <summary>
    /// Reads a date off the end of a sentence that has no <c>@</c> in it, and answers with what is
    /// left in front of it as the title — the watch's dictation (Apple Watch design §03).
    /// </summary>
    /// <remarks>
    /// Nobody can type <c>@</c> into a dictation, so the phrase has to be found rather than
    /// marked. It is the <b>longest</b> run of whole words at the end that reads completely, which
    /// is the same reading <see cref="Parse"/> gives when the same words follow an <c>@</c>: "회의자료
    /// 초안 공유 오늘 5시" is the title "회의자료 초안 공유" and the phrase "오늘 5시", exactly as
    /// "회의자료 초안 공유 @오늘 5시" would be in the editor.
    /// <para>
    /// Spoken sentences carry a few words a typed phrase does not: the full stop dictation adds,
    /// a Korean particle on the last word ("5시에", "내일까지"), and English's "at" between a
    /// day and a time or "on"/"by" in front of the day. Those are read past here rather than taught
    /// to the parser, so the editor's <c>@</c> keeps exactly the grammar it has.
    /// </para>
    /// <para>
    /// Null when no date was found, or when the whole sentence was a date and nothing is left to
    /// call it — the watch then offers only "노트에 한 줄".
    /// </para>
    /// </remarks>
    public static (string Title, AgendaPhrase Phrase)? ParseTrailing(string? text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string sentence = text.Trim().TrimEnd('.', '!', '?', '。').TrimEnd();
        foreach (string particle in TrailingParticles)
        {
            // Only stuck to the word before it: on its own it is a word, not a particle.
            if (sentence.Length > particle.Length && sentence.EndsWith(particle, StringComparison.Ordinal)
                && !char.IsWhiteSpace(sentence[^(particle.Length + 1)]))
            {
                sentence = sentence[..^particle.Length];
                break;
            }
        }

        // From the front, so the first reading found is the longest. One that starts at the very
        // front leaves nothing to be the title, and a shorter one would only be making one up.
        for (int start = 0; start < sentence.Length; start++)
        {
            if ((start > 0 && !char.IsWhiteSpace(sentence[start - 1])) || char.IsWhiteSpace(sentence[start]))
            {
                continue;
            }

            string phrase = DropConnector(sentence[start..]);
            if (Parse(phrase, now) is { } reading && reading.Length == phrase.TrimEnd().Length)
            {
                string title = TrimConnectorAtEnd(sentence[..start].TrimEnd());
                return title.Length == 0 ? null : (title, reading);
            }
        }

        return null;
    }

    /// <summary>A particle a spoken Korean sentence leaves on its last word: "5시에", "내일까지".</summary>
    private static readonly string[] TrailingParticles = ["까지", "에"];

    /// <summary>English connectors that sit inside or in front of a spoken date.</summary>
    private static readonly string[] Connectors = ["at", "on", "by"];

    /// <summary>"today at 5pm" reads as "today 5pm": the parser takes a day and then a time, with nothing between.</summary>
    private static string DropConnector(string phrase)
    {
        string[] words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 1
            ? string.Join(' ', words.Where((word, index) => index == 0 || !Connectors.Contains(word, StringComparer.OrdinalIgnoreCase)))
            : phrase;
    }

    /// <summary>"Call mom on" in front of "Friday" is the title "Call mom".</summary>
    private static string TrimConnectorAtEnd(string title)
    {
        int space = title.LastIndexOf(' ');
        return space > 0 && Connectors.Contains(title[(space + 1)..], StringComparer.OrdinalIgnoreCase)
            ? title[..space].TrimEnd()
            : title;
    }

    private static string? ReadRecurrence(ref Scanner scan, out DayOfWeek? day)
    {
        day = null;
        Scanner saved = scan;

        if (scan.Take("매일") || scan.Take("daily") || scan.TakeAll("every", "day"))
        {
            return "FREQ=DAILY";
        }

        if (scan.Take("매주") || scan.Take("weekly") || scan.Take("every"))
        {
            // "매주" and "every" alone repeat on the day the user is standing on, which the caller
            // resolves from `now`. "매주 월" names one.
            day = ReadWeekday(ref scan, allowSingleLetter: true);
            if (day is null && !scan.StartedWithWeeklyWord)
            {
                scan = saved;
                return null;
            }

            return "FREQ=WEEKLY";
        }

        scan = saved;
        return null;
    }

    private static DayOfWeek? ReadWeekday(ref Scanner scan, bool allowSingleLetter)
    {
        foreach ((string word, DayOfWeek value) in Weekdays)
        {
            if ((allowSingleLetter || word.Length > 1) && scan.Take(word))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Longest first, so "월요일" is not read as "월" with "요일" left over, and "sunday" is not
    /// read as "sun" with "day" left over — which would then match "every day".
    /// </summary>
    /// <remarks>
    /// The one-character Korean forms are only offered after a word meaning "every". On their own
    /// they are far too eager: "@일정 잡기" would become Sunday, and "@수정 필요" Wednesday. After
    /// "매주" there is nothing else they could be.
    /// </remarks>
    private static readonly (string Word, DayOfWeek Day)[] Weekdays =
    [
        ("월요일", DayOfWeek.Monday), ("화요일", DayOfWeek.Tuesday), ("수요일", DayOfWeek.Wednesday),
        ("목요일", DayOfWeek.Thursday), ("금요일", DayOfWeek.Friday), ("토요일", DayOfWeek.Saturday),
        ("일요일", DayOfWeek.Sunday),
        ("monday", DayOfWeek.Monday), ("tuesday", DayOfWeek.Tuesday), ("wednesday", DayOfWeek.Wednesday),
        ("thursday", DayOfWeek.Thursday), ("friday", DayOfWeek.Friday), ("saturday", DayOfWeek.Saturday),
        ("sunday", DayOfWeek.Sunday),
        ("mon", DayOfWeek.Monday), ("tue", DayOfWeek.Tuesday), ("wed", DayOfWeek.Wednesday),
        ("thu", DayOfWeek.Thursday), ("fri", DayOfWeek.Friday), ("sat", DayOfWeek.Saturday),
        ("sun", DayOfWeek.Sunday),
        ("월", DayOfWeek.Monday), ("화", DayOfWeek.Tuesday), ("수", DayOfWeek.Wednesday),
        ("목", DayOfWeek.Thursday), ("금", DayOfWeek.Friday), ("토", DayOfWeek.Saturday),
        ("일", DayOfWeek.Sunday),
    ];

    private static DateOnly? ReadDate(ref Scanner scan, DateTime now)
    {
        DateOnly today = DateOnly.FromDateTime(now);

        if (scan.Take("오늘") || scan.Take("today"))
        {
            return today;
        }

        if (scan.Take("내일") || scan.Take("tomorrow"))
        {
            return today.AddDays(1);
        }

        if (scan.Take("모레"))
        {
            return today.AddDays(2);
        }

        Scanner saved = scan;
        if (scan.TakeDate(out int month, out int dayOfMonth))
        {
            // No year is ever typed, so it is this one unless that is already past — "@1/3" in
            // December means January.
            int year = now.Year;
            if (month < 1 || month > 12 || dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(year, month))
            {
                scan = saved;
                return null;
            }

            var candidate = new DateOnly(year, month, dayOfMonth);
            return candidate < today ? candidate.AddYears(1) : candidate;
        }

        if (ReadWeekday(ref scan, allowSingleLetter: false) is { } weekday)
        {
            // Strictly ahead: "금요일" typed on a Friday means the next one, because a day that has
            // already started is not what someone is scheduling.
            int ahead = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
            return today.AddDays(ahead == 0 ? 7 : ahead);
        }

        return null;
    }

    private static TimeOnly? ReadTime(ref Scanner scan) => scan.TakeTime();

    private static DateOnly NextOccurrence(DateTime now, DayOfWeek? ruleDay, TimeOnly? time)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        if (ruleDay is not { } weekday)
        {
            // Daily, or weekly with no day named: today if the time is still ahead, else tomorrow
            // (for a weekly rule with no day, a week from the day it was typed).
            bool passed = time is { } at && today.ToDateTime(at) <= now;
            return passed ? today.AddDays(1) : today;
        }

        int ahead = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
        if (ahead == 0 && time is { } hour && today.ToDateTime(hour) <= now)
        {
            ahead = 7;
        }

        return today.AddDays(ahead);
    }

    private static string IcalDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU",
    };

    /// <summary>
    /// A cursor over the phrase. A struct so a failed attempt is undone by assigning a saved copy
    /// back, which keeps the backtracking visible instead of hidden in an index nobody remembers to
    /// restore.
    /// </summary>
    private struct Scanner(string text)
    {
        private readonly string text = text;
        private int index;

        /// <summary>How far in the phrase was understood, trailing whitespace trimmed off.</summary>
        internal readonly int Consumed => index;

        /// <summary>True once a word meaning "every week" has been taken, named or not.</summary>
        internal bool StartedWithWeeklyWord { get; private set; }

        internal bool Take(string word)
        {
            SkipSpace();
            if (index + word.Length > text.Length
                || !text.AsSpan(index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // A word made of letters has to end where it says it does, or "everyone" is a weekly
            // rule and "sunrise" is Sunday. Korean has no such boundary to test, which is why the
            // one-character weekdays are gated on a preceding "매주" instead.
            if (char.IsAsciiLetter(word[^1])
                && index + word.Length < text.Length
                && char.IsAsciiLetterOrDigit(text[index + word.Length]))
            {
                return false;
            }

            index += word.Length;
            if (word is "매주" or "weekly" or "every")
            {
                StartedWithWeeklyWord = true;
            }

            return true;
        }

        internal bool TakeAll(params string[] words)
        {
            Scanner saved = this;
            foreach (string word in words)
            {
                if (!Take(word))
                {
                    this = saved;
                    return false;
                }
            }

            return true;
        }

        /// <summary>"10/8", "10.8" or "10월 8일".</summary>
        internal bool TakeDate(out int month, out int day)
        {
            month = 0;
            day = 0;
            Scanner saved = this;
            SkipSpace();

            if (!TakeNumber(out month))
            {
                this = saved;
                return false;
            }

            bool korean = Take("월");
            if (!korean && !Take("/"))
            {
                this = saved;
                return false;
            }

            if (!TakeNumber(out day))
            {
                this = saved;
                return false;
            }

            if (korean)
            {
                Take("일");
            }

            return true;
        }

        /// <summary>"9시", "9시 30분", "오후 3시", "3pm", "14:30", "9:00".</summary>
        internal TimeOnly? TakeTime()
        {
            Scanner saved = this;
            SkipSpace();

            // The meridiem can come before the number in Korean and after it in English.
            bool? pm = TakeMeridiemBefore();

            if (!TakeNumber(out int hour))
            {
                this = saved;
                return null;
            }

            int minute = 0;
            if (Take(":"))
            {
                if (!TakeNumber(out minute))
                {
                    this = saved;
                    return null;
                }
            }
            else if (Take("시"))
            {
                if (TakeNumber(out int korean))
                {
                    minute = korean;
                    Take("분");
                }
            }

            pm ??= TakeMeridiemAfter();

            if (pm is null && hour is >= 1 and <= 6)
            {
                // Nobody schedules a meeting for three in the morning, so "3시" and "3:30" are
                // afternoon unless the user said otherwise. Seven and later are left alone, which
                // is where the guess stops being obvious — and is exactly why the popup reads the
                // result back as a sentence before Enter.
                hour += 12;
            }
            else if (pm is true && hour < 12)
            {
                hour += 12;
            }
            else if (pm is false && hour == 12)
            {
                hour = 0;
            }

            if (hour > 23 || minute > 59)
            {
                this = saved;
                return null;
            }

            return new TimeOnly(hour, minute);
        }

        private bool? TakeMeridiemBefore()
        {
            if (Take("오전"))
            {
                return false;
            }

            return Take("오후") ? true : null;
        }

        private bool? TakeMeridiemAfter()
        {
            if (Take("am"))
            {
                return false;
            }

            return Take("pm") ? true : null;
        }

        private bool TakeNumber(out int value)
        {
            value = 0;
            SkipSpace();
            int start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index += 1;
            }

            return index > start
                && int.TryParse(text.AsSpan(start, index - start), CultureInfo.InvariantCulture, out value);
        }

        /// <remarks>
        /// A loop rather than a regex. <c>^\s*</c> matched against a start position does not do
        /// this: <c>^</c> anchors to the beginning of the string, not to where the scan is, so it
        /// silently skipped nothing and every phrase stopped at its first space.
        /// </remarks>
        private void SkipSpace()
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index += 1;
            }
        }
    }
}
