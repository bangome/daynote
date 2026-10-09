import AppKit
import SwiftUI

// Renders every widget, light and dark, Korean and English, to PNG beside the design (B8/B9):
//
//   DaynoteWidgetPreviews <snapshot.json> <out-folder> [now: yyyy-MM-ddTHH:mm]
//
// The widget's views are drawn as they are; only the system's container (corner radius, shadow)
// is imitated here, because WidgetKit draws that itself and it does not exist outside it.

let arguments = CommandLine.arguments
guard arguments.count >= 3, let data = FileManager.default.contents(atPath: arguments[1]),
      let base = try? JSONDecoder().decode(GlanceSnapshot.self, from: data) else {
    FileHandle.standardError.write("usage: DaynoteWidgetPreviews <snapshot.json> <out> [now]\n".data(using: .utf8)!)
    exit(2)
}
let output = URL(fileURLWithPath: arguments[2], isDirectory: true)
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
// What the extension gets from ATSApplicationFontsPath, done by hand for a bare executable.
if let folder = ProcessInfo.processInfo.environment["DAYNOTE_WIDGET_FONTS"],
   let fonts = try? FileManager.default.contentsOfDirectory(atPath: folder) {
    for font in fonts where font.hasSuffix(".otf") {
        CTFontManagerRegisterFontsForURL(URL(fileURLWithPath: folder + "/" + font) as CFURL, .process, nil)
    }
}
WidgetLink.offscreen = true
let now = arguments.count > 3 ? LocalMoment(iso: arguments[3])!.date() : Date()

/// The English half of the fixture: the same rows, retitled, so KO and EN line up like B8 and B9.
func english(_ snapshot: GlanceSnapshot) -> GlanceSnapshot {
    let titles = [
        "회의실 예약 확인": "Confirm room booking", "퇴근 전 로그 확인": "Check logs before leaving",
        "릴리즈 노트 작성": "Write release notes", "비타민 먹기": "Take vitamins", "디자인 리뷰": "Design review",
    ]
    var copy = snapshot
    copy.language = "en"
    for index in copy.days.indices {
        copy.days[index].todos = copy.days[index].todos.map { var todo = $0; todo.title = titles[todo.title] ?? todo.title; return todo }
        copy.days[index].events = copy.days[index].events.map { var event = $0; event.title = titles[event.title] ?? event.title; return event }
    }
    return copy
}

@MainActor
func board(_ snapshot: GlanceSnapshot?, _ scheme: ColorScheme) -> some View {
    let context = WidgetContext(snapshot: snapshot, now: now, scheme: scheme)
    func card<V: View>(_ width: CGFloat, _ height: CGFloat, _ view: V) -> some View {
        view.padding(14)
            .frame(width: width, height: height, alignment: .topLeading)
            .background(
                RoundedRectangle(cornerRadius: 22, style: .continuous).fill(context.theme.fill)
                    .shadow(color: .black.opacity(0.16), radius: 15, y: 12))
    }
    let wallpaper = scheme == .dark
        ? LinearGradient(colors: [Color(hex: 0x20253F), Color(hex: 0x0F1222)], startPoint: .topLeading, endPoint: .bottomTrailing)
        : LinearGradient(colors: [Color(hex: 0xC9CFDC), Color(hex: 0x9AA3B5)], startPoint: .topLeading, endPoint: .bottomTrailing)
    return HStack(alignment: .top, spacing: 16) {
        VStack(alignment: .leading, spacing: 16) {
            card(364, 170, TodayWidgetView(context: context))
            HStack(spacing: 24) {
                card(170, 170, TodosWidgetView(context: context))
                card(170, 170, UpNextWidgetView(context: context))
            }
        }
        card(364, 382, WeekWidgetView(context: context))
    }
    .padding(.vertical, 44).padding(.horizontal, 30)
    .frame(width: 900, height: 500, alignment: .topLeading)
    .background(wallpaper)
    .environment(\.colorScheme, scheme)
}

@MainActor
func render(_ name: String, _ snapshot: GlanceSnapshot?, _ scheme: ColorScheme) throws {
    let renderer = ImageRenderer(content: board(snapshot, scheme))
    renderer.scale = 2
    guard let image = renderer.nsImage, let tiff = image.tiffRepresentation,
          let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) else {
        throw CocoaError(.fileWriteUnknown)
    }
    try png.write(to: output.appendingPathComponent(name + ".png"))
    print(output.appendingPathComponent(name + ".png").path)
}

var empty = base
empty.days = []
empty.week = empty.week.map { GlanceWeekDay(date: $0.date, noteCount: 0, titles: []) }
var ticked = base
ticked.days[0].todos[0].done = true
var locked = base
locked.locked = true

try MainActor.assumeIsolated {
    try render("light-ko", base, .light)
    try render("dark-ko", base, .dark)
    try render("light-en", english(base), .light)
    try render("dark-en", english(base), .dark)
    try render("light-ko-ticked", ticked, .light)
    try render("dark-en-empty", english(empty), .dark)
    try render("light-ko-no-snapshot", nil, .light)
    try render("dark-ko-locked", locked, .dark)
}
