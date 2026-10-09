import SwiftUI
import WatchKit
import WidgetKit

@main
struct DaynoteWatchApp: App {
    @StateObject private var store = WatchStore()
    @State private var session = WatchSession()
    @State private var spoken: String?

    var body: some Scene {
        WindowGroup {
            NavigationStack {
                TodayView(store: store) {
                    // The system's own input: dictation first, Scribble and the keyboard behind it.
                    TextFieldLink(prompt: Text(store.text.capture)) {
                        CaptureLabel(title: store.text.capture)
                    } onSubmit: { sentence in
                        if !sentence.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { spoken = sentence }
                    }
                    .buttonStyle(.plain)
                }
                .navigationDestination(item: $spoken) { sentence in
                    ReadbackView(
                        sentence: sentence,
                        readback: CaptureReadback.read(sentence, now: LocalMoment(Date()), text: store.text),
                        store: store,
                        done: { spoken = nil })
                }
            }
            .task {
                store.haptic = { WKInterfaceDevice.current().play(.success) }
                session.start(store: store)
            }
        }
    }
}
