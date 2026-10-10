import Foundation
import StoreKit
import UIKit
import WidgetKit
#if canImport(ActivityKit)
import ActivityKit
#endif

// The .NET app's way into three Swift-only APIs (docs/APPLE_EXTENSIONS.md §6).
//
// WidgetKit's WidgetCenter and ActivityKit have no Objective-C surface, so .NET for iOS has no
// binding for either; nor has StoreKit 2's subscription sheet. This framework is linked into the
// app and exports plain C functions, which the app calls with [DllImport("__Internal")]. Everything
// else the app needs — the App Group folder, WatchConnectivity — it reaches through its own bindings.

/// Asks WidgetKit to redraw every Daynote widget and control from the snapshot just written.
@_cdecl("daynote_glance_reload")
public func daynoteGlanceReload() {
    WidgetCenter.shared.reloadAllTimelines()
    if #available(iOS 18.0, *) {
        ControlCenter.shared.reloadAllControls()
    }
}

/// Starts, updates or ends the event Live Activity to match the snapshot (design §08).
///
/// Local only: there is no push token, so this runs whenever the app writes a snapshot or comes
/// back to the foreground. An activity therefore starts the first time the app runs inside an
/// event's fifteen-minute lead, not on the minute; the system's own timer views keep it counting
/// after that without the app.
@_cdecl("daynote_glance_sync_activity")
public func daynoteGlanceSyncActivity() {
    #if canImport(ActivityKit)
    guard #available(iOS 16.2, *) else { return }
    Task { await EventActivityDriver.sync(now: Date()) }
    #endif
}

/// Shows the App Store's own sheet for managing this app's subscriptions, over the app.
///
/// Unlike the account web page, the sheet lists a TestFlight or sandbox subscription too, and it
/// lets an upgrade, a downgrade or a cancellation happen without leaving the app. Returns 0 when
/// there is no active window scene to show it in, so the caller can open the web page instead.
@_cdecl("daynote_store_manage_subscriptions")
public func daynoteStoreManageSubscriptions() -> Int32 {
    let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
    guard let scene = scenes.first(where: { $0.activationState == .foregroundActive }) ?? scenes.first else {
        return 0
    }
    Task { @MainActor in
        try? await AppStore.showManageSubscriptions(in: scene)
    }
    return 1
}

#if canImport(ActivityKit)
@available(iOS 16.2, *)
enum EventActivityDriver {
    static func sync(now: Date) async {
        guard ActivityAuthorizationInfo().areActivitiesEnabled else { return }
        let snapshot = GlanceStore.shared?.load()
        let dismissed = UserDefaults(suiteName: GlanceStore.appGroup)?.string(forKey: EventActivity.dismissedKey)
        let due = snapshot.flatMap { $0.locked ? nil : EventActivity.due(in: $0, at: now) }
            .flatMap { $0.event.id == dismissed ? nil : $0 }

        // Anything that is not the due event ends: it is over, it was moved, or another one began.
        for activity in Activity<EventActivityAttributes>.activities where activity.attributes.eventId != due?.event.id {
            await activity.end(nil, dismissalPolicy: .immediate)
        }

        guard let due, let snapshot else { return }
        let state = EventActivityAttributes.ContentState(title: due.event.title, start: due.start, end: due.end)
        // Stale at the start, so the system redraws it as "진행 중" without the app; after that, at the end.
        let content = ActivityContent(state: state, staleDate: now < due.start ? due.start : due.end)

        if let running = Activity<EventActivityAttributes>.activities.first(where: { $0.attributes.eventId == due.event.id }) {
            await running.update(content)
            return
        }

        let today = LocalDay(now)
        let link = due.event.noteId.map { "daynote://note?date=\(due.event.noteDate ?? today.iso)&id=\($0)" }
            ?? "daynote://day?date=\(today.iso)"
        let attributes = EventActivityAttributes(
            eventId: due.event.id,
            listName: snapshot.list(due.event.listId)?.name ?? "",
            english: snapshot.isEnglish,
            noteLink: link)
        _ = try? Activity.request(attributes: attributes, content: content, pushType: nil)
    }
}
#endif
