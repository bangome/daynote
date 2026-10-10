import ActivityKit
import AppIntents
import Foundation
import WidgetKit

/// A check on a widget row (design §09, motion M8).
///
/// Runs in the extension, not the app: the app is .NET and cannot host an App Intent. So the check
/// is queued for the app to carry out through its store the next time it runs, and the snapshot
/// is marked done at once so the ring fills now rather than whenever the app is next opened.
/// WidgetKit reloads the widget's timeline when this returns.
struct CompleteTodoIntent: AppIntent {
    static let title: LocalizedStringResource = "Complete to-do"
    static let isDiscoverable = false

    @Parameter(title: "Row") var rowKey: String
    @Parameter(title: "Date") var date: String

    init() {}

    init(todo: GlanceTodo, day: LocalDay) {
        rowKey = todo.rowKey
        date = day.iso
    }

    func perform() async throws -> some IntentResult {
        guard let store = GlanceStore.shared, let day = LocalDay(iso: date),
              let todo = store.load()?.day(day).todos.first(where: { $0.rowKey == rowKey }), !todo.done else {
            return .result()
        }
        try store.complete(todo, on: day)
        RecentChecks.record(rowKey)
        return .result()
    }
}

/// Control Center's two buttons (design §07): today's note, or the to-do sheet.
/// The app is opened on a `daynote://` link, which is all a .NET app can be asked to do.
@available(iOS 18.0, *)
struct OpenCaptureIntent: AppIntent {
    static let title: LocalizedStringResource = "Open Daynote capture"
    static let isDiscoverable = false

    @Parameter(title: "With @") var withAt: Bool

    init() {}

    init(withAt: Bool) {
        self.withAt = withAt
    }

    func perform() async throws -> some IntentResult & OpensIntent {
        .result(opensIntent: OpenURLIntent(GlanceLinks.capture(withAt: withAt)))
    }
}

/// "알림 끄기" on the Live Activity: ends it here and now. The app does not start it again for the
/// same event, because it keeps the ids it was told to leave alone (see `DaynoteBridge`).
struct DismissEventActivityIntent: AppIntent {
    static let title: LocalizedStringResource = "Dismiss event"
    static let isDiscoverable = false

    @Parameter(title: "Event") var eventId: String

    init() {}

    init(eventId: String) {
        self.eventId = eventId
    }

    func perform() async throws -> some IntentResult {
        UserDefaults(suiteName: GlanceStore.appGroup)?.set(eventId, forKey: EventActivity.dismissedKey)
        for activity in Activity<EventActivityAttributes>.activities where activity.attributes.eventId == eventId {
            await activity.end(nil, dismissalPolicy: .immediate)
        }
        return .result()
    }
}

/// The links widgets open the app with. The app's side is `MobileShellViewModel.OpenLinkAsync`.
enum GlanceLinks {
    static func capture(withAt: Bool) -> URL {
        URL(string: withAt ? "daynote://capture?at=1" : "daynote://capture")!
    }

    static func day(_ day: LocalDay) -> URL { URL(string: "daynote://day?date=\(day.iso)")! }

    static let todos = URL(string: "daynote://todos")!

    static func note(id: String?, date: String?, fallback: LocalDay) -> URL {
        guard let id, let date else { return day(fallback) }
        return URL(string: "daynote://note?date=\(date)&id=\(id)")!
    }
}
