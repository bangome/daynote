import Foundation

/// A local calendar date, `yyyy-MM-dd`, as the snapshot writes it.
///
/// Wall-clock values throughout, never instants, because that is what the app keeps
/// (docs/TODOS.md §6): "14:00" on the widget is 14:00 wherever the phone happens to be.
public struct LocalDay: Hashable, Comparable, Sendable {
    public var year: Int
    public var month: Int
    public var day: Int

    public init(year: Int, month: Int, day: Int) {
        self.year = year
        self.month = month
        self.day = day
    }

    public init?(iso: String) {
        let parts = iso.split(separator: "-").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        self.init(year: parts[0], month: parts[1], day: parts[2])
    }

    public init(_ date: Date, calendar: Calendar = .current) {
        let parts = calendar.dateComponents([.year, .month, .day], from: date)
        self.init(year: parts.year!, month: parts.month!, day: parts.day!)
    }

    public var iso: String { String(format: "%04d-%02d-%02d", year, month, day) }

    /// Midnight at the start of this day, in `calendar`'s zone.
    public func start(in calendar: Calendar = .current) -> Date {
        calendar.date(from: DateComponents(year: year, month: month, day: day))!
    }

    public func adding(days: Int, calendar: Calendar = .current) -> LocalDay {
        LocalDay(calendar.date(byAdding: .day, value: days, to: start(in: calendar))!, calendar: calendar)
    }

    /// 0 = Sunday … 6 = Saturday, as .NET's `DayOfWeek` counts.
    public var weekday: Int {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        let date = calendar.date(from: DateComponents(year: year, month: month, day: day))!
        return calendar.component(.weekday, from: date) - 1
    }

    public static func < (lhs: LocalDay, rhs: LocalDay) -> Bool {
        (lhs.year, lhs.month, lhs.day) < (rhs.year, rhs.month, rhs.day)
    }
}

/// A local wall clock to the minute, `yyyy-MM-ddTHH:mm`.
public struct LocalMoment: Hashable, Comparable, Sendable {
    public var day: LocalDay
    public var hour: Int
    public var minute: Int

    public init(day: LocalDay, hour: Int, minute: Int) {
        self.day = day
        self.hour = hour
        self.minute = minute
    }

    public init(_ date: Date, calendar: Calendar = .current) {
        let parts = calendar.dateComponents([.hour, .minute], from: date)
        self.init(day: LocalDay(date, calendar: calendar), hour: parts.hour!, minute: parts.minute!)
    }

    public init?(iso: String) {
        let halves = iso.split(separator: "T")
        guard halves.count == 2, let day = LocalDay(iso: String(halves[0])),
              let (hour, minute) = LocalMoment.clock(String(halves[1])) else { return nil }
        self.init(day: day, hour: hour, minute: minute)
    }

    public var iso: String { day.iso + "T" + String(format: "%02d:%02d", hour, minute) }

    public var minutesOfDay: Int { hour * 60 + minute }

    public func date(in calendar: Calendar = .current) -> Date {
        calendar.date(from: DateComponents(year: day.year, month: day.month, day: day.day, hour: hour, minute: minute))!
    }

    /// `"14:00"` → (14, 0).
    public static func clock(_ text: String) -> (Int, Int)? {
        let parts = text.split(separator: ":").compactMap { Int($0) }
        guard parts.count == 2, (0..<24).contains(parts[0]), (0..<60).contains(parts[1]) else { return nil }
        return (parts[0], parts[1])
    }

    public static func < (lhs: LocalMoment, rhs: LocalMoment) -> Bool {
        (lhs.day, lhs.hour, lhs.minute) < (rhs.day, rhs.hour, rhs.minute)
    }
}

public enum GlanceClock {
    /// `2026-10-07T05:30:00Z`, as the app stamps the snapshot.
    public static func utcStamp(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        return formatter.string(from: date)
    }
}
