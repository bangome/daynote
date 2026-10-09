import SwiftUI
import WidgetKit

// The watch's complications and its Smart Stack widget (Apple Watch design §04), drawn from the
// snapshot the watch app keeps in its App Group container.

struct WatchEntry: TimelineEntry {
    let date: Date
    let snapshot: GlanceSnapshot?
    var relevance: TimelineEntryRelevance?

    var text: GlanceText { GlanceText(snapshot) }
    var day: GlanceDay { snapshot?.day(LocalDay(date)) ?? GlanceDay(date: LocalDay(date).iso, todos: [], events: []) }
    var open: [GlanceTodo] { day.open }

    var nextEvent: GlanceEvent? {
        let minutes = LocalMoment(date).minutesOfDay
        return day.events.first { event in
            guard let start = event.start, let (h, m) = LocalMoment.clock(event.end ?? start) else { return false }
            return h * 60 + m > minutes
        }
    }

    /// To-dos due after the next event starts: "이후 할 일 4".
    var todosAfterNext: Int {
        guard let start = nextEvent?.start, let (h, m) = LocalMoment.clock(start) else { return open.count }
        return open.filter { todo in todo.time.flatMap(LocalMoment.clock).map { $0.0 * 60 + $0.1 >= h * 60 + m } ?? true }.count
    }
}

struct WatchProvider: TimelineProvider {
    func placeholder(in context: Context) -> WatchEntry { WatchEntry(date: Date(), snapshot: GlanceSamples.korean) }

    func getSnapshot(in context: Context, completion: @escaping (WatchEntry) -> Void) {
        completion(WatchEntry(date: Date(), snapshot: GlanceStore.shared?.load() ?? (context.isPreview ? GlanceSamples.korean : nil)))
    }

    /// A quarter-hourly day, with each event's lead-in marked relevant so the Smart Stack raises
    /// it from thirty minutes before (§04).
    func getTimeline(in context: Context, completion: @escaping (Timeline<WatchEntry>) -> Void) {
        let snapshot = GlanceStore.shared?.load()
        let now = Date()
        let quarter = (now.timeIntervalSince1970 / 900).rounded(.down) * 900
        let entries = [now] + (1...24).map { Date(timeIntervalSince1970: quarter + TimeInterval($0) * 900) }
        completion(Timeline(entries: entries.map { date in
            var entry = WatchEntry(date: date, snapshot: snapshot)
            if let event = entry.nextEvent, let start = event.start, let (h, m) = LocalMoment.clock(start) {
                let lead = h * 60 + m - LocalMoment(date).minutesOfDay
                entry.relevance = TimelineEntryRelevance(score: lead <= 30 ? 100 : 10, duration: 15 * 60)
            }
            return entry
        }, policy: .atEnd))
    }
}

@main
struct DaynoteWatchWidgets: WidgetBundle {
    var body: some Widget {
        RemainingComplication()
        CaptureComplication()
        NextEventComplication()
        UpNextStackWidget()
    }
}

/// Circular "4" in a ring of what is done, and the corner "4 할 일".
struct RemainingComplication: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.watch.remaining", provider: WatchProvider()) { entry in
            RemainingView(entry: entry).containerBackground(for: .widget) { Color.clear }
        }
        .configurationDisplayName(Text("complication.name"))
        .description(Text("complication.remaining.description"))
        .supportedFamilies([.accessoryCircular, .accessoryCorner])
    }
}

struct RemainingView: View {
    var entry: WatchEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        let total = max(entry.day.todos.count, 1)
        let done = Double(total - entry.open.count)
        if family == .accessoryCorner {
            Text("\(entry.open.count)")
                .font(.system(size: 20, weight: .heavy))
                .foregroundStyle(Color(hex: "#ff9a52"))
                .widgetLabel { Text(entry.text.todosUnit) }
        } else {
            Gauge(value: done, in: 0...Double(total)) {
                EmptyView()
            } currentValueLabel: {
                Text("\(entry.open.count)").font(.system(size: 18, weight: .heavy)).foregroundStyle(Color(hex: "#ff9a52"))
            }
            .gaugeStyle(.accessoryCircularCapacity)
            .tint(Color(hex: "#ff9a52"))
        }
    }
}

/// Circular microphone: opens the app for a dictation.
struct CaptureComplication: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.watch.capture", provider: WatchProvider()) { _ in
            ZStack {
                AccessoryWidgetBackground()
                Image(systemName: "mic").font(.system(size: 18, weight: .semibold)).foregroundStyle(Color(hex: "#ff9a52"))
            }
            .containerBackground(for: .widget) { Color.clear }
        }
        .configurationDisplayName(Text("complication.name"))
        .description(Text("complication.capture.description"))
        .supportedFamilies([.accessoryCircular])
    }
}

/// Rectangular next event with what follows it, and the inline "16:00 디자인 리뷰".
struct NextEventComplication: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.watch.next", provider: WatchProvider()) { entry in
            NextEventView(entry: entry).containerBackground(for: .widget) { Color.clear }
        }
        .configurationDisplayName(Text("complication.name"))
        .description(Text("complication.next.description"))
        .supportedFamilies([.accessoryRectangular, .accessoryInline])
    }
}

struct NextEventView: View {
    var entry: WatchEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        if family == .accessoryInline {
            if let event = entry.nextEvent {
                Text([event.start, event.title].compactMap { $0 }.joined(separator: " "))
            } else {
                Text(entry.text.left(entry.open.count))
            }
        } else if let event = entry.nextEvent {
            VStack(alignment: .leading, spacing: 0) {
                Text(entry.text.range(event.start, event.end)).font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Color(hex: "#ff9a52"))
                Text(event.title).font(.system(size: 15, weight: .bold)).lineLimit(1)
                Text(entry.text.todosAfter(entry.todosAfterNext)).font(.system(size: 12)).foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        } else if let next = entry.open.first {
            VStack(alignment: .leading, spacing: 0) {
                Text(entry.text.nextTodoShort(at: next.time)).font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Color(hex: "#ff9a52"))
                Text(next.title).font(.system(size: 15, weight: .bold)).lineLimit(2)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            Text(entry.text.nothingToday).font(.system(size: 14, weight: .semibold))
        }
    }
}

/// The Smart Stack card: next event and next to-do, raised from thirty minutes before (A14).
struct UpNextStackWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.watch.stack", provider: WatchProvider()) { entry in
            StackView(entry: entry).containerBackground(for: .widget) { Color(hex: "#2a1b10") }
        }
        .configurationDisplayName(Text("complication.name"))
        .description(Text("complication.stack.description"))
        .supportedFamilies([.accessoryRectangular])
    }
}

struct StackView: View {
    var entry: WatchEntry

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 4) {
                DaynoteMark(size: 13)
                Text("Daynote").font(.system(size: 12, weight: .bold)).foregroundStyle(Color(hex: "#ff9a52"))
            }
            if let event = entry.nextEvent {
                Text([event.start, event.title].compactMap { $0 }.joined(separator: " "))
                    .font(.system(size: 15, weight: .bold)).lineLimit(1)
            }
            if let next = entry.open.first {
                HStack(spacing: 5) {
                    CheckRing(color: GlanceTheme.watch.ring(for: next.listId, in: entry.snapshot), done: false, size: 12, lineWidth: 1.5)
                    Text(next.title).font(.system(size: 13)).lineLimit(1)
                    Spacer(minLength: 2)
                    if let time = next.time { Text(time).font(.system(size: 12)).foregroundStyle(.secondary) }
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
