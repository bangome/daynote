import Foundation
import WidgetKit

// The two things the .NET app cannot do for itself, exported as C symbols it P/Invokes
// (src/Daynote.Desktop/Platform/MacWidgetBridge.cs). Loaded into the app's own process on purpose:
// WidgetCenter answers to the bundle that called it, and only Daynote.app contains the widgets.

/// Writes the App Group container's path into `buffer` and returns its length, or -1 when the
/// group is not available to this process — an ad-hoc build carries no team, so no group.
@_cdecl("daynote_widgets_container")
public func daynoteWidgetsContainer(
    _ group: UnsafePointer<CChar>, _ buffer: UnsafeMutablePointer<CChar>, _ capacity: Int32
) -> Int32 {
    guard let url = FileManager.default.containerURL(
        forSecurityApplicationGroupIdentifier: String(cString: group)) else { return -1 }
    let path = Array(url.path.utf8CString)
    guard path.count <= Int(capacity) else { return -1 }
    path.withUnsafeBufferPointer { source in
        buffer.update(from: source.baseAddress!, count: path.count)
    }
    return Int32(path.count - 1)
}

/// Asks every Daynote widget to read the snapshot again.
@_cdecl("daynote_widgets_reload")
public func daynoteWidgetsReload() {
    WidgetCenter.shared.reloadAllTimelines()
}
