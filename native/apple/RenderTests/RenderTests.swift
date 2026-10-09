import SwiftUI
import WidgetKit
import XCTest

/// Draws every widget, Lock Screen accessory, Live Activity and watch screen to PNG, light and
/// dark, Korean and English, at the design's moment (Wednesday 7 October 2026, 14:30), so they
/// can be laid beside the design renders. Not a pixel comparison: the design is drawn in a
/// browser with Pretendard and these in SF, so the check is by eye, and these files are what the
/// eye looks at.
///
/// `xcodebuild test -scheme DaynoteRenderTests -destination 'platform=iOS Simulator,…'` with
/// `TEST_RUNNER_DAYNOTE_RENDER_DIR=/some/folder` in the environment.
@MainActor
final class RenderTests: XCTestCase {
    private var folder: URL!

    override func setUp() async throws {
        let path = ProcessInfo.processInfo.environment["DAYNOTE_RENDER_DIR"] ?? NSTemporaryDirectory() + "daynote-renders"
        folder = URL(fileURLWithPath: path, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    private let designDay = LocalDay(year: 2026, month: 10, day: 7)

    private func moment(_ hour: Int, _ minute: Int) -> Date {
        LocalMoment(day: designDay, hour: hour, minute: minute).date()
    }

    private func entry(english: Bool, at hour: Int = 14, _ minute: Int = 30, checked: Bool = false) -> GlanceEntry {
        var snapshot = GlanceSamples.make(today: designDay, english: english)
        if checked { snapshot.days[0].todos[0].done = true }
        return GlanceEntry(
            date: moment(hour, minute), snapshot: snapshot,
            recentlyChecked: checked ? [snapshot.days[0].todos[0].rowKey] : [])
    }

    func testHomeScreenWidgets() throws {
        for english in [false, true] {
            for scheme in [ColorScheme.light, .dark] {
                let suffix = "\(english ? "en" : "ko")-\(scheme == .dark ? "dark" : "light")"
                let e = entry(english: english)
                try widget("W3-next-\(suffix)", width: 170, height: 170, scheme: scheme) { SmallNextEventView(entry: e, theme: $0) }
                try widget("W4-todos-\(suffix)", width: 170, height: 170, scheme: scheme) { SmallTodosView(entry: e, theme: $0) }
                let checked = entry(english: english, checked: true)
                try widget("W4-todos-checked-\(suffix)", width: 170, height: 170, scheme: scheme) { SmallTodosView(entry: checked, theme: $0) }
                try widget("W5-capture-\(suffix)", width: 170, height: 170, scheme: scheme) { SmallQuickCaptureView(entry: e, theme: $0) }
                try widget("W6-today-\(suffix)", width: 364, height: 170, scheme: scheme) { MediumTodayView(entry: e, theme: $0) }
                try widget("W7-week-\(suffix)", width: 364, height: 170, scheme: scheme) { MediumWeekView(entry: e, theme: $0) }
                try widget("W8-favorites-\(suffix)", width: 364, height: 170, scheme: scheme) { MediumFavoritesView(entry: e, theme: $0) }
                try widget("W10-large-\(suffix)", width: 364, height: 382, scheme: scheme) { LargeDayView(entry: e, theme: $0) }
            }
        }
    }

    func testEmptyAndLockedStates() throws {
        var empty = GlanceSamples.make(today: designDay, english: false)
        empty.days[0].todos = []
        empty.days[0].events = []
        let e = GlanceEntry(date: moment(14, 30), snapshot: empty, recentlyChecked: [])
        try widget("empty-today-ko-light", width: 364, height: 170, scheme: .light) { MediumTodayView(entry: e, theme: $0) }
        try widget("empty-todos-ko-light", width: 170, height: 170, scheme: .light) { SmallTodosView(entry: e, theme: $0) }
        var locked = empty
        locked.locked = true
        let l = GlanceEntry(date: moment(14, 30), snapshot: locked, recentlyChecked: [])
        try widget("locked-ko-dark", width: 170, height: 170, scheme: .dark) { WidgetNotice(entry: l, theme: $0) }
    }

    func testLockScreenAndStandBy() throws {
        for english in [false, true] {
            let suffix = english ? "en" : "ko"
            let e = entry(english: english)
            try save("W12-lock-\(suffix)", lockScreen(e), width: 390, height: 230)
            try save("W14-standby-\(suffix)", standBy(e, night: false), width: 700, height: 330)
            try save("W15-standby-night-\(suffix)", standBy(e, night: true), width: 700, height: 330)
        }
    }

    func testLiveActivity() throws {
        for english in [false, true] {
            let suffix = english ? "en" : "ko"
            let attributes = EventActivityAttributes(
                eventId: "review", listName: english ? "Work" : "업무", english: english, noteLink: "daynote://day?date=2026-10-07")
            let state = EventActivityAttributes.ContentState(
                title: english ? "Design review" : "디자인 리뷰", start: moment(16, 0), end: moment(17, 0))
            for scheme in [ColorScheme.light, .dark] {
                let name = "W18-banner-soon-\(suffix)-\(scheme == .dark ? "dark" : "light")"
                try save(name, EventBanner(attributes: attributes, state: state, isStale: false, now: moment(15, 50).addingTimeInterval(18))
                    .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous)).environment(\.colorScheme, scheme), width: 364, height: 150)
            }
            try save("W19-banner-now-\(suffix)-dark", EventBanner(attributes: attributes, state: state, isStale: true, now: moment(16, 10))
                .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous)).environment(\.colorScheme, .dark), width: 364, height: 150)
            try save("W20-island-\(suffix)", island(attributes: attributes, state: state, now: moment(15, 50).addingTimeInterval(18)), width: 380, height: 260)
        }
    }

    func testWatchScreens() throws {
        for english in [false, true] {
            let suffix = english ? "en" : "ko"
            let store = WatchStore(snapshot: GlanceSamples.make(today: designDay, english: english))
            store.now = { [unowned self] in moment(14, 30) }
            try watch("A1-today-\(suffix)", store: store)

            store.tap(store.todos[0])
            try watch("A4-checked-\(suffix)", store: store)

            let sentence = english ? "Share draft slides today at 5pm" : "회의자료 초안 공유 오늘 5시"
            let readback = CaptureReadback.read(sentence, now: LocalMoment(moment(14, 30)), text: store.text)
            try save("A7-readback-\(suffix)", ReadbackView(sentence: sentence, readback: readback, store: store, clock: "14:30")
                .frame(width: 198, height: 242, alignment: .top).background(.black), width: 198, height: 242)

            let made = WatchStore(snapshot: GlanceSamples.make(today: designDay, english: english))
            made.now = { [unowned self] in moment(14, 30) }
            made.create(sentence, kind: "task", readback: readback)
            try watch("A9-created-\(suffix)", store: made)

            let other = WatchStore(snapshot: GlanceSamples.make(today: designDay, english: english))
            other.now = { [unowned self] in moment(14, 30) }
            other.addedElsewhere = designDay.adding(days: 1)
            try watch("A10-elsewhere-\(suffix)", store: other)
        }

        let rolled = WatchStore(snapshot: GlanceSamples.make(today: designDay, english: false))
        rolled.now = { [unowned self] in moment(14, 30) }
        let misread = "회의자료 초안 공유 오전 5시"
        try save("A8-misread-ko", ReadbackView(
            sentence: misread, readback: CaptureReadback.read(misread, now: LocalMoment(moment(14, 30)), text: rolled.text),
            store: rolled, clock: "14:30").frame(width: 198, height: 242, alignment: .top).background(.black), width: 198, height: 242)
    }

    // MARK: - Frames

    private func widget<V: View>(
        _ name: String, width: CGFloat, height: CGFloat, scheme: ColorScheme, @ViewBuilder content: (GlanceTheme) -> V
    ) throws {
        let theme = GlanceTheme.of(scheme)
        let view = content(theme)
            .padding(15)
            .frame(width: width, height: height, alignment: .topLeading)
            .background(theme.background)
            .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
            .padding(24)
            .background(scheme == .dark ? Color(hex: "#1c2033") : Color(hex: "#9aa3b5"))
            .environment(\.colorScheme, scheme)
        try save(name, view, width: width + 48, height: height + 48)
    }

    private func watch(_ name: String, store: WatchStore) throws {
        let view = TodayView(store: store, clock: "14:30") { CaptureLabel(title: store.text.capture) }
            .frame(width: 198, height: 242, alignment: .top)
            .background(.black)
            .environment(\.colorScheme, .dark)
        try save(name, view, width: 198, height: 242)
    }

    /// The Lock Screen's three accessories over a wallpaper, in the system's white.
    private func lockScreen(_ e: GlanceEntry) -> some View {
        VStack(spacing: 14) {
            InlineNextEventView(entry: e).font(.system(size: 15, weight: .semibold))
            Text("14:30").font(.system(size: 72, weight: .semibold))
            HStack(spacing: 12) {
                CircularRemainingView(entry: e).frame(width: 72, height: 72)
                RectangularNextTodoView(entry: e).padding(8).frame(width: 160, height: 72)
                    .background(RoundedRectangle(cornerRadius: 14).fill(.white.opacity(0.14)))
                CircularCaptureView(entry: e).frame(width: 72, height: 72).clipShape(Circle())
            }
        }
        .foregroundStyle(.white)
        .frame(width: 390, height: 230)
        .background(Color(hex: "#2c3350"))
        .environment(\.colorScheme, .dark)
    }

    /// StandBy's two halves side by side; at night the system draws them in red, which is
    /// imitated here with a red multiply over the same views.
    private func standBy(_ e: GlanceEntry, night: Bool) -> some View {
        HStack(spacing: 20) {
            if let event = e.nextEvent {
                StandByNextEventView(event: event, entry: e).padding(24)
                    .frame(width: 320, height: 290).background(RoundedRectangle(cornerRadius: 34).fill(Color(hex: night ? "#140202" : "#1a1c26")))
            }
            StandByTodosView(entry: e).padding(24)
                .frame(width: 320, height: 290).background(RoundedRectangle(cornerRadius: 34).fill(Color(hex: night ? "#140202" : "#1a1c26")))
        }
        .foregroundStyle(.white)
        .colorMultiply(night ? Color(hex: "#ff3b30") : .white)
        .frame(width: 700, height: 330)
        .background(.black)
        .environment(\.colorScheme, .dark)
    }

    /// The Dynamic Island's compact pill and its expanded card, assembled from the same regions.
    private func island(attributes: EventActivityAttributes, state: EventActivityAttributes.ContentState, now: Date) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                DaynoteMark(size: 18)
                Spacer()
                Countdown(state: state, stale: false, now: now, size: 14)
            }
            .padding(.horizontal, 14).frame(width: 250, height: 37).background(Capsule().fill(.black))
            VStack(spacing: 12) {
                HStack(alignment: .top) {
                    DaynoteMark(size: 22)
                    IslandTitle(attributes: attributes, state: state)
                    Countdown(state: state, stale: false, now: now, size: 26)
                }
                ActivityButtons(attributes: attributes, text: GlanceText(english: attributes.english), compact: true)
            }
            .padding(18).frame(width: 370).background(RoundedRectangle(cornerRadius: 40, style: .continuous).fill(.black))
        }
        .foregroundStyle(.white)
        .padding(5)
        .frame(width: 380, height: 260, alignment: .topLeading)
        .background(Color(hex: "#f1f0eb"))
        .environment(\.colorScheme, .dark)
    }

    private func save<V: View>(_ name: String, _ view: V, width: CGFloat, height: CGFloat) throws {
        let renderer = ImageRenderer(content: view.frame(width: width, height: height, alignment: .top).clipped())
        renderer.scale = 3
        let image = try XCTUnwrap(renderer.uiImage, name)
        try XCTUnwrap(image.pngData()).write(to: folder.appendingPathComponent("\(name).png"))
    }
}
