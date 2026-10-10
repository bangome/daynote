import Foundation

/// The shared folder: the snapshot the app writes and the queue the extensions write
/// (docs/APPLE_EXTENSIONS.md §3–4). Mirrors `Daynote.App.Glance.GlanceFolder`.
///
/// Nothing is written in place. Both sides write to a temporary name and rename it over the real
/// one, so a reader sees the old file or the new one and never half of each, and an action is one
/// file per action so a widget appending while the app drains cannot lose one.
public struct GlanceStore: Sendable {
    /// The App Group every Daynote target on a device shares.
    ///
    /// On macOS the team-prefixed form, which a Mac can use without the group being registered
    /// (and which a Developer ID build has to, having no profile to grant a registered one); the
    /// Mac widgets may name it in their Info.plist as `DaynoteAppGroup`.
    public static let appGroup: String = {
        #if os(macOS)
        return Bundle.main.object(forInfoDictionaryKey: "DaynoteAppGroup") as? String ?? "4T8C76SP99.group.cc.arachat.daynote"
        #else
        return "group.cc.arachat.daynote"
        #endif
    }()

    /// The folder inside the group's container, which the iOS app's host names the same way.
    public static let folderName = "Glance"

    public let folder: URL

    public init(folder: URL) {
        self.folder = folder
    }

    /// The group's folder, or nil in a build signed without the App Group.
    public static var shared: GlanceStore? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup)
            .map { GlanceStore(folder: $0.appendingPathComponent(folderName, isDirectory: true)) }
    }

    public var snapshotURL: URL { folder.appendingPathComponent("snapshot.json") }

    public var actionsURL: URL { folder.appendingPathComponent("actions", isDirectory: true) }

    public func load() -> GlanceSnapshot? {
        guard let data = try? Data(contentsOf: snapshotURL) else { return nil }
        return try? JSONDecoder().decode(GlanceSnapshot.self, from: data)
    }

    public func save(_ snapshot: GlanceSnapshot) throws {
        try write(JSONEncoder().encode(snapshot), to: snapshotURL)
    }

    /// Writes the app's own bytes, as the watch receives them, without a decode and encode between.
    public func save(snapshotData data: Data) throws {
        try write(data, to: snapshotURL)
    }

    /// Puts an action in the queue for the app's next drain.
    public func enqueue(_ action: GlanceAction, now: Date = Date()) throws {
        let millis = Int64(now.timeIntervalSince1970 * 1000)
        let name = String(format: "%015lld-%@.json", millis, action.id)
        try write(JSONEncoder().encode(action), to: actionsURL.appendingPathComponent(name))
    }

    /// The queued actions, oldest first. The app drains these; the watch's relay forwards its own.
    public func pendingActions() -> [(url: URL, action: GlanceAction)] {
        let files = (try? FileManager.default.contentsOfDirectory(at: actionsURL, includingPropertiesForKeys: nil)) ?? []
        return files
            .filter { $0.pathExtension == "json" && !$0.lastPathComponent.hasPrefix(".") }
            .sorted { $0.lastPathComponent < $1.lastPathComponent }
            .compactMap { url in
                guard let data = try? Data(contentsOf: url),
                      let action = try? JSONDecoder().decode(GlanceAction.self, from: data) else { return nil }
                return (url, action)
            }
    }

    /// A check made outside the app: queued for the app, and shown done straight away.
    ///
    /// The snapshot is the app's, and the app will write it again from its store once it has
    /// carried the check out; until then the extension's guess is the better picture. A row the
    /// app has since removed is simply not found, and the queued check is dropped on the app side.
    @discardableResult
    public func complete(_ todo: GlanceTodo, on day: LocalDay, now: Date = Date()) throws -> GlanceSnapshot? {
        try enqueue(.completing(todo, on: day, now: now), now: now)
        return try guess { $0.setDone(todo, on: day, true) }
    }

    /// A check taken off outside the app: queued, and shown open straight away.
    @discardableResult
    public func uncomplete(_ todo: GlanceTodo, on day: LocalDay, now: Date = Date()) throws -> GlanceSnapshot? {
        try enqueue(.uncompleting(todo, on: day, now: now), now: now)
        return try guess { $0.setDone(todo, on: day, false) }
    }

    /// A delete outside the app: queued, and the row gone from the snapshot straight away.
    @discardableResult
    public func delete(_ todo: GlanceTodo, on day: LocalDay, now: Date = Date()) throws -> GlanceSnapshot? {
        try enqueue(.deleting(todo, on: day, now: now), now: now)
        return try guess { $0.remove(todo, on: day) }
    }

    /// The extension's guess written over the snapshot, when the row is still in it.
    private func guess(_ change: (inout GlanceSnapshot) -> Bool) throws -> GlanceSnapshot? {
        guard var snapshot = load(), change(&snapshot) else { return nil }
        try save(snapshot)
        return snapshot
    }

    private func write(_ data: Data, to url: URL) throws {
        let directory = url.deletingLastPathComponent()
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let temporary = directory.appendingPathComponent(".\(url.lastPathComponent).\(UUID().uuidString)")
        try data.write(to: temporary)
        if FileManager.default.fileExists(atPath: url.path) {
            _ = try FileManager.default.replaceItemAt(url, withItemAt: temporary)
        } else {
            try FileManager.default.moveItem(at: temporary, to: url)
        }
    }
}
