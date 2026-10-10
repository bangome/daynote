import AppIntents
import SwiftUI
import WidgetKit

@main
struct DaynoteWidgetsBundle: WidgetBundle {
    var body: some Widget {
        TodayWidget()
        TodosWidget()
        NextEventWidget()
        QuickCaptureWidget()
        WeekWidget()
        FavoritesWidget()
        LockScreenWidget()
        RemainingWidget()
        CaptureAccessoryWidget()
        EventLiveActivity()
        if #available(iOSApplicationExtension 18.0, *) {
            NewNoteControl()
            AtCaptureControl()
        }
    }
}

/// The light or dark palette, the widget's tap target, and the container background.
private struct GlanceWidgetView<Content: View>: View {
    var entry: GlanceEntry
    @ViewBuilder var content: (GlanceTheme) -> Content
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let theme = GlanceTheme.of(scheme)
        Group {
            if entry.snapshot == nil || entry.snapshot?.locked == true {
                WidgetNotice(entry: entry, theme: theme)
            } else {
                content(theme)
            }
        }
        .widgetURL(GlanceLinks.day(entry.today))
        .containerBackground(for: .widget) { theme.background }
    }
}

// MARK: - Home screen

/// Medium "오늘" and large "하루 전체" (W6, W10): the same day at two sizes.
struct TodayWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.today", provider: GlanceProvider()) { entry in
            TodayWidgetView(entry: entry)
        }
        .configurationDisplayName(Text("widget.today.name"))
        .description(Text("widget.today.description"))
        .supportedFamilies([.systemMedium, .systemLarge])
        .contentMarginsDisabled()
    }
}

struct TodayWidgetView: View {
    var entry: GlanceEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        GlanceWidgetView(entry: entry) { theme in
            Group {
                if family == .systemLarge {
                    LargeDayView(entry: entry, theme: theme)
                } else {
                    MediumTodayView(entry: entry, theme: theme)
                }
            }
            .padding(15)
        }
    }
}

/// Small "할 일" (W4), which is also StandBy's right half (W14).
struct TodosWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.todos", provider: GlanceProvider()) { entry in
            GlanceWidgetView(entry: entry) { theme in SmallTodosView(entry: entry, theme: theme).padding(15) }
        }
        .configurationDisplayName(Text("widget.todos.name"))
        .description(Text("widget.todos.description"))
        .supportedFamilies([.systemSmall])
        .contentMarginsDisabled()
    }
}

/// Small "다음 일정" (W3), which is also StandBy's left half (W14).
struct NextEventWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.next-event", provider: GlanceProvider()) { entry in
            GlanceWidgetView(entry: entry) { theme in SmallNextEventView(entry: entry, theme: theme).padding(15) }
        }
        .configurationDisplayName(Text("widget.next.name"))
        .description(Text("widget.next.description"))
        .supportedFamilies([.systemSmall])
        .contentMarginsDisabled()
    }
}

/// Small "빠른 기록" (W5).
struct QuickCaptureWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.capture", provider: GlanceProvider()) { entry in
            GlanceWidgetView(entry: entry) { theme in SmallQuickCaptureView(entry: entry, theme: theme).padding(15) }
        }
        .configurationDisplayName(Text("widget.capture.name"))
        .description(Text("widget.capture.description"))
        .supportedFamilies([.systemSmall])
        .contentMarginsDisabled()
    }
}

/// Medium "이번 주" (W7).
struct WeekWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.week", provider: GlanceProvider()) { entry in
            GlanceWidgetView(entry: entry) { theme in MediumWeekView(entry: entry, theme: theme).padding(15) }
        }
        .configurationDisplayName(Text("widget.week.name"))
        .description(Text("widget.week.description"))
        .supportedFamilies([.systemMedium])
        .contentMarginsDisabled()
    }
}

/// Medium "즐겨찾기" (W8).
struct FavoritesWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.favorites", provider: GlanceProvider()) { entry in
            GlanceWidgetView(entry: entry) { theme in MediumFavoritesView(entry: entry, theme: theme).padding(15) }
        }
        .configurationDisplayName(Text("widget.favorites.name"))
        .description(Text("widget.favorites.description"))
        .supportedFamilies([.systemMedium])
        .contentMarginsDisabled()
    }
}

// MARK: - Lock Screen

/// Inline next event and rectangular next to-do (W12).
struct LockScreenWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.lock", provider: GlanceProvider()) { entry in
            LockScreenWidgetView(entry: entry).containerBackground(for: .widget) { Color.clear }
        }
        .configurationDisplayName(Text("widget.lock.name"))
        .description(Text("widget.lock.description"))
        .supportedFamilies([.accessoryInline, .accessoryRectangular])
    }
}

struct LockScreenWidgetView: View {
    var entry: GlanceEntry
    @Environment(\.widgetFamily) private var family

    var body: some View {
        if family == .accessoryInline {
            InlineNextEventView(entry: entry)
        } else {
            RectangularNextTodoView(entry: entry)
        }
    }
}

/// Circular: what is left, as a ring (W12).
struct RemainingWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.remaining", provider: GlanceProvider()) { entry in
            CircularRemainingView(entry: entry).containerBackground(for: .widget) { Color.clear }
        }
        .configurationDisplayName(Text("widget.remaining.name"))
        .description(Text("widget.remaining.description"))
        .supportedFamilies([.accessoryCircular])
    }
}

/// Circular: "+ 기록" (W12).
struct CaptureAccessoryWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "cc.arachat.daynote.capture-accessory", provider: GlanceProvider()) { entry in
            CircularCaptureView(entry: entry).containerBackground(for: .widget) { Color.clear }
        }
        .configurationDisplayName(Text("widget.capture.name"))
        .description(Text("widget.captureAccessory.description"))
        .supportedFamilies([.accessoryCircular])
    }
}

// MARK: - Control Center (design §07)

@available(iOS 18.0, *)
struct NewNoteControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "cc.arachat.daynote.control.note") {
            ControlWidgetButton(action: OpenCaptureIntent(withAt: false)) {
                Label {
                    Text(GlanceText(GlanceStore.shared?.load()).todayNote)
                } icon: {
                    Image(systemName: "plus")
                }
            }
        }
        .displayName(LocalizedStringResource("control.note.name"))
    }
}

@available(iOS 18.0, *)
struct AtCaptureControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "cc.arachat.daynote.control.at") {
            ControlWidgetButton(action: OpenCaptureIntent(withAt: true)) {
                Label {
                    Text(GlanceText(GlanceStore.shared?.load()).todoEvent)
                } icon: {
                    Image(systemName: "checklist")
                }
            }
        }
        .displayName(LocalizedStringResource("control.at.name"))
    }
}
