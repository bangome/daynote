import SwiftUI
import WidgetKit

/// Daynote's desktop and Notification Center widgets: the iPhone set (small, medium, large), with
/// the same tick-in-place behaviour (design "Daynote Menu Bar - Desktop - Android Widgets" §02).
@main
struct DaynoteWidgets: WidgetBundle {
    var body: some Widget {
        DaynoteTodayWidget()
        DaynoteTodosWidget()
        DaynoteUpNextWidget()
        DaynoteWeekWidget()
    }
}

struct SnapshotEntry: TimelineEntry {
    let date: Date
    let snapshot: GlanceSnapshot?
}

/// One timeline for every widget: the snapshot as it stands, redrawn as the clock moves.
///
/// Nothing in the snapshot changes on its own, but "1시간 30분 후", a time turning red and the
/// day itself do, so there is an entry every quarter hour, one at each event's start and end,
/// and one past midnight. The app asks for a reload whenever it writes new content.
struct SnapshotProvider: TimelineProvider {
    func placeholder(in context: Context) -> SnapshotEntry {
        SnapshotEntry(date: Date(), snapshot: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (SnapshotEntry) -> Void) {
        completion(SnapshotEntry(date: Date(), snapshot: GlanceStore.shared?.load()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<SnapshotEntry>) -> Void) {
        let snapshot = GlanceStore.shared?.load()
        let now = Date()
        let horizon = now.addingTimeInterval(6 * 3600)
        var moments = Set(stride(from: 0.0, through: 6 * 3600, by: 15 * 60).map { now.addingTimeInterval($0) })
        for offset in 0..<2 {
            let day = LocalDay(now).adding(days: offset)
            for event in snapshot?.day(day).events ?? [] {
                for clock in [event.start, event.end].compactMap({ $0 }).compactMap(LocalMoment.clock) {
                    let at = LocalMoment(day: day, hour: clock.0, minute: clock.1).date()
                    if at > now, at < horizon { moments.insert(at) }
                }
            }
        }
        // The snapshot carries seven days, so past midnight the widget draws the next one by
        // itself; an entry just after the turn of the day is what makes it redraw then.
        if let midnight = Calendar.current.nextDate(
            after: now, matching: DateComponents(hour: 0, minute: 0), matchingPolicy: .nextTime),
           midnight < horizon {
            moments.insert(midnight.addingTimeInterval(60))
        }
        let entries = moments.sorted().map { SnapshotEntry(date: $0, snapshot: snapshot) }
        completion(Timeline(entries: entries, policy: .atEnd))
    }
}

/// The frame every widget shares: the design's 14pt inset over a translucent card, so the
/// desktop's colour shows through (§02: "바탕화면 색이 비치도록 반투명 배경을 씁니다").
struct WidgetFrame<Content: View>: View {
    @Environment(\.colorScheme) private var scheme
    let entry: SnapshotEntry
    let content: (WidgetContext) -> Content

    var body: some View {
        let context = WidgetContext(snapshot: entry.snapshot, now: entry.date, scheme: scheme)
        content(context)
            .padding(14)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .containerBackground(for: .widget) { context.theme.fill }
            .widgetURL(WidgetLink.day(context.today))
    }
}

/// The gallery names in the app's language, which the snapshot knows and the Mac does not.
private var galleryStrings: GlanceText { GlanceText(GlanceStore.shared?.load()) }

struct DaynoteTodayWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "DaynoteToday", provider: SnapshotProvider()) { entry in
            WidgetFrame(entry: entry) { TodayWidgetView(context: $0) }
        }
        .configurationDisplayName(galleryStrings.today)
        .description(galleryStrings.todayDescription)
        .supportedFamilies([.systemMedium])
        .contentMarginsDisabled()
    }
}

struct DaynoteTodosWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "DaynoteTodos", provider: SnapshotProvider()) { entry in
            WidgetFrame(entry: entry) { TodosWidgetView(context: $0) }
        }
        .configurationDisplayName(galleryStrings.todos)
        .description(galleryStrings.todosDescription)
        .supportedFamilies([.systemSmall])
        .contentMarginsDisabled()
    }
}

struct DaynoteUpNextWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "DaynoteUpNext", provider: SnapshotProvider()) { entry in
            WidgetFrame(entry: entry) { UpNextWidgetView(context: $0) }
        }
        .configurationDisplayName(galleryStrings.upNext)
        .description(galleryStrings.upNextDescription)
        .supportedFamilies([.systemSmall])
        .contentMarginsDisabled()
    }
}

struct DaynoteWeekWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "DaynoteWeek", provider: SnapshotProvider()) { entry in
            WidgetFrame(entry: entry) { WeekWidgetView(context: $0) }
        }
        .configurationDisplayName(galleryStrings.thisWeek)
        .description(galleryStrings.weekDescription)
        .supportedFamilies([.systemLarge])
        .contentMarginsDisabled()
    }
}
