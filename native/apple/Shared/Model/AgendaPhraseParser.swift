import Foundation

/// What a date phrase was understood to mean. Mirrors `Daynote.Core.Agenda.AgendaPhrase`.
public struct AgendaPhrase: Equatable, Sendable {
    /// The resolved wall clock; for a rule, its next occurrence.
    public var at: LocalMoment
    /// False when only a day was given.
    public var hasTime: Bool
    /// The recurrence as RRULE, or nil.
    public var rrule: String?
    /// A bare time that had passed today and was read as tomorrow.
    public var rolledToTomorrow: Bool
    /// UTF-16 units consumed, as .NET counts a string's length.
    public var length: Int
}

/// The watch's copy of the app's `@` parser (`AgendaPhraseParser.cs`), for the readback only.
///
/// The watch has to read a dictated sentence back before the user picks what it becomes, and
/// cannot wait on the phone for that — so the reading is done here, and the phone reads the same
/// text again with its own parser when it makes the item (docs/APPLE_EXTENSIONS.md §5). The two are
/// held to one table, `tests/fixtures/agenda-phrase-vectors.json`, which the C# tests write and
/// both test suites read. A change to the grammar is made in C# first, the table rewritten, and
/// this file brought back into line until the Swift tests pass again.
///
/// Written over UTF-16 code units rather than `Character`s so that every index and length agrees
/// with .NET's `string`, character for character.
public enum AgendaPhraseParser {
    /// Where an event ends when only a start was given.
    public static let defaultEventMinutes = 60

    public static func parse(_ text: String?, now: LocalMoment) -> AgendaPhrase? {
        guard let text, !text.isEmpty else { return nil }
        var scan = Scanner(Array(text.utf16))
        var ruleDay: Int?
        var rrule = readRecurrence(&scan, day: &ruleDay)
        let date = rrule == nil ? readDate(&scan, now: now) : nil
        let time = scan.takeTime()

        if rrule == nil && date == nil && time == nil { return nil }

        var rolled = false
        let day: LocalDay
        if let rule = rrule {
            day = nextOccurrence(now: now, ruleDay: ruleDay, time: time)
            if let weekday = ruleDay { rrule = rule + ";BYDAY=" + icalDay(weekday) }
        } else if let given = date {
            day = given
        } else {
            var today = now.day
            if let at = time, (at.0 * 60 + at.1) <= now.minutesOfDay {
                today = today.adding(days: 1, calendar: utc)
                rolled = true
            }
            day = today
        }

        return AgendaPhrase(
            at: LocalMoment(day: day, hour: time?.0 ?? 0, minute: time?.1 ?? 0),
            hasTime: time != nil,
            rrule: rrule,
            rolledToTomorrow: rolled,
            length: scan.index)
    }

    /// When an event made from `phrase` ends: an hour on, or nil (all day) for a bare day.
    public static func eventEnd(_ phrase: AgendaPhrase) -> LocalMoment? {
        guard phrase.hasTime else { return nil }
        let date = phrase.at.date(in: utc).addingTimeInterval(TimeInterval(defaultEventMinutes * 60))
        return LocalMoment(date, calendar: utc)
    }

    /// A date read off the end of a spoken sentence, and the rest as its title.
    /// Mirrors `AgendaPhraseParser.ParseTrailing`.
    public static func parseTrailing(_ text: String?, now: LocalMoment) -> (title: String, phrase: AgendaPhrase)? {
        guard let text else { return nil }
        var sentence = trim(Array(text.utf16))
        while let last = sentence.last, [0x2E, 0x21, 0x3F, 0x3002].contains(last) { sentence.removeLast() }
        sentence = trimEnd(sentence)
        if sentence.isEmpty { return nil }

        for particle in trailingParticles where sentence.count > particle.count
            && sentence.suffix(particle.count).elementsEqual(particle)
            && !isWhite(sentence[sentence.count - particle.count - 1]) {
            sentence.removeLast(particle.count)
            break
        }

        for start in 0..<sentence.count {
            if (start > 0 && !isWhite(sentence[start - 1])) || isWhite(sentence[start]) { continue }
            let phrase = dropConnector(Array(sentence[start...]))
            let phraseText = String(decoding: phrase, as: UTF16.self)
            if let reading = parse(phraseText, now: now), reading.length == trimEnd(phrase).count,
               !phrase.allSatisfy({ (0x30...0x39).contains($0) || isWhite($0) }) {
                let title = trimConnectorAtEnd(trimEnd(Array(sentence[..<start])))
                return title.isEmpty ? nil : (String(decoding: title, as: UTF16.self), reading)
            }
        }

        return nil
    }

    // MARK: - Grammar

    // A bare trailing number ("아이폰 15", "Review PR 12") is not a time when spoken: see
    // `IsBareNumber` in the C# parser, which this mirrors in `parseTrailing` above.

    private static let utc: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar
    }()

    private static let trailingParticles: [[UInt16]] = ["까지", "에"].map { Array($0.utf16) }

    private static let connectors = ["at", "on", "by"]

    /// 0 = Sunday … 6 = Saturday, longest first, as the C# table orders them.
    private static let weekdays: [(String, Int)] = [
        ("월요일", 1), ("화요일", 2), ("수요일", 3), ("목요일", 4), ("금요일", 5), ("토요일", 6), ("일요일", 0),
        ("monday", 1), ("tuesday", 2), ("wednesday", 3), ("thursday", 4), ("friday", 5), ("saturday", 6), ("sunday", 0),
        ("mon", 1), ("tue", 2), ("wed", 3), ("thu", 4), ("fri", 5), ("sat", 6), ("sun", 0),
        ("월", 1), ("화", 2), ("수", 3), ("목", 4), ("금", 5), ("토", 6), ("일", 0),
    ]

    private static func readRecurrence(_ scan: inout Scanner, day: inout Int?) -> String? {
        day = nil
        let saved = scan
        if scan.take("매일") || scan.take("daily") || scan.takeAll(["every", "day"]) { return "FREQ=DAILY" }
        if scan.take("매주") || scan.take("weekly") || scan.take("every") {
            day = readWeekday(&scan, allowSingleLetter: true)
            if day == nil && !scan.startedWithWeeklyWord {
                scan = saved
                return nil
            }
            return "FREQ=WEEKLY"
        }
        scan = saved
        return nil
    }

    private static func readWeekday(_ scan: inout Scanner, allowSingleLetter: Bool) -> Int? {
        for (word, value) in weekdays where (allowSingleLetter || word.utf16.count > 1) && scan.take(word) {
            return value
        }
        return nil
    }

    private static func readDate(_ scan: inout Scanner, now: LocalMoment) -> LocalDay? {
        let today = now.day
        if scan.take("오늘") || scan.take("today") { return today }
        if scan.take("내일") || scan.take("tomorrow") { return today.adding(days: 1, calendar: utc) }
        if scan.take("모레") { return today.adding(days: 2, calendar: utc) }

        let saved = scan
        if let (month, dayOfMonth) = scan.takeDate() {
            let year = today.year
            guard (1...12).contains(month), dayOfMonth >= 1, dayOfMonth <= daysIn(month: month, year: year) else {
                scan = saved
                return nil
            }
            let candidate = LocalDay(year: year, month: month, day: dayOfMonth)
            if candidate < today {
                // Next year's, as .NET's AddYears does: 29 February becomes the 28th.
                let next = year + 1
                return LocalDay(year: next, month: month, day: min(dayOfMonth, daysIn(month: month, year: next)))
            }
            return candidate
        }

        if let weekday = readWeekday(&scan, allowSingleLetter: false) {
            let ahead = (weekday - today.weekday + 7) % 7
            return today.adding(days: ahead == 0 ? 7 : ahead, calendar: utc)
        }
        return nil
    }

    private static func nextOccurrence(now: LocalMoment, ruleDay: Int?, time: (Int, Int)?) -> LocalDay {
        let today = now.day
        guard let weekday = ruleDay else {
            let passed = time.map { $0.0 * 60 + $0.1 <= now.minutesOfDay } ?? false
            return passed ? today.adding(days: 1, calendar: utc) : today
        }
        var ahead = (weekday - today.weekday + 7) % 7
        if ahead == 0, let at = time, at.0 * 60 + at.1 <= now.minutesOfDay { ahead = 7 }
        return today.adding(days: ahead, calendar: utc)
    }

    private static func icalDay(_ day: Int) -> String {
        ["SU", "MO", "TU", "WE", "TH", "FR", "SA"][day]
    }

    private static func daysIn(month: Int, year: Int) -> Int {
        utc.range(of: .day, in: .month, for: utc.date(from: DateComponents(year: year, month: month))!)!.count
    }

    // MARK: - Spoken connectors

    private static func dropConnector(_ phrase: [UInt16]) -> [UInt16] {
        let words = phrase.split(separator: 0x20, omittingEmptySubsequences: true).map(Array.init)
        guard words.count > 1 else { return phrase }
        let kept = words.enumerated().filter { index, word in index == 0 || !isConnector(word) }.map(\.element)
        return Array(kept.joined(separator: [0x20]))
    }

    private static func trimConnectorAtEnd(_ title: [UInt16]) -> [UInt16] {
        guard let space = title.lastIndex(of: 0x20), space > 0, isConnector(Array(title[(space + 1)...])) else { return title }
        return trimEnd(Array(title[..<space]))
    }

    private static func isConnector(_ word: [UInt16]) -> Bool {
        connectors.contains { Array($0.utf16) == word.map(lowerAscii) }
    }

    // MARK: - UTF-16 helpers matching .NET

    /// `char.IsWhiteSpace` for the code units a phrase can contain.
    static func isWhite(_ unit: UInt16) -> Bool {
        switch unit {
        case 0x09...0x0D, 0x20, 0x85, 0xA0, 0x1680, 0x2000...0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000: true
        default: false
        }
    }

    static func lowerAscii(_ unit: UInt16) -> UInt16 {
        (0x41...0x5A).contains(unit) ? unit + 0x20 : unit
    }

    private static func trim(_ units: [UInt16]) -> [UInt16] {
        trimEnd(Array(units.drop(while: isWhite)))
    }

    private static func trimEnd(_ units: [UInt16]) -> [UInt16] {
        var result = units
        while let last = result.last, isWhite(last) { result.removeLast() }
        return result
    }

    /// The cursor, a value type so a failed attempt is undone by assigning a saved copy back.
    private struct Scanner {
        let text: [UInt16]
        var index = 0
        var startedWithWeeklyWord = false

        init(_ text: [UInt16]) { self.text = text }

        mutating func take(_ word: String) -> Bool {
            skipSpace()
            let units = Array(word.utf16)
            guard index + units.count <= text.count,
                  zip(text[index..<(index + units.count)], units).allSatisfy({ lowerAscii($0) == lowerAscii($1) })
            else { return false }

            if let last = units.last, isAsciiLetter(last), index + units.count < text.count,
               isAsciiLetterOrDigit(text[index + units.count]) {
                return false
            }

            index += units.count
            if ["매주", "weekly", "every"].contains(word) { startedWithWeeklyWord = true }
            return true
        }

        mutating func takeAll(_ words: [String]) -> Bool {
            let saved = self
            for word in words where !take(word) {
                self = saved
                return false
            }
            return true
        }

        /// "10/8" or "10월 8일".
        mutating func takeDate() -> (Int, Int)? {
            let saved = self
            skipSpace()
            guard let month = takeNumber() else {
                self = saved
                return nil
            }
            let korean = take("월")
            guard korean || take("/") else {
                self = saved
                return nil
            }
            guard let day = takeNumber() else {
                self = saved
                return nil
            }
            if korean { _ = take("일") }
            return (month, day)
        }

        /// "9시", "9시 30분", "오후 3시", "3pm", "14:30".
        mutating func takeTime() -> (Int, Int)? {
            let saved = self
            skipSpace()
            var pm: Bool? = take("오전") ? false : (take("오후") ? true : nil)

            guard var hour = takeNumber() else {
                self = saved
                return nil
            }

            var minute = 0
            if take(":") {
                guard let value = takeNumber() else {
                    self = saved
                    return nil
                }
                minute = value
            } else if take("시") {
                if let korean = takeNumber() {
                    minute = korean
                    _ = take("분")
                }
            }

            if pm == nil { pm = take("am") ? false : (take("pm") ? true : nil) }

            if pm == nil && (1...6).contains(hour) {
                hour += 12
            } else if pm == true && hour < 12 {
                hour += 12
            } else if pm == false && hour == 12 {
                hour = 0
            }

            guard hour <= 23, minute <= 59 else {
                self = saved
                return nil
            }
            return (hour, minute)
        }

        private mutating func takeNumber() -> Int? {
            skipSpace()
            let start = index
            while index < text.count, (0x30...0x39).contains(text[index]) { index += 1 }
            guard index > start else { return nil }
            return Int(String(decoding: text[start..<index], as: UTF16.self))
        }

        private mutating func skipSpace() {
            while index < text.count, isWhite(text[index]) { index += 1 }
        }

        private func isAsciiLetter(_ unit: UInt16) -> Bool {
            (0x41...0x5A).contains(unit) || (0x61...0x7A).contains(unit)
        }

        private func isAsciiLetterOrDigit(_ unit: UInt16) -> Bool {
            isAsciiLetter(unit) || (0x30...0x39).contains(unit)
        }
    }
}
