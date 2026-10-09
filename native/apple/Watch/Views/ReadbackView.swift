import SwiftUI

/// After the dictation (design §03): the title, then 할 일 / 일정 / 노트에 한 줄. The crown moves
/// the highlight and a tap makes the one tapped. With no date in the sentence only the note line
/// is offered — there is nothing to make a to-do of.
struct ReadbackView: View {
    var sentence: String
    var readback: CaptureReadback
    @ObservedObject var store: WatchStore
    var clock: String?
    /// Called once something has been made, to go back to the list.
    var done: () -> Void = {}

    @State private var selected = 0
    @State private var crown = 0.0

    private var kinds: [String] { readback.hasDate ? ["task", "event", "note"] : ["note"] }

    var body: some View {
        let theme = GlanceTheme.watch
        scroller {
            VStack(alignment: .leading, spacing: 5) {
                Header(title: store.text.capture, clock: clock, theme: theme)
                VStack(alignment: .leading, spacing: 1) {
                    Text(store.text.title).font(.system(size: 11)).foregroundStyle(theme.muted)
                    Text(readback.title).font(.system(size: 14.5, weight: .heavy)).foregroundStyle(theme.text)
                }
                .padding(.horizontal, 6).padding(.bottom, 4)

                ForEach(Array(kinds.enumerated()), id: \.element) { index, kind in
                    Button {
                        store.create(sentence, kind: kind, readback: readback)
                        done()
                    } label: {
                        choice(kind: kind, selected: index == selected)
                    }
                    .buttonStyle(.plain)
                }
            }
        }
        .background(Color.black)
        #if os(watchOS)
        .focusable()
        .digitalCrownRotation($crown, from: 0, through: Double(max(kinds.count - 1, 0)), by: 1, sensitivity: .low)
        .onChange(of: crown) { _, value in selected = Int(value.rounded()) }
        #endif
    }

    @ViewBuilder
    private func scroller<C: View>(@ViewBuilder _ content: () -> C) -> some View {
        #if DAYNOTE_RENDER_TESTS
        content().fixedSize(horizontal: false, vertical: true).frame(maxHeight: .infinity, alignment: .top).clipped()
        #else
        ScrollView { content() }
        #endif
    }

    private func choice(kind: String, selected: Bool) -> some View {
        let theme = GlanceTheme.watch
        let (label, detail): (String, String) = switch kind {
        case "task": (store.text.todo, readback.task ?? "")
        case "event": (store.text.event, readback.event ?? "")
        default: (store.text.noteLine, store.text.noteLineDetail)
        }
        return VStack(alignment: .leading, spacing: 1) {
            Text(label).font(.system(size: 11.5, weight: .heavy)).foregroundStyle(selected ? theme.accent : theme.secondary)
            detailText(detail, kind: kind)
                .font(.system(size: 13, weight: .semibold)).foregroundStyle(theme.text)
                .multilineTextAlignment(.leading)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 11).padding(.vertical, 8)
        .background(
            RoundedRectangle(cornerRadius: 15, style: .continuous)
                .fill(selected ? theme.accent.opacity(0.22) : theme.chip)
                .overlay(RoundedRectangle(cornerRadius: 15, style: .continuous).strokeBorder(selected ? theme.accent : .clear, lineWidth: 1.5)))
    }

    /// A8: when a passed time was read as tomorrow, "내일" is in the accent so the misreading shows.
    private func detailText(_ detail: String, kind: String) -> Text {
        let word = store.text.english ? "Tomorrow" : "내일"
        guard readback.rolledToTomorrow, kind != "note", detail.hasPrefix(word) else { return Text(detail) }
        return Text(word).foregroundColor(GlanceTheme.watch.accent) + Text(detail.dropFirst(word.count))
    }
}
