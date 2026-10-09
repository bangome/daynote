import SwiftUI

/// The widget and watch palettes, from the two design files' own values.
///
/// Widgets follow the system's light or dark appearance; the watch has one, black. The list colour
/// a check ring takes comes from the snapshot, which carries both its light and its dark value.
public struct GlanceTheme: Sendable {
    public var background: Color
    public var text: Color
    public var accent: Color
    public var muted: Color
    public var secondary: Color
    public var overdue: Color
    public var divider: Color
    public var chip: Color
    public var primaryButton: Color
    public var primaryButtonText: Color
    public var selectedDay: Color
    public var selectedDayText: Color
    public var sunday: Color
    public var dark: Bool

    public static let light = GlanceTheme(
        background: Color(hex: "#fffefb"), text: Color(hex: "#161a36"), accent: Color(hex: "#b4531a"),
        muted: Color(hex: "#9a9caa"), secondary: Color(hex: "#62667d"), overdue: Color(hex: "#cf4a3f"),
        divider: Color(hex: "#efece5"), chip: Color(hex: "#efede6"), primaryButton: Color(hex: "#1b2356"),
        primaryButtonText: .white, selectedDay: Color(hex: "#1b2356"), selectedDayText: .white,
        sunday: Color(hex: "#cf4a3f"), dark: false)

    public static let dark = GlanceTheme(
        background: Color(hex: "#171a2e"), text: Color(hex: "#eceaf2"), accent: Color(hex: "#ffb27a"),
        muted: Color(hex: "#6c6f88"), secondary: Color(hex: "#9a9cb3"), overdue: Color(hex: "#ff8076"),
        divider: Color(hex: "#262a42"), chip: Color(hex: "#22253c"), primaryButton: Color(hex: "#ff9a52"),
        primaryButtonText: Color(hex: "#1a1206"), selectedDay: Color(hex: "#ff9a52"), selectedDayText: Color(hex: "#1a1206"),
        sunday: Color(hex: "#ff8076"), dark: true)

    /// The watch: black, with cards a step above it.
    public static let watch = GlanceTheme(
        background: .black, text: .white, accent: Color(hex: "#ff9a52"),
        muted: Color(red: 235 / 255, green: 235 / 255, blue: 245 / 255).opacity(0.38),
        secondary: Color(red: 235 / 255, green: 235 / 255, blue: 245 / 255).opacity(0.62),
        overdue: Color(hex: "#ff6b5e"), divider: Color(hex: "#2a2b31"), chip: Color(hex: "#1c1d22"),
        primaryButton: Color(hex: "#ff9a52"), primaryButtonText: .black, selectedDay: Color(hex: "#ff9a52"),
        selectedDayText: .black, sunday: Color(hex: "#ff6b5e"), dark: true)

    public static func of(_ scheme: ColorScheme) -> GlanceTheme { scheme == .dark ? .dark : .light }

    /// A list's ring colour on this background. An unknown list draws in the accent.
    public func ring(for listId: String, in snapshot: GlanceSnapshot?) -> Color {
        guard let list = snapshot?.list(listId) else { return dark ? Color(hex: "#ff9a52") : Color(hex: "#ee7f35") }
        return Color(hex: dark ? list.colorDark : list.color)
    }
}

public extension Color {
    /// `#rrggbb`.
    init(hex: String) {
        let digits = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        let value = UInt32(digits, radix: 16) ?? 0
        self.init(
            red: Double((value >> 16) & 0xFF) / 255,
            green: Double((value >> 8) & 0xFF) / 255,
            blue: Double(value & 0xFF) / 255)
    }
}

/// The check ring: the list's colour, empty until done, then filled with a tick (M3, shortened to
/// M8 on a widget and a watch — the fill and the dimmed row, nothing that needs a frame budget).
public struct CheckRing: View {
    public var color: Color
    public var done: Bool
    public var size: CGFloat
    public var lineWidth: CGFloat

    public init(color: Color, done: Bool, size: CGFloat = 18, lineWidth: CGFloat = 2) {
        self.color = color
        self.done = done
        self.size = size
        self.lineWidth = lineWidth
    }

    public var body: some View {
        ZStack {
            Circle().strokeBorder(color, lineWidth: lineWidth).opacity(done ? 0 : 1)
            Circle().fill(color).scaleEffect(done ? 1 : 0.001)
            Image(systemName: "checkmark")
                .font(.system(size: size * 0.5, weight: .heavy))
                .foregroundStyle(.white)
                .opacity(done ? 1 : 0)
        }
        .frame(width: size, height: size)
    }
}

/// The Daynote mark, from the extension's asset catalog.
public struct DaynoteMark: View {
    public var size: CGFloat

    public init(size: CGFloat = 16) {
        self.size = size
    }

    public var body: some View {
        Image("DaynoteMark", bundle: .glanceAssets)
            .resizable()
            .interpolation(.high)
            .frame(width: size, height: size)
    }
}

public extension Bundle {
    /// Where the asset catalog was compiled: the extension itself, or the render tests' bundle.
    static var glanceAssets: Bundle {
        #if DAYNOTE_RENDER_TESTS
        return Bundle(for: GlanceAssetsAnchor.self)
        #else
        return .main
        #endif
    }
}

#if DAYNOTE_RENDER_TESTS
final class GlanceAssetsAnchor {}
#endif
