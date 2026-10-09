import SwiftUI

/// The watch app's one screen (design §01): the next event, the day's to-dos, and 기록 at the
/// bottom. The whole row is the tap target and a tap completes it.
struct TodayView<Capture: View>: View {
    @ObservedObject var store: WatchStore
    /// Pins the clock in the header for a render.
    var clock: String?
    /// The 기록 button: the app wraps `CaptureLabel` in a dictation link, a render draws it bare.
    @ViewBuilder var capture: () -> Capture

    private let theme = GlanceTheme.watch

    var body: some View {
        // A render draws the first screenful; ImageRenderer leaves a ScrollView blank.
        #if DAYNOTE_RENDER_TESTS
        content.fixedSize(horizontal: false, vertical: true).frame(maxHeight: .infinity, alignment: .top).clipped().background(Color.black)
        #else
        ScrollView { content }.background(Color.black)
        #endif
    }

    private var content: some View {
            VStack(alignment: .leading, spacing: 5) {
                Header(title: store.text.today, clock: clock, theme: theme)
                if let snapshot = store.snapshot, !snapshot.locked {
                    Text("\(store.text.mediumDate(store.today)) · \(store.text.left(store.remaining))")
                        .font(.system(size: 12)).foregroundStyle(theme.secondary)
                        .padding(.horizontal, 8).padding(.bottom, 4)

                    if let day = store.addedElsewhere {
                        AddedElsewhereBanner(day: day, store: store)
                    }
                    if let made = store.justMade {
                        JustMadeRow(made: made, text: store.text)
                    }
                    if let event = store.nextEvent {
                        EventCard(event: event, store: store)
                    }
                    ForEach(store.todos, id: \.rowKey) { todo in
                        WatchTodoRow(todo: todo, store: store)
                            .transition(.asymmetric(insertion: .opacity, removal: .move(edge: .leading).combined(with: .opacity)))
                    }
                    if store.todos.isEmpty {
                        Text(store.text.nothingToday).font(.system(size: 14, weight: .semibold)).foregroundStyle(theme.secondary)
                            .padding(.horizontal, 8).padding(.vertical, 10)
                    }
                    capture().padding(.top, 4)
                } else {
                    Text(store.snapshot?.locked == true ? store.text.locked : store.text.openOnPhone)
                        .font(.system(size: 14, weight: .semibold)).foregroundStyle(theme.secondary)
                        .padding(8)
                }
            }
            .padding(.horizontal, 2)
    }
}

struct Header: View {
    var title: String
    var clock: String?
    var theme: GlanceTheme

    var body: some View {
        HStack {
            Text(title).font(.system(size: 14, weight: .bold)).foregroundStyle(theme.accent)
            Spacer()
            if let clock {
                Text(clock).font(.system(size: 14, weight: .semibold)).foregroundStyle(theme.text)
            }
        }
        .padding(.horizontal, 10)
        .frame(height: 30)
    }
}

struct EventCard: View {
    var event: GlanceEvent
    @ObservedObject var store: WatchStore

    var body: some View {
        let theme = GlanceTheme.watch
        HStack(spacing: 9) {
            RoundedRectangle(cornerRadius: 2).fill(theme.ring(for: event.listId, in: store.snapshot)).frame(width: 3.5)
            VStack(alignment: .leading, spacing: 1) {
                Text(store.text.range(event.start, event.end)).font(.system(size: 11.5).monospacedDigit()).foregroundStyle(theme.secondary)
                Text(event.title).font(.system(size: 15, weight: .bold)).foregroundStyle(theme.text).lineLimit(2)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12).padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(theme.chip))
    }
}

/// One to-do: ring, title, time. A tap fills the ring with a haptic and strikes the title; 1.5
/// seconds later it is sent and leaves, and a second tap inside that window takes it back.
struct WatchTodoRow: View {
    var todo: GlanceTodo
    @ObservedObject var store: WatchStore

    var body: some View {
        let theme = GlanceTheme.watch
        let checking = store.checking[todo.rowKey] != nil
        Button {
            withAnimation(.spring(response: 0.35, dampingFraction: 0.7)) { store.tap(todo) }
        } label: {
            HStack(spacing: 9) {
                CheckRing(color: store.ring(todo), done: checking, size: 22, lineWidth: 2.5)
                VStack(alignment: .leading, spacing: 1) {
                    Text(todo.title)
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(checking ? theme.muted : theme.text)
                        .strikethrough(checking, color: theme.muted)
                        .lineLimit(2)
                        .multilineTextAlignment(.leading)
                    if let time = todo.time {
                        Text(time).font(.system(size: 11.5).monospacedDigit())
                            .foregroundStyle(!checking && store.isOverdue(todo) ? theme.overdue : theme.secondary)
                    }
                }
                Spacer(minLength: 0)
                if todo.repeats {
                    Image(systemName: "arrow.clockwise").font(.system(size: 11, weight: .semibold)).foregroundStyle(theme.muted)
                }
            }
            .padding(.horizontal, 11)
            .frame(minHeight: 44)
            .background(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(checking ? theme.accent.opacity(0.22) : theme.chip))
        }
        .buttonStyle(.plain)
    }
}

/// The row a capture just made, warm for a moment as the app's M2 confirmation is.
struct JustMadeRow: View {
    var made: WatchStore.JustMade
    var text: GlanceText

    var body: some View {
        let theme = GlanceTheme.watch
        HStack(spacing: 9) {
            CheckRing(color: Color(hex: "#a3a9d6"), done: false, size: 22, lineWidth: 2.5)
            VStack(alignment: .leading, spacing: 1) {
                Text(made.title).font(.system(size: 14, weight: .semibold)).foregroundStyle(theme.text).lineLimit(2)
                Text([made.time, text.justNow].compactMap { $0 }.joined(separator: " · "))
                    .font(.system(size: 11.5)).foregroundStyle(theme.secondary)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 11)
        .frame(minHeight: 44)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color(hex: "#2a1b10")))
    }
}

/// "10/8에 추가됨 · 보기" — the one line that says where a capture for another day went.
struct AddedElsewhereBanner: View {
    var day: LocalDay
    @ObservedObject var store: WatchStore

    var body: some View {
        let theme = GlanceTheme.watch
        Button {
            store.addedElsewhere = nil
        } label: {
            HStack(spacing: 0) {
                Text(store.text.addedTo(day) + " · ").foregroundStyle(theme.text)
                Text(store.text.view).foregroundStyle(theme.accent)
                Spacer(minLength: 0)
            }
            .font(.system(size: 13, weight: .bold))
            .padding(.horizontal, 12).frame(minHeight: 34)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Color(hex: "#2a1b10")))
        }
        .buttonStyle(.plain)
    }
}

/// "🎙 기록" at the foot of the list.
struct CaptureLabel: View {
    var title: String

    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "mic").foregroundStyle(GlanceTheme.watch.accent)
            Text(title).foregroundStyle(.white)
        }
        .font(.system(size: 15, weight: .bold))
        .frame(maxWidth: .infinity, minHeight: 44)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color(hex: "#2a2b31")))
    }
}
