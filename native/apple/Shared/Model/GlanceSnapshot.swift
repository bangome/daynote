import Foundation

/// The day as the app decided it, read from the shared folder (docs/APPLE_EXTENSIONS.md §3).
///
/// Mirrors `Daynote.App.Glance.GlanceSnapshot`. Nothing here decides which rows a day has or in
/// what order: the app's `AgendaDay` already did, and a widget that re-derived it is how a to-do
/// ends up on the widget and not in the app. What a reader does decide is which of the seven days
/// is today when it draws, since a widget redraws long after the app last ran.
public struct GlanceSnapshot: Codable, Equatable, Sendable {
    public static let currentSchema = 1

    public var schema: Int
    public var generatedUtc: String
    public var zone: String
    public var language: String
    public var today: String
    public var locked: Bool
    public var lists: [GlanceList]
    public var days: [GlanceDay]
    public var week: [GlanceWeekDay]
    public var favorites: [GlanceFavorite]

    public init(
        schema: Int = GlanceSnapshot.currentSchema, generatedUtc: String, zone: String, language: String,
        today: String, locked: Bool = false, lists: [GlanceList], days: [GlanceDay], week: [GlanceWeekDay],
        favorites: [GlanceFavorite]
    ) {
        self.schema = schema
        self.generatedUtc = generatedUtc
        self.zone = zone
        self.language = language
        self.today = today
        self.locked = locked
        self.lists = lists
        self.days = days
        self.week = week
        self.favorites = favorites
    }

    /// The app's language, which the extensions follow rather than the phone's.
    public var isEnglish: Bool { language == "en" }

    /// The day for `date`, or an empty one when the snapshot does not reach that far.
    public func day(_ date: LocalDay) -> GlanceDay {
        days.first { $0.date == date.iso } ?? GlanceDay(date: date.iso, todos: [], events: [])
    }

    public func list(_ id: String) -> GlanceList? {
        lists.first { $0.id == id }
    }

    /// Marks `todo` on `day` done or open. False when the row is no longer there.
    public mutating func setDone(_ todo: GlanceTodo, on day: LocalDay, _ done: Bool) -> Bool {
        guard let (d, r) = index(of: todo, on: day) else { return false }
        days[d].todos[r].done = done
        return true
    }

    /// Takes `todo` off `day`. False when the row is no longer there.
    public mutating func remove(_ todo: GlanceTodo, on day: LocalDay) -> Bool {
        guard let (d, r) = index(of: todo, on: day) else { return false }
        days[d].todos.remove(at: r)
        return true
    }

    private func index(of todo: GlanceTodo, on day: LocalDay) -> (Int, Int)? {
        guard let d = days.firstIndex(where: { $0.date == day.iso }),
              let r = days[d].todos.firstIndex(where: { $0.rowKey == todo.rowKey }) else { return nil }
        return (d, r)
    }
}

public struct GlanceList: Codable, Equatable, Sendable {
    public var id: String
    public var name: String
    /// `#rrggbb` on a light background.
    public var color: String
    /// `#rrggbb` on a dark one.
    public var colorDark: String

    public init(id: String, name: String, color: String, colorDark: String) {
        self.id = id
        self.name = name
        self.color = color
        self.colorDark = colorDark
    }
}

public struct GlanceDay: Codable, Equatable, Sendable {
    public var date: String
    public var todos: [GlanceTodo]
    public var events: [GlanceEvent]

    public init(date: String, todos: [GlanceTodo], events: [GlanceEvent]) {
        self.date = date
        self.todos = todos
        self.events = events
    }

    /// What is left, in the app's order: timed first, then undated.
    public var open: [GlanceTodo] { todos.filter { !$0.done } }
}

public struct GlanceTodo: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var seriesId: String?
    public var occurrence: String?
    public var title: String
    public var listId: String
    /// `HH:mm`, or nil for a day with no clock.
    public var time: String?
    public var repeats: Bool
    public var done: Bool
    public var noteId: String?
    public var noteDate: String?

    public init(
        id: String, seriesId: String? = nil, occurrence: String? = nil, title: String, listId: String,
        time: String? = nil, repeats: Bool = false, done: Bool = false, noteId: String? = nil, noteDate: String? = nil
    ) {
        self.id = id
        self.seriesId = seriesId
        self.occurrence = occurrence
        self.title = title
        self.listId = listId
        self.time = time
        self.repeats = repeats
        self.done = done
        self.noteId = noteId
        self.noteDate = noteDate
    }

    /// One key per row on a day: an occurrence of a rule is its series plus which occurrence.
    public var rowKey: String { seriesId.map { "\($0)@\(occurrence ?? "")" } ?? id }
}

public struct GlanceEvent: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var title: String
    public var listId: String
    /// `HH:mm`, or nil all day.
    public var start: String?
    public var end: String?
    public var repeats: Bool
    public var noteId: String?
    public var noteDate: String?

    public init(
        id: String, title: String, listId: String, start: String?, end: String?, repeats: Bool = false,
        noteId: String? = nil, noteDate: String? = nil
    ) {
        self.id = id
        self.title = title
        self.listId = listId
        self.start = start
        self.end = end
        self.repeats = repeats
        self.noteId = noteId
        self.noteDate = noteDate
    }

    public var isAllDay: Bool { start == nil }
}

public struct GlanceWeekDay: Codable, Equatable, Sendable {
    public var date: String
    public var noteCount: Int
    public var titles: [String]

    public init(date: String, noteCount: Int, titles: [String]) {
        self.date = date
        self.noteCount = noteCount
        self.titles = titles
    }
}

public struct GlanceFavorite: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var date: String
    public var title: String
    public var preview: String

    public init(id: String, date: String, title: String, preview: String) {
        self.id = id
        self.date = date
        self.title = title
        self.preview = preview
    }
}

/// Something done outside the app, for the app to carry out (§4). Mirrors `GlanceAction`.
public struct GlanceAction: Codable, Equatable, Sendable {
    public var schema: Int = 1
    public var id: String
    public var type: String
    public var createdUtc: String
    public var itemId: String?
    public var seriesId: String?
    public var occurrence: String?
    public var date: String?
    public var text: String?
    public var kind: String?
    public var capturedLocal: String?

    public static let complete = "complete"
    /// A done row made open again. Sets, never toggles, as `complete` does.
    public static let uncomplete = "uncomplete"
    /// The row deleted; for an occurrence of a rule, that occurrence only (an EXDATE on the phone).
    public static let delete = "delete"
    public static let capture = "capture"

    /// A check on `todo`, shown on `day`.
    public static func completing(_ todo: GlanceTodo, on day: LocalDay, now: Date = Date()) -> GlanceAction {
        onRow(complete, todo, on: day, now: now)
    }

    /// A check taken off `todo`, shown on `day`.
    public static func uncompleting(_ todo: GlanceTodo, on day: LocalDay, now: Date = Date()) -> GlanceAction {
        onRow(uncomplete, todo, on: day, now: now)
    }

    /// `todo` deleted, as shown on `day`.
    public static func deleting(_ todo: GlanceTodo, on day: LocalDay, now: Date = Date()) -> GlanceAction {
        onRow(delete, todo, on: day, now: now)
    }

    /// An action on one row, which the phone finds again by id, or by series and occurrence.
    private static func onRow(_ type: String, _ todo: GlanceTodo, on day: LocalDay, now: Date) -> GlanceAction {
        GlanceAction(
            id: UUID().uuidString.lowercased(), type: type, createdUtc: GlanceClock.utcStamp(now),
            itemId: todo.id, seriesId: todo.seriesId, occurrence: todo.occurrence, date: day.iso)
    }

    /// A dictated sentence and what the readback made of it: `task`, `event` or `note`.
    public static func capturing(_ text: String, kind: String, at said: LocalMoment, now: Date = Date()) -> GlanceAction {
        GlanceAction(
            id: UUID().uuidString.lowercased(), type: capture, createdUtc: GlanceClock.utcStamp(now),
            text: text, kind: kind, capturedLocal: said.iso)
    }
}
