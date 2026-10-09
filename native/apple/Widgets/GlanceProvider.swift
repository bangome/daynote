import Foundation
import WidgetKit

/// One moment a widget draws: the snapshot, the day that is today at that moment, and the rows
/// checked in the last instant, which stay on screen filled-in before they leave (design §09).
struct GlanceEntry: TimelineEntry {
    let date: Date
    let snapshot: GlanceSnapshot?
    let recentlyChecked: Set<String>

    var today: LocalDay { LocalDay(date) }
    var now: LocalMoment { LocalMoment(date) }
    var text: GlanceText { GlanceText(snapshot) }
    var day: GlanceDay { snapshot?.day(today) ?? GlanceDay(date: today.iso, todos: [], events: []) }

    /// To-dos to draw: what is open, plus what was checked a moment ago.
    var todos: [GlanceTodo] { day.todos.filter { !$0.done || recentlyChecked.contains($0.rowKey) } }

    var remaining: Int { day.open.count }

    /// Events still ahead or under way, in time order. An all-day event stays for the day.
    var upcomingEvents: [GlanceEvent] {
        day.events.filter { event in
            guard let end = event.end ?? event.start, let (h, m) = LocalMoment.clock(end) else { return true }
            return h * 60 + m > now.minutesOfDay
        }
    }

    var nextEvent: GlanceEvent? { upcomingEvents.first { !$0.isAllDay } ?? upcomingEvents.first }

    /// Minutes from now to an event's start, negative once it has begun.
    func minutesUntil(_ event: GlanceEvent) -> Int? {
        guard let start = event.start, let (h, m) = LocalMoment.clock(start) else { return nil }
        return h * 60 + m - now.minutesOfDay
    }

    /// Past its time and not done: drawn red, as in the app.
    func isOverdue(_ todo: GlanceTodo) -> Bool {
        guard !todo.done, let time = todo.time, let (h, m) = LocalMoment.clock(time) else { return false }
        return h * 60 + m < now.minutesOfDay
    }

    static let placeholder = GlanceEntry(date: Date(), snapshot: GlanceSamples.korean, recentlyChecked: [])
}

/// Reads the snapshot the app wrote and lays a day of entries over it.
///
/// The app reloads the timelines whenever it writes a new snapshot; between those, the entries
/// here are what keeps "1시간 30분 후" counting down and moves the widget onto tomorrow at
/// midnight without the app running.
struct GlanceProvider: TimelineProvider {
    func placeholder(in context: Context) -> GlanceEntry { .placeholder }

    func getSnapshot(in context: Context, completion: @escaping (GlanceEntry) -> Void) {
        let stored = GlanceStore.shared?.load()
        completion(GlanceEntry(date: Date(), snapshot: context.isPreview && stored == nil ? GlanceSamples.korean : stored, recentlyChecked: []))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<GlanceEntry>) -> Void) {
        let snapshot = GlanceStore.shared?.load()
        completion(Timeline(entries: GlanceProvider.entries(snapshot: snapshot, from: Date()), policy: .atEnd))
    }

    /// Now; a second on, once a just-checked row has had its moment; every quarter hour for the
    /// next six hours, and each event's start and end in between; and midnight.
    static func entries(snapshot: GlanceSnapshot?, from now: Date, calendar: Calendar = .current) -> [GlanceEntry] {
        let recent = RecentChecks.load(now: now)
        var moments: Set<Date> = [now]
        if !recent.isEmpty { moments.insert(now.addingTimeInterval(RecentChecks.window)) }

        // Quarter hours on the wall clock; every zone in use is offset from UTC by a whole quarter.
        let quarter = (now.timeIntervalSince1970 / 900).rounded(.down) * 900
        for step in 1...24 {
            moments.insert(Date(timeIntervalSince1970: quarter + TimeInterval(step) * 900))
        }

        let today = LocalDay(now, calendar: calendar)
        for event in snapshot?.day(today).events ?? [] {
            for time in [event.start, event.end].compactMap({ $0 }) {
                if let (h, m) = LocalMoment.clock(time) {
                    let at = LocalMoment(day: today, hour: h, minute: m).date(in: calendar)
                    if at > now { moments.insert(at) }
                }
            }
        }
        moments.insert(today.adding(days: 1, calendar: calendar).start(in: calendar))

        return moments.sorted().map { moment in
            GlanceEntry(
                date: moment,
                snapshot: snapshot,
                recentlyChecked: moment < now.addingTimeInterval(RecentChecks.window) ? recent : [])
        }
    }
}

/// Rows checked on a widget a moment ago, so the next draw shows them filled in before they go
/// (design §09: "링이 채워진 채 0.6초 머문 뒤 목록에서 빠지며"). Kept in the group's defaults
/// because the intent that checks and the provider that draws run as separate calls.
enum RecentChecks {
    static let window: TimeInterval = 0.6
    private static let key = "glance.recentChecks"

    static func record(_ rowKey: String, now: Date = Date()) {
        let defaults = UserDefaults(suiteName: GlanceStore.appGroup)
        var stamps = defaults?.dictionary(forKey: key) as? [String: Double] ?? [:]
        stamps = stamps.filter { now.timeIntervalSince1970 - $0.value < 60 }
        stamps[rowKey] = now.timeIntervalSince1970
        defaults?.set(stamps, forKey: key)
    }

    static func load(now: Date) -> Set<String> {
        let stamps = UserDefaults(suiteName: GlanceStore.appGroup)?.dictionary(forKey: key) as? [String: Double] ?? [:]
        return Set(stamps.filter { now.timeIntervalSince1970 - $0.value < window + 1 }.keys)
    }
}
