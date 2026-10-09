import AppIntents
import SwiftUI
import WidgetKit

// The home screen widgets (design §01–§04): small, medium and large, light and dark.
// Sizes and type follow the design's 170 / 364×170 / 364×382 points; the system scales them to
// the device.

struct WidgetHeader: View {
    var title: String
    var trailing: String?
    var theme: GlanceTheme

    var body: some View {
        HStack(spacing: 6) {
            DaynoteMark(size: 16)
            Text(title).font(.system(size: 12, weight: .heavy)).foregroundStyle(theme.accent).lineLimit(1)
            Spacer(minLength: 4)
            if let trailing {
                Text(trailing).font(.system(size: 11.5, weight: .semibold)).foregroundStyle(theme.muted).lineLimit(1)
            }
        }
        .padding(.bottom, 8)
    }
}

/// One to-do: the ring is the check (an App Intent), the rest of the row opens its day.
struct TodoRow: View {
    var todo: GlanceTodo
    var entry: GlanceEntry
    var theme: GlanceTheme
    var showsTime = true
    var fontSize: CGFloat = 13.5
    var ringSize: CGFloat = 18
    var minHeight: CGFloat = 33

    var body: some View {
        HStack(spacing: 9) {
            Button(intent: CompleteTodoIntent(todo: todo, day: entry.today)) {
                CheckRing(color: theme.ring(for: todo.listId, in: entry.snapshot), done: todo.done, size: ringSize)
                    .contentTransition(.symbolEffect)
            }
            .buttonStyle(.plain)

            Text(todo.title)
                .font(.system(size: fontSize, weight: .medium))
                .foregroundStyle(theme.text)
                .strikethrough(todo.done, color: theme.muted)
                .lineLimit(1)
                .truncationMode(.tail)
                .privacySensitive()
            if todo.repeats {
                Image(systemName: "arrow.clockwise").font(.system(size: fontSize * 0.7, weight: .semibold)).foregroundStyle(theme.muted)
            }
            Spacer(minLength: 4)
            if showsTime, let time = todo.time {
                Text(time)
                    .font(.system(size: 11.5, weight: .bold).monospacedDigit())
                    .foregroundStyle(entry.isOverdue(todo) ? theme.overdue : theme.secondary)
            }
        }
        .frame(minHeight: minHeight)
        // M8: the row dims once checked and leaves on the next timeline entry; nothing re-sorts.
        .opacity(todo.done ? 0.45 : 1)
        .animation(.easeOut(duration: 0.32), value: todo.done)
    }
}

/// The coloured bar and the title and range of an event.
struct EventBlock: View {
    var event: GlanceEvent
    var entry: GlanceEntry
    var theme: GlanceTheme
    var titleSize: CGFloat = 15
    var detail: String?

    var body: some View {
        HStack(alignment: .top, spacing: 9) {
            RoundedRectangle(cornerRadius: 2).fill(theme.ring(for: event.listId, in: entry.snapshot)).frame(width: 3.5)
            VStack(alignment: .leading, spacing: 3) {
                Text(event.title).font(.system(size: titleSize, weight: .heavy)).tracking(-0.3).foregroundStyle(theme.text)
                    .lineLimit(1).privacySensitive()
                Text(detail ?? entry.text.range(event.start, event.end))
                    .font(.system(size: 12.5).monospacedDigit()).foregroundStyle(theme.secondary).lineLimit(1)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
    }
}

/// What a widget shows when it has nothing to draw from: the app has never run, or the account
/// is locked.
struct WidgetNotice: View {
    var entry: GlanceEntry
    var theme: GlanceTheme

    var body: some View {
        VStack(spacing: 8) {
            DaynoteMark(size: 28)
            Text(entry.snapshot?.locked == true ? entry.text.locked : entry.text.openAppOnce)
                .font(.system(size: 13, weight: .semibold)).foregroundStyle(theme.secondary).multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

// MARK: - Small

struct SmallNextEventView: View {
    var entry: GlanceEntry
    var theme: GlanceTheme
    @Environment(\.showsWidgetContainerBackground) private var hasBackground

    var body: some View {
        if let event = entry.nextEvent {
            if hasBackground {
                VStack(alignment: .leading, spacing: 0) {
                    WidgetHeader(title: entry.text.upNext, trailing: nil, theme: theme)
                    Spacer(minLength: 0)
                    EventBlock(event: event, entry: entry, theme: theme)
                    if let minutes = entry.minutesUntil(event) {
                        Text(entry.text.until(minutes: minutes))
                            .font(.system(size: 11.5, weight: .bold)).foregroundStyle(theme.accent).padding(.top, 8)
                    }
                }
            } else {
                StandByNextEventView(event: event, entry: entry)
            }
        } else {
            SmallTodosView(entry: entry, theme: theme)
        }
    }
}

struct SmallTodosView: View {
    var entry: GlanceEntry
    var theme: GlanceTheme
    @Environment(\.showsWidgetContainerBackground) private var hasBackground

    var body: some View {
        if !hasBackground {
            StandByTodosView(entry: entry)
        } else if entry.todos.isEmpty {
            VStack(alignment: .leading, spacing: 0) {
                WidgetHeader(title: entry.text.todos, trailing: nil, theme: theme)
                Text(entry.text.nothingToday).font(.system(size: 13, weight: .medium)).foregroundStyle(theme.secondary)
                Spacer(minLength: 0)
                CaptureButtons(entry: entry, theme: theme, vertical: false, height: 36)
            }
        } else {
            VStack(alignment: .leading, spacing: 2) {
                WidgetHeader(title: entry.text.todos, trailing: entry.text.left(entry.remaining), theme: theme)
                ForEach(entry.todos.prefix(3), id: \.rowKey) { todo in
                    TodoRow(todo: todo, entry: entry, theme: theme, showsTime: false, minHeight: 36)
                }
                Spacer(minLength: 0)
            }
        }
    }
}

struct SmallQuickCaptureView: View {
    var entry: GlanceEntry
    var theme: GlanceTheme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(title: entry.text.quickCapture, trailing: nil, theme: theme)
            Spacer(minLength: 0)
            CaptureButtons(entry: entry, theme: theme, vertical: true, height: 44)
        }
    }
}

/// "+ 새 노트" and "@ 할 일·일정", stacked or side by side.
struct CaptureButtons: View {
    var entry: GlanceEntry
    var theme: GlanceTheme
    var vertical: Bool
    var height: CGFloat
    var atLabel: String?

    var body: some View {
        let layout = vertical ? AnyLayout(VStackLayout(spacing: 7)) : AnyLayout(HStackLayout(spacing: 8))
        layout {
            Link(destination: GlanceLinks.capture(withAt: false)) {
                pill(entry.text.newNote, fill: theme.primaryButton, ink: theme.primaryButtonText)
            }
            Link(destination: GlanceLinks.capture(withAt: true)) {
                pill(atLabel ?? (vertical ? entry.text.atTodoEvent : entry.text.atTodo), fill: theme.chip, ink: theme.text)
            }
        }
    }

    private func pill(_ label: String, fill: Color, ink: Color) -> some View {
        Text(label)
            .font(.system(size: vertical ? 14 : 13, weight: .heavy))
            .foregroundStyle(ink)
            .lineLimit(1)
            .minimumScaleFactor(0.8)
            .frame(maxWidth: .infinity, minHeight: height, maxHeight: height)
            .background(RoundedRectangle(cornerRadius: height * 0.385, style: .continuous).fill(fill))
    }
}

// MARK: - Medium

struct MediumTodayView: View {
    var entry: GlanceEntry
    var theme: GlanceTheme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(title: entry.text.longDate(entry.today), trailing: entry.text.left(entry.remaining), theme: theme)
            GeometryReader { space in
                // The design's 1.35fr : 1fr, the event column only when there is an event (§09).
                let event = entry.nextEvent
                let left = event == nil ? space.size.width : (space.size.width - 14) * 1.35 / 2.35
                HStack(alignment: .top, spacing: 14) {
                    VStack(alignment: .leading, spacing: 2) {
                        if entry.todos.isEmpty {
                            Text(entry.text.nothingToday).font(.system(size: 13, weight: .medium)).foregroundStyle(theme.secondary)
                            Spacer(minLength: 0)
                            CaptureButtons(entry: entry, theme: theme, vertical: false, height: 34)
                        } else {
                            ForEach(entry.todos.prefix(3), id: \.rowKey) { todo in
                                TodoRow(todo: todo, entry: entry, theme: theme)
                            }
                            Spacer(minLength: 0)
                        }
                    }
                    .frame(width: left, alignment: .leading)

                    if let event {
                        HStack(spacing: 0) {
                            Rectangle().fill(theme.divider).frame(width: 1)
                            VStack(alignment: .leading, spacing: 0) {
                                Text(entry.text.upNext).font(.system(size: 11, weight: .bold)).foregroundStyle(theme.muted).padding(.bottom, 6)
                                EventBlock(event: event, entry: entry, theme: theme)
                                Spacer(minLength: 0)
                                if entry.upcomingEvents.count > 1 {
                                    Text(entry.text.moreToday(entry.upcomingEvents.count - 1))
                                        .font(.system(size: 11.5)).foregroundStyle(theme.muted)
                                }
                            }
                            .padding(.leading, 14)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            }
        }
    }
}

struct WeekStrip: View {
    var entry: GlanceEntry
    var theme: GlanceTheme

    var body: some View {
        HStack(spacing: 2) {
            ForEach(entry.snapshot?.week ?? [], id: \.date) { day in
                let date = LocalDay(iso: day.date) ?? entry.today
                let selected = date == entry.today
                Link(destination: GlanceLinks.day(date)) {
                    VStack(spacing: 2) {
                        Text(entry.text.weekdayLetter(date.weekday))
                            .font(.system(size: 10))
                            .foregroundStyle(selected ? theme.selectedDayText : date.weekday == 0 ? theme.sunday : theme.muted)
                        Text("\(date.day)")
                            .font(.system(size: 14.5, weight: .heavy).monospacedDigit())
                            .foregroundStyle(selected ? theme.selectedDayText : theme.text)
                        HStack(spacing: 2) {
                            ForEach(0..<min(day.noteCount, 3), id: \.self) { _ in
                                Circle().fill(selected ? theme.selectedDayText : Color(hex: theme.dark ? "#ff9a52" : "#ee7f35"))
                                    .frame(width: 4, height: 4)
                            }
                        }
                        .frame(height: 4)
                    }
                    .frame(maxWidth: .infinity, minHeight: 48, maxHeight: 52)
                    .background(RoundedRectangle(cornerRadius: 11, style: .continuous).fill(selected ? theme.selectedDay : .clear))
                }
            }
        }
    }
}

struct MediumWeekView: View {
    var entry: GlanceEntry
    var theme: GlanceTheme

    private var noteTotal: Int { entry.snapshot?.week.reduce(0) { $0 + $1.noteCount } ?? 0 }
    private var todayTitles: [String] { entry.snapshot?.week.first { $0.date == entry.today.iso }?.titles ?? [] }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(title: entry.text.thisWeek, trailing: entry.text.notes(noteTotal), theme: theme)
            WeekStrip(entry: entry, theme: theme)
            Spacer(minLength: 6)
            Link(destination: GlanceLinks.day(entry.today)) {
                HStack(spacing: 8) {
                    Text(entry.text.today).font(.system(size: 12, weight: .heavy)).foregroundStyle(theme.accent)
                    Text(todayTitles.isEmpty ? "—" : todayTitles.joined(separator: " · "))
                        .font(.system(size: 13.5, weight: .bold)).foregroundStyle(theme.text).lineLimit(1).privacySensitive()
                    Spacer(minLength: 0)
                }
                .padding(.horizontal, 12)
                .frame(height: 38)
                .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(theme.chip))
            }
        }
    }
}

struct MediumFavoritesView: View {
    var entry: GlanceEntry
    var theme: GlanceTheme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(title: entry.text.favorites, trailing: nil, theme: theme)
            let favorites = entry.snapshot?.favorites.prefix(2) ?? []
            if favorites.isEmpty {
                Text(entry.text.noFavorites).font(.system(size: 13, weight: .medium)).foregroundStyle(theme.secondary)
                Spacer(minLength: 0)
            } else {
                HStack(spacing: 10) {
                    ForEach(Array(favorites), id: \.id) { favorite in
                        Link(destination: GlanceLinks.note(id: favorite.id, date: favorite.date, fallback: entry.today)) {
                            card(favorite)
                        }
                    }
                    if favorites.count == 1 { Color.clear.frame(maxWidth: .infinity) }
                }
            }
        }
    }

    private func card(_ favorite: GlanceFavorite) -> some View {
        let day = LocalDay(iso: favorite.date)
        return VStack(alignment: .leading, spacing: 4) {
            Text("★ " + (day == entry.today ? entry.text.today : day.map(entry.text.shortDate) ?? ""))
                .font(.system(size: 10.5, weight: .semibold)).foregroundStyle(theme.accent)
            Text(favorite.title).font(.system(size: 14, weight: .heavy)).foregroundStyle(theme.text).lineLimit(1)
            Text(favorite.preview).font(.system(size: 11.5)).foregroundStyle(theme.secondary).lineLimit(3)
                .multilineTextAlignment(.leading)
            Spacer(minLength: 0)
        }
        .privacySensitive()
        .padding(12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(theme.chip))
    }
}

// MARK: - Large

struct LargeDayView: View {
    var entry: GlanceEntry
    var theme: GlanceTheme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            WidgetHeader(title: entry.text.longDate(entry.today), trailing: nil, theme: theme)
            WeekStrip(entry: entry, theme: theme).padding(.bottom, 10)

            let events = Array(entry.upcomingEvents.prefix(2))
            if !events.isEmpty {
                label(entry.text.events)
                VStack(alignment: .leading, spacing: 8) {
                    ForEach(Array(events.enumerated()), id: \.element.id) { index, event in
                        Link(destination: GlanceLinks.note(id: event.noteId, date: event.noteDate, fallback: entry.today)) {
                            EventBlock(event: event, entry: entry, theme: theme, titleSize: 14, detail: detail(event, first: index == 0))
                        }
                    }
                }
                .padding(.bottom, 10)
            }

            label(entry.todos.isEmpty ? entry.text.todos : entry.text.todosLeft(entry.remaining))
            if entry.todos.isEmpty {
                Text(entry.text.nothingToday).font(.system(size: 13, weight: .medium)).foregroundStyle(theme.secondary)
            } else {
                ForEach(entry.todos.prefix(events.isEmpty ? 5 : 3), id: \.rowKey) { todo in
                    TodoRow(todo: todo, entry: entry, theme: theme, minHeight: 31)
                }
            }
            Spacer(minLength: 8)
            CaptureButtons(entry: entry, theme: theme, vertical: false, height: 36)
        }
    }

    private func label(_ text: String) -> some View {
        Text(text).font(.system(size: 11, weight: .semibold)).foregroundStyle(theme.muted).padding(.bottom, 4)
    }

    private func detail(_ event: GlanceEvent, first: Bool) -> String {
        let range = entry.text.range(event.start, event.end)
        guard first, let minutes = entry.minutesUntil(event), minutes > 0 else { return range }
        return "\(range) · \(entry.text.until(minutes: minutes))"
    }
}
