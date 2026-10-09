import Foundation

/// What the watch reads back after a dictation, before anything is made (Apple Watch design §03).
///
/// The phone's `@` bar says "10/7 (수) 오후 5:00 마감"; the watch says "오늘 오후 5:00 마감". A
/// wrist has less room and the sentence is being glanced at, so the day is a word when it can be
/// one — 오늘, 내일 — and a date only beyond that. The reading underneath is the same parser's.
public struct CaptureReadback: Equatable, Sendable {
    /// What was said in front of the date, or the whole sentence when there is no date.
    public var title: String
    /// The 할 일 line, or nil when no date was found and only "노트에 한 줄" is offered.
    public var task: String?
    /// The 일정 line, nil likewise.
    public var event: String?
    /// True when a bare time had passed and was read as tomorrow — A8, the reading worth a second look.
    public var rolledToTomorrow: Bool
    /// The day the item would land on, for "10/8에 추가됨" when it is not today.
    public var day: LocalDay?
    /// `HH:mm`, for the "17:00 · 방금" row the watch adds at once.
    public var time: String?

    public var hasDate: Bool { task != nil }

    public static func read(_ sentence: String, now: LocalMoment, text: GlanceText) -> CaptureReadback {
        let spoken = sentence.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let (title, phrase) = AgendaPhraseParser.parseTrailing(spoken, now: now) else {
            return CaptureReadback(title: spoken, task: nil, event: nil, rolledToTomorrow: false, day: nil, time: nil)
        }

        return CaptureReadback(
            title: title,
            task: taskLine(phrase, now: now, text: text),
            event: eventLine(phrase, now: now, text: text),
            rolledToTomorrow: phrase.rolledToTomorrow,
            day: phrase.at.day,
            time: phrase.hasTime ? String(format: "%02d:%02d", phrase.at.hour, phrase.at.minute) : nil)
    }

    // "오늘 오후 5:00 마감" / "Due today, 5:00 PM"; rolled: "내일 10/8 오전 5:00" / "Tomorrow, Oct 8, 5:00 AM".
    static func taskLine(_ phrase: AgendaPhrase, now: LocalMoment, text: GlanceText) -> String {
        let time = phrase.hasTime ? text.clock12(phrase.at.hour, phrase.at.minute) : nil
        if phrase.rrule != nil {
            let every = every(phrase, text: text)
            guard let time else { return every }
            return text.english ? "\(every) at \(time)" : "\(every) \(time)"
        }

        if phrase.rolledToTomorrow, let time {
            return text.english
                ? "Tomorrow, \(text.shortDate(phrase.at.day)), \(time)"
                : "내일 \(text.shortDate(phrase.at.day)) \(time)"
        }

        let day = dayWord(phrase.at.day, now: now, text: text)
        if text.english {
            return time.map { "Due \(day), \($0)" } ?? "Due \(day)"
        }
        return time.map { "\(day) \($0) 마감" } ?? "\(day) 마감"
    }

    // "오늘 오후 5–6시" / "Today 5–6 PM"; all day: "내일 · 하루 종일" / "Tomorrow · All day".
    static func eventLine(_ phrase: AgendaPhrase, now: LocalMoment, text: GlanceText) -> String {
        let lead: String
        if phrase.rrule != nil {
            lead = every(phrase, text: text)
        } else {
            let day = dayWord(phrase.at.day, now: now, text: text)
            lead = text.english ? day.prefix(1).uppercased() + day.dropFirst() : day
        }

        guard phrase.hasTime, let end = AgendaPhraseParser.eventEnd(phrase) else {
            return phrase.rrule != nil ? lead : text.english ? "\(lead) · All day" : "\(lead) · 하루 종일"
        }

        return "\(lead) \(range(phrase.at, end, text: text))"
    }

    /// "오후 5–6시" / "5–6 PM" when both ends are on the hour and in the same half of the day;
    /// otherwise both ends in full, as the phone's readback does across noon.
    static func range(_ start: LocalMoment, _ end: LocalMoment, text: GlanceText) -> String {
        let sameHalf = (start.hour < 12) == (end.hour < 12)
        let onTheHour = start.minute == 0 && end.minute == 0
        func h(_ hour: Int) -> Int { hour % 12 == 0 ? 12 : hour % 12 }
        if sameHalf && onTheHour {
            let half = start.hour < 12 ? (text.english ? "AM" : "오전") : (text.english ? "PM" : "오후")
            return text.english ? "\(h(start.hour))–\(h(end.hour)) \(half)" : "\(half) \(h(start.hour))–\(h(end.hour))시"
        }
        return "\(text.clock12(start.hour, start.minute))–\(text.clock12(end.hour, end.minute))"
    }

    /// 오늘 / 내일 / "10/9 (금)"; "today" / "tomorrow" / "Fri, Oct 9".
    static func dayWord(_ day: LocalDay, now: LocalMoment, text: GlanceText) -> String {
        if day == now.day { return text.english ? "today" : "오늘" }
        if day == now.day.adding(days: 1, calendar: utc) { return text.english ? "tomorrow" : "내일" }
        return text.english
            ? "\(GlanceText.weekdaysShort[day.weekday]), \(text.shortDate(day))"
            : "\(text.shortDate(day)) (\(GlanceText.weekdaysKo[day.weekday]))"
    }

    /// "매일" / "매주 월요일"; "Every day" / "Every Mon".
    static func every(_ phrase: AgendaPhrase, text: GlanceText) -> String {
        // A weekly rule with no day named repeats on the day it was said, which is the day it lands on.
        guard phrase.rrule?.hasPrefix("FREQ=WEEKLY") == true else { return text.english ? "Every day" : "매일" }
        let weekday = phrase.at.day.weekday
        return text.english ? "Every \(GlanceText.weekdaysShort[weekday])" : "매주 \(GlanceText.weekdaysKo[weekday])요일"
    }

    private static let utc: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar
    }()
}
