#if canImport(ActivityKit) && os(iOS)
import ActivityKit
import Foundation

/// The Live Activity for an event about to start (design §08). Compiled into both the widget
/// extension, which draws it, and the bridge, which starts it; ActivityKit matches the two by
/// this type's shape, so there is one definition.
public struct EventActivityAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        public var title: String
        public var start: Date
        public var end: Date

        public init(title: String, start: Date, end: Date) {
            self.title = title
            self.start = start
            self.end = end
        }
    }

    public var eventId: String
    public var listName: String
    public var english: Bool
    /// `daynote://note?…` for the event's note, or the day.
    public var noteLink: String

    public init(eventId: String, listName: String, english: Bool, noteLink: String) {
        self.eventId = eventId
        self.listName = listName
        self.english = english
        self.noteLink = noteLink
    }
}

/// When a Live Activity runs: from fifteen minutes before an event to its end. To-dos never get
/// one — a notification is enough for those (§08).
public enum EventActivity {
    public static let leadMinutes = 15

    /// The event the user dismissed, which is not started again.
    public static let dismissedKey = "glance.dismissedEvent"

    /// The timed event that should have an activity at `now`, if any, with its start and end.
    public static func due(in snapshot: GlanceSnapshot, at now: Date, calendar: Calendar = .current)
        -> (event: GlanceEvent, start: Date, end: Date)?
    {
        let today = LocalDay(now, calendar: calendar)
        for event in snapshot.day(today).events {
            guard let start = event.start, let (sh, sm) = LocalMoment.clock(start) else { continue }
            let begins = LocalMoment(day: today, hour: sh, minute: sm).date(in: calendar)
            let ends = event.end.flatMap(LocalMoment.clock).map { LocalMoment(day: today, hour: $0.0, minute: $0.1).date(in: calendar) }
                ?? begins.addingTimeInterval(3600)
            if now >= begins.addingTimeInterval(TimeInterval(-leadMinutes * 60)) && now < ends {
                return (event, begins, ends)
            }
        }
        return nil
    }
}
#endif
