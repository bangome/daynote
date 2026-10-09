import Foundation
import SwiftUI

/// The watch's state: the snapshot the phone last sent, and what the wearer did since.
///
/// The watch has no database. It shows the phone's snapshot, and what is done on the wrist goes
/// back to the phone as an action (docs/APPLE_EXTENSIONS.md §5) — and shows at once here, because
/// the phone may not apply it for a while: it does so the next time the Daynote app runs.
@MainActor
final class WatchStore: ObservableObject {
    /// A row in its undo window: ticked, struck through, still tappable (design §02).
    struct Checking: Equatable {
        var deadline: Date
    }

    /// What a capture just made on today, shown at the top of the list as "17:00 · 방금".
    struct JustMade: Equatable {
        var title: String
        var time: String?
    }

    @Published private(set) var snapshot: GlanceSnapshot?
    @Published private(set) var checking: [String: Checking] = [:]
    @Published private(set) var completed: Set<String> = []
    @Published var justMade: JustMade?
    /// "10/8에 추가됨 · 보기", for a capture that went to another day.
    @Published var addedElsewhere: LocalDay?

    /// How long a checked row waits, struck through, before it is sent and leaves (design §02).
    static let undoWindow: TimeInterval = 1.5

    /// Hands an action to the phone. Set by the session; a render leaves it as a no-op.
    var send: (GlanceAction) -> Void = { _ in }
    /// Plays the success haptic. Set by the app on watchOS.
    var haptic: () -> Void = {}
    /// The clock, so a render can be pinned to the design's 14:30.
    var now: () -> Date = Date.init

    init(snapshot: GlanceSnapshot? = nil) {
        self.snapshot = snapshot
    }

    var text: GlanceText { GlanceText(snapshot) }
    var today: LocalDay { LocalDay(now()) }
    var day: GlanceDay { snapshot?.day(today) ?? GlanceDay(date: today.iso, todos: [], events: []) }

    /// The rows to draw: open ones, including any in their undo window.
    var todos: [GlanceTodo] { day.todos.filter { !$0.done && !completed.contains($0.rowKey) } }

    /// "남은 4": the window does not count as done until it closes.
    var remaining: Int { todos.count }

    var nextEvent: GlanceEvent? {
        let minutes = LocalMoment(now()).minutesOfDay
        return day.events.first { event in
            guard let end = event.end ?? event.start, let (h, m) = LocalMoment.clock(end) else { return false }
            return h * 60 + m > minutes
        }
    }

    func isOverdue(_ todo: GlanceTodo) -> Bool {
        guard let time = todo.time, let (h, m) = LocalMoment.clock(time) else { return false }
        return h * 60 + m < LocalMoment(now()).minutesOfDay
    }

    func ring(_ todo: GlanceTodo) -> Color { GlanceTheme.watch.ring(for: todo.listId, in: snapshot) }

    /// The phone sent a new snapshot. A completion the phone has now applied is in it as done, so
    /// the local memory of it can go.
    func receive(_ snapshot: GlanceSnapshot) {
        self.snapshot = snapshot
        let doneThere = Set(snapshot.days.flatMap(\.todos).filter(\.done).map(\.rowKey))
        completed.subtract(doneThere)
        if let made = justMade, snapshot.day(today).todos.contains(where: { $0.title == made.title }) {
            justMade = nil
        }
    }

    /// A tap on a row: tick it, or untick it inside the window.
    func tap(_ todo: GlanceTodo) {
        if checking[todo.rowKey] != nil {
            checking[todo.rowKey] = nil
            return
        }

        let deadline = now().addingTimeInterval(Self.undoWindow)
        checking[todo.rowKey] = Checking(deadline: deadline)
        haptic()
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(Self.undoWindow * 1_000_000_000))
            self?.commit(todo, deadline: deadline)
        }
    }

    private func commit(_ todo: GlanceTodo, deadline: Date) {
        // Unticked in the meantime, or ticked again with a fresh window.
        guard checking[todo.rowKey]?.deadline == deadline else { return }
        send(.completing(todo, on: today, now: now()))
        withAnimation(.spring(response: 0.35, dampingFraction: 0.7)) {
            checking[todo.rowKey] = nil
            completed.insert(todo.rowKey)
        }
    }

    /// Makes what the readback offered and sends the sentence to the phone, which reads it again
    /// with its own parser and makes the item.
    func create(_ sentence: String, kind: String, readback: CaptureReadback) {
        send(.capturing(sentence, kind: kind, at: LocalMoment(now()), now: now()))
        guard kind != "note", let day = readback.day else { return }
        if day == today {
            justMade = JustMade(title: readback.title, time: readback.time)
        } else {
            addedElsewhere = day
        }
    }
}
