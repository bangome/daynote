import AppIntents
import SwiftUI
import WidgetKit

// StandBy (design §06) and the Lock Screen (§05). Both are drawn by the system in its own
// colours — white on the Lock Screen, red at night in StandBy — so list colours are left out and
// everything is primary or secondary.

/// StandBy, left half: the next event, large enough to read from the nightstand.
struct StandByNextEventView: View {
    var event: GlanceEvent
    var entry: GlanceEntry

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(entry.text.upNext).font(.system(size: 14, weight: .semibold)).foregroundStyle(.secondary)
            Spacer(minLength: 0)
            Text(event.start ?? entry.text.range(nil, nil))
                .font(.system(size: 44, weight: .heavy).monospacedDigit()).tracking(-1).minimumScaleFactor(0.6)
            Text(event.title).font(.system(size: 20, weight: .heavy)).lineLimit(1).minimumScaleFactor(0.7).privacySensitive()
            if let minutes = entry.minutesUntil(event) {
                Text(entry.text.until(minutes: minutes)).font(.system(size: 13)).foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    }
}

/// StandBy, right half: the day's to-dos, four of them.
struct StandByTodosView: View {
    var entry: GlanceEntry
    @Environment(\.widgetRenderingMode) private var mode

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(entry.text.todosLeft(entry.remaining)).font(.system(size: 14, weight: .semibold)).foregroundStyle(.secondary)
            if entry.todos.isEmpty {
                Text(entry.text.nothingToday).font(.system(size: 16, weight: .semibold))
            }
            ForEach(entry.todos.prefix(4), id: \.rowKey) { todo in
                HStack(spacing: 10) {
                    Button(intent: CompleteTodoIntent(todo: todo, day: entry.today)) {
                        CheckRing(
                            color: mode == .fullColor ? GlanceTheme.dark.ring(for: todo.listId, in: entry.snapshot) : .primary,
                            done: todo.done, size: 22, lineWidth: 2.5)
                    }
                    .buttonStyle(.plain)
                    Text(todo.title).font(.system(size: 17, weight: .semibold)).lineLimit(1).privacySensitive()
                    Spacer(minLength: 4)
                    if let time = todo.time {
                        Text(time).font(.system(size: 14).monospacedDigit()).foregroundStyle(.secondary)
                    }
                }
                .opacity(todo.done ? 0.45 : 1)
            }
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

/// Inline, beside the Lock Screen date: "○ 16:00 디자인 리뷰".
struct InlineNextEventView: View {
    var entry: GlanceEntry

    var body: some View {
        if let event = entry.nextEvent {
            Label {
                Text([event.start, event.title].compactMap { $0 }.joined(separator: " "))
            } icon: {
                Image(systemName: "circle")
            }
            .privacySensitive()
        } else {
            Text(entry.text.left(entry.remaining))
        }
    }
}

/// Circular: what is left today, and how much of the day is done as a ring.
struct CircularRemainingView: View {
    var entry: GlanceEntry

    var body: some View {
        let total = max(entry.day.todos.count, 1)
        Gauge(value: Double(total - entry.remaining), in: 0...Double(total)) {
            Text(entry.text.leftWord)
        } currentValueLabel: {
            VStack(spacing: -2) {
                Text("\(entry.remaining)").font(.system(size: 20, weight: .heavy).monospacedDigit())
                Text(entry.text.leftWord).font(.system(size: 9, weight: .semibold))
            }
        }
        .gaugeStyle(.accessoryCircularCapacity)
        .widgetURL(GlanceLinks.todos)
    }
}

/// Circular: straight to the to-do sheet.
struct CircularCaptureView: View {
    var entry: GlanceEntry

    var body: some View {
        ZStack {
            AccessoryWidgetBackground()
            VStack(spacing: 0) {
                Image(systemName: "plus").font(.system(size: 18, weight: .semibold))
                Text(entry.text.captureShort).font(.system(size: 9, weight: .semibold))
            }
        }
        .widgetURL(GlanceLinks.capture(withAt: true))
    }
}

/// Rectangular: the next to-do, and how many come after it.
struct RectangularNextTodoView: View {
    var entry: GlanceEntry

    var body: some View {
        let open = entry.day.open
        if let next = open.first {
            VStack(alignment: .leading, spacing: 1) {
                Text(entry.text.nextTodo(at: next.time)).font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary)
                Text(next.title).font(.system(size: 15, weight: .bold)).lineLimit(1).privacySensitive()
                if open.count > 1 {
                    Text(entry.text.more(open.count - 1)).font(.system(size: 11)).foregroundStyle(.secondary)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .widgetURL(GlanceLinks.todos)
        } else {
            Text(entry.text.nothingToday).font(.system(size: 13, weight: .semibold))
        }
    }
}
