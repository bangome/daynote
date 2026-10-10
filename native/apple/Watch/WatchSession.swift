import Foundation
import WatchConnectivity
import WidgetKit

/// The watch's half of the relay to the phone (docs/APPLE_EXTENSIONS.md §5).
///
/// In: the phone's snapshot, as its application context — the latest one replaces any not yet
/// delivered, which is exactly right for a "this is the day now" message. It is kept in the
/// watch's own App Group container, where the complications read it too.
///
/// Out: each completion, delete or capture as a `transferUserInfo`, which the system queues and delivers
/// in order even when the phone is out of reach, and which the phone writes into its action
/// queue for the app to carry out.
@MainActor
final class WatchSession: NSObject, WCSessionDelegate {
    static let snapshotKey = "snapshot"
    static let actionKey = "glanceAction"

    private weak var store: WatchStore?

    func start(store: WatchStore) {
        self.store = store
        if let saved = GlanceStore.shared?.load() {
            store.receive(saved)
        }
        store.send = { action in
            guard let data = try? JSONEncoder().encode(action), let json = String(data: data, encoding: .utf8) else { return }
            WCSession.default.transferUserInfo([WatchSession.actionKey: json])
        }
        store.persist = { snapshot in
            try? GlanceStore.shared?.save(snapshot)
            WidgetCenter.shared.reloadAllTimelines()
        }

        guard WCSession.isSupported() else { return }
        WCSession.default.delegate = self
        WCSession.default.activate()
    }

    nonisolated func session(_ session: WCSession, activationDidCompleteWith state: WCSessionActivationState, error: Error?) {
        let context = session.receivedApplicationContext
        Task { @MainActor in self.apply(context) }
    }

    nonisolated func session(_ session: WCSession, didReceiveApplicationContext context: [String: Any]) {
        Task { @MainActor in self.apply(context) }
    }

    private func apply(_ context: [String: Any]) {
        guard let json = context[WatchSession.snapshotKey] as? String, let data = json.data(using: .utf8),
              let snapshot = try? JSONDecoder().decode(GlanceSnapshot.self, from: data) else { return }
        try? GlanceStore.shared?.save(snapshotData: data)
        store?.receive(snapshot)
        WidgetCenter.shared.reloadAllTimelines()
    }
}
