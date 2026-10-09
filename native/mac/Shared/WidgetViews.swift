import AppIntents
import AppKit
import SwiftUI

/// Ticks a to-do from the desktop (design §02: "macOS 위젯은 데스크톱에서 직접 체크할 수 있으므로").
///
/// Runs in the widget's own process, which cannot open the app's database, so it queues a
/// `complete` action for the app and marks the row done in the snapshot
/// (`GlanceStore.complete`, the iPhone widgets' path); WidgetKit redraws once it returns.
struct CompleteTodoIntent: AppIntent {
    static let title: LocalizedStringResource = "Complete to-do"
    static let isDiscoverable = false

    @Parameter(title: "Item") var itemId: String
    @Parameter(title: "Series") var seriesId: String?
    @Parameter(title: "Occurrence") var occurrence: String?
    @Parameter(title: "Day") var day: String

    init() {}

    init(todo: GlanceTodo, day: LocalDay) {
        itemId = todo.id
        seriesId = todo.seriesId
        occurrence = todo.occurrence
        self.day = day.iso
    }

    func perform() async throws -> some IntentResult {
        guard let store = GlanceStore.shared, let shown = LocalDay(iso: day) else { return .result() }
        let todo = GlanceTodo(id: itemId, seriesId: seriesId, occurrence: occurrence, title: "", listId: "")
        try store.complete(todo, on: shown)
        return .result()
    }
}

/// Where a click lands: the app opens the day it names (docs/APPLE_EXTENSIONS.md §7).
enum WidgetLink {
    static func day(_ day: LocalDay) -> URL { URL(string: "daynote://day?date=" + day.iso)! }

    /// Set by the preview renderer: `ImageRenderer` draws a `Link` as a placeholder, so off screen
    /// the content is drawn bare. A widget never sets it.
    nonisolated(unsafe) static var offscreen = false
}

/// A `Link` to a day inside the widget, and just its content when drawn off screen.
struct DayLink<Content: View>: View {
    let day: LocalDay
    @ViewBuilder let content: () -> Content

    var body: some View {
        if WidgetLink.offscreen {
            content()
        } else {
            Link(destination: WidgetLink.day(day), label: content)
        }
    }
}

/// The lines only the Mac's widgets say; everything else is the phone's `GlanceText`.
extension GlanceText {
    var clickToOpen: String { english ? "Click to open the day in Daynote" : "클릭하면 Daynote에서 그 날짜를 엽니다" }
    var todayDescription: String { english ? "Today's to-dos and what's next" : "오늘 할 일과 다음 일정" }
    var todosDescription: String { english ? "Tick off what's left, right here" : "남은 할 일을 바로 체크해요" }
    var upNextDescription: String { english ? "The next event on your calendar" : "곧 시작하는 일정" }
    var weekDescription: String { english ? "The week, what's next, and your to-dos" : "이번 주, 다음 일정, 할 일" }
}

/// What every widget is drawn from: the snapshot, the moment the entry is for, and the scheme.
///
/// Which of the snapshot's seven days is today is decided here, at draw time — an entry drawn
/// after midnight shows the next day even if the app has not run since.
struct WidgetContext {
    let snapshot: GlanceSnapshot?
    let now: Date
    let scheme: ColorScheme

    var theme: WidgetTheme { .of(scheme) }
    var text: GlanceText { GlanceText(snapshot) }
    var today: LocalDay { LocalDay(now) }
    var moment: LocalMoment { LocalMoment(now) }
    var day: GlanceDay? { snapshot?.day(today) }

    /// What is left first, then anything just ticked, while there is room for it.
    var todos: [GlanceTodo] { (day?.open ?? []) + (day?.todos.filter(\.done) ?? []) }
    var remaining: Int { day?.open.count ?? 0 }

    func tint(_ todo: GlanceTodo) -> Color { theme.tint(snapshot?.list(todo.listId), scheme) }
    func tint(_ event: GlanceEvent) -> Color { theme.tint(snapshot?.list(event.listId), scheme) }

    /// The first timed event, from today on, that has not finished yet.
    var nextEvent: (day: LocalDay, event: GlanceEvent)? {
        guard let snapshot, !snapshot.locked else { return nil }
        for offset in 0..<7 {
            let date = today.adding(days: offset)
            for event in snapshot.day(date).events {
                guard let start = event.start, let (hour, minute) = LocalMoment.clock(start) else { continue }
                let ends = event.end.flatMap(LocalMoment.clock).map { LocalMoment(day: date, hour: $0.0, minute: $0.1) }
                    ?? LocalMoment(day: date, hour: hour, minute: minute)
                if ends > moment { return (date, event) }
            }
        }
        return nil
    }

    /// The line a widget shows in place of rows it cannot draw, or nil when it can draw them.
    var unavailable: String? {
        guard let snapshot else { return text.openAppOnce }
        return snapshot.locked ? text.locked : nil
    }
}

// MARK: - Pieces

struct WidgetHeader: View {
    let context: WidgetContext
    let title: String
    var trailing: String?

    var body: some View {
        HStack(spacing: 6) {
            SymbolImage().frame(width: 15, height: 15)
            Text(title).font(WidgetFont.of(12, .heavy)).foregroundStyle(context.theme.heading)
                .lineLimit(1)
            Spacer(minLength: 0)
            if let trailing {
                Text(trailing).font(WidgetFont.of(11.5, .semibold)).foregroundStyle(context.theme.muted)
            }
        }
        .padding(.bottom, 8)
    }
}

/// The app symbol beside every heading. Read from the extension's Resources, or from
/// `DAYNOTE_WIDGET_ASSETS` when the preview renderer draws outside any bundle.
struct SymbolImage: View {
    var body: some View {
        if let image = Self.image {
            Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fit)
        } else {
            Circle().fill(Color(hex: 0xEE7F35))
        }
    }

    private static let image: NSImage? = {
        if let url = Bundle.main.url(forResource: "daynote-symbol", withExtension: "png") {
            return NSImage(contentsOf: url)
        }
        if let folder = ProcessInfo.processInfo.environment["DAYNOTE_WIDGET_ASSETS"] {
            return NSImage(contentsOfFile: folder + "/daynote-symbol.png")
        }
        return nil
    }()
}

struct CheckRing: View {
    let context: WidgetContext
    let todo: GlanceTodo

    var body: some View {
        // Completing completes: a ticked ring has nothing left to do, as on the phone, so it is
        // drawn rather than offered (a disabled button would draw it dimmed).
        if todo.done {
            ring
        } else {
            Button(intent: CompleteTodoIntent(todo: todo, day: context.today)) { ring }
                .buttonStyle(.plain)
        }
    }

    private var ring: some View {
        let tint = context.tint(todo)
        return ZStack {
            if todo.done {
                Circle().fill(tint)
                Image(systemName: "checkmark").font(.system(size: 8, weight: .heavy))
                    .foregroundStyle(context.scheme == .dark ? context.theme.todayInk : .white)
            } else {
                Circle().strokeBorder(tint, lineWidth: 2)
            }
        }
        .frame(width: 16, height: 16)
        .contentShape(Circle())
    }
}

struct TodoRow: View {
    let context: WidgetContext
    let todo: GlanceTodo
    var showsTime = true
    var height: CGFloat = 33

    var body: some View {
        HStack(spacing: 9) {
            CheckRing(context: context, todo: todo)
            DayLink(day: context.today) {
                HStack(spacing: 9) {
                    Text(todo.title)
                        .font(WidgetFont.of(13.5))
                        .strikethrough(todo.done)
                        .foregroundStyle(todo.done ? context.theme.muted : context.theme.ink)
                        .lineLimit(1).truncationMode(.tail)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    trailing
                }
            }
        }
        .frame(minHeight: height)
    }

    @ViewBuilder private var trailing: some View {
        if showsTime, let time = todo.time, let (hour, minute) = LocalMoment.clock(time) {
            // Red once the clock has passed it: the desktop has no notifications, so this is the alarm.
            let overdue = !todo.done && LocalMoment(day: context.today, hour: hour, minute: minute) < context.moment
            Text(time)
                .font(WidgetFont.of(12, .bold)).monospacedDigit()
                .foregroundStyle(overdue ? context.theme.overdue : context.theme.secondary)
        } else if showsTime, todo.repeats {
            RepeatGlyph().stroke(context.theme.muted, style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
                .frame(width: 10, height: 10)
        }
    }
}

/// The design's ↻: an open arc with an arrowhead, drawn rather than taken from SF Symbols so it
/// matches the desktop panel's glyph stroke for stroke.
struct RepeatGlyph: Shape {
    func path(in rect: CGRect) -> Path {
        let scale = rect.width / 12
        var path = Path()
        path.addArc(
            center: CGPoint(x: 6 * scale, y: 6 * scale), radius: 4.2 * scale,
            startAngle: .degrees(0), endAngle: .degrees(-50), clockwise: false)
        path.move(to: CGPoint(x: 9.4 * scale, y: 0.8 * scale))
        path.addLine(to: CGPoint(x: 9.1 * scale, y: 3.3 * scale))
        path.addLine(to: CGPoint(x: 6.6 * scale, y: 3.1 * scale))
        return path
    }
}

struct EventBlock: View {
    let context: WidgetContext
    let day: LocalDay
    let event: GlanceEvent
    var titleSize: CGFloat = 14
    var detailSize: CGFloat = 12

    var body: some View {
        DayLink(day: day) {
            HStack(alignment: .top, spacing: 9) {
                RoundedRectangle(cornerRadius: 2).fill(context.tint(event)).frame(width: 3.5)
                VStack(alignment: .leading, spacing: 1) {
                    Text(event.title).font(WidgetFont.of(titleSize, .heavy)).foregroundStyle(context.theme.ink)
                        .lineLimit(2)
                    Text(detail).font(WidgetFont.of(detailSize)).foregroundStyle(context.theme.secondary)
                        .lineLimit(2).fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// "16:00–17:00 · 1시간 30분 후", or the day in front of the range when it is not today.
    private var detail: String {
        let range = context.text.range(event.start, event.end)
        guard day == context.today else { return context.text.shortDate(day) + " " + range }
        guard let start = event.start, let (hour, minute) = LocalMoment.clock(start) else { return range }
        let minutes = LocalMoment(day: day, hour: hour, minute: minute).date().timeIntervalSince(context.now) / 60
        return range + " · " + context.text.until(minutes: Int(minutes.rounded(.up)))
    }
}

struct SectionLabel: View {
    let context: WidgetContext
    let text: String
    var bottom: CGFloat = 6

    var body: some View {
        Text(text).font(WidgetFont.of(11, .bold)).foregroundStyle(context.theme.muted)
            .padding(.bottom, bottom)
    }
}

struct EmptyLine: View {
    let context: WidgetContext
    let text: String

    var body: some View {
        Text(text).font(WidgetFont.of(12.5)).foregroundStyle(context.theme.muted)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// The to-do rows, or the line that stands in for them.
struct TodoRows: View {
    let context: WidgetContext
    let limit: Int
    var showsTime = true
    var height: CGFloat = 33

    var body: some View {
        if let unavailable = context.unavailable {
            EmptyLine(context: context, text: unavailable)
        } else if context.todos.isEmpty {
            EmptyLine(context: context, text: context.text.nothingToday)
        } else {
            ForEach(context.todos.prefix(limit), id: \.rowKey) { todo in
                TodoRow(context: context, todo: todo, showsTime: showsTime, height: height)
            }
        }
    }
}

// MARK: - The four widgets (design §02 B8/B9)

/// Small: 할 일 — what is left today, tickable.
struct TodosWidgetView: View {
    let context: WidgetContext

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(
                context: context, title: context.text.todos,
                trailing: context.unavailable == nil ? context.text.left(context.remaining) : nil)
            TodoRows(context: context, limit: 3, showsTime: false)
            Spacer(minLength: 0)
        }
    }
}

/// Small: 다음 일정 — the next event, sitting at the bottom.
struct UpNextWidgetView: View {
    let context: WidgetContext

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(context: context, title: context.text.upNext)
            Spacer(minLength: 0)
            if let unavailable = context.unavailable {
                EmptyLine(context: context, text: unavailable)
            } else if let next = context.nextEvent {
                EventBlock(context: context, day: next.day, event: next.event)
            } else {
                EmptyLine(context: context, text: context.text.noEvents)
            }
        }
    }
}

/// Medium: the day — to-dos on the left, the next event on the right.
struct TodayWidgetView: View {
    let context: WidgetContext

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(
                context: context, title: context.text.longDate(context.today),
                trailing: context.unavailable == nil ? context.text.left(context.remaining) : nil)
            // The design's 1.35fr : 1fr, with a hairline between.
            GeometryReader { geometry in
                let left = (geometry.size.width - 14) * 1.35 / 2.35
                HStack(alignment: .top, spacing: 0) {
                    VStack(alignment: .leading, spacing: 0) {
                        TodoRows(context: context, limit: 3)
                    }
                    .frame(width: left, alignment: .topLeading)
                    Rectangle().fill(context.theme.divider).frame(width: 1).padding(.leading, 14)
                    VStack(alignment: .leading, spacing: 0) {
                        SectionLabel(context: context, text: context.text.upNext)
                        if let next = context.nextEvent {
                            EventBlock(context: context, day: next.day, event: next.event)
                        } else if context.unavailable == nil {
                            EmptyLine(context: context, text: context.text.noEvents)
                        }
                    }
                    .padding(.leading, 14)
                    .frame(maxWidth: .infinity, alignment: .topLeading)
                }
            }
        }
    }
}

/// Large: 이번 주 — the week strip, the next event, then the to-dos.
struct WeekWidgetView: View {
    let context: WidgetContext

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(context: context, title: context.text.thisWeek)
            WeekStrip(context: context)
            Spacer().frame(height: 12)
            SectionLabel(context: context, text: context.text.upNext)
            if let next = context.nextEvent {
                EventBlock(context: context, day: next.day, event: next.event, titleSize: 14.5, detailSize: 12.5)
            } else if context.unavailable == nil {
                EmptyLine(context: context, text: context.text.noEvents)
            }
            Spacer().frame(height: 12)
            SectionLabel(
                context: context,
                text: context.unavailable == nil ? context.text.todosLeft(context.remaining) : context.text.todos,
                bottom: 2)
            TodoRows(context: context, limit: 5, height: 32)
            Spacer(minLength: 0)
            Text(context.text.clickToOpen).font(WidgetFont.of(11.5)).foregroundStyle(context.theme.muted)
        }
    }
}

/// Sunday to Saturday around today, with a dot per note on each day (iPhone design: "날짜별 노트 수 점").
struct WeekStrip: View {
    let context: WidgetContext

    var body: some View {
        let sunday = context.today.adding(days: -context.today.weekday)
        HStack(spacing: 2) {
            ForEach(0..<7, id: \.self) { offset in
                cell(sunday.adding(days: offset))
            }
        }
    }

    private func cell(_ day: LocalDay) -> some View {
        let isToday = day == context.today
        let notes = context.snapshot?.week.first { $0.date == day.iso }?.noteCount ?? 0
        let theme = context.theme
        return DayLink(day: day) {
            VStack(spacing: 2) {
                Text(context.text.weekdayLetter(day.weekday)).font(WidgetFont.of(10))
                    .foregroundStyle(isToday ? theme.todayInk : day.weekday == 0 ? theme.sunday : theme.muted)
                Text("\(day.day)").font(WidgetFont.of(13.5, .heavy))
                    .foregroundStyle(isToday ? theme.todayInk : theme.ink)
                HStack(spacing: 2) {
                    ForEach(0..<min(notes, 3), id: \.self) { _ in
                        Circle().fill(isToday ? theme.todayInk : theme.accent(context.scheme)).frame(width: 4, height: 4)
                    }
                }
                .frame(height: 4)
            }
            .frame(maxWidth: .infinity).frame(height: 46)
            .background(RoundedRectangle(cornerRadius: 10).fill(isToday ? theme.todayFill : .clear))
        }
    }
}
