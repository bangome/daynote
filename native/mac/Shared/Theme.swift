import SwiftUI

/// The design's widget palette (`Daynote Menu Bar - Desktop - Android Widgets`, §02 B8 light / B9 dark).
struct WidgetTheme {
    let fill: Color
    let ink: Color
    let heading: Color
    let muted: Color
    let secondary: Color
    let divider: Color
    let overdue: Color
    let sunday: Color
    let todayFill: Color
    let todayInk: Color

    static func of(_ scheme: ColorScheme) -> WidgetTheme {
        scheme == .dark
            ? WidgetTheme(
                fill: Color(hex: 0x1C1F35, opacity: 0.82), ink: Color(hex: 0xECEAF2),
                heading: Color(hex: 0xFFB27A), muted: Color(hex: 0x6C6F88),
                secondary: Color(hex: 0x9A9CB3), divider: Color(hex: 0x262A42),
                overdue: Color(hex: 0xFF8076), sunday: Color(hex: 0xFF8076),
                todayFill: Color(hex: 0xFF9A52), todayInk: Color(hex: 0x1A1206))
            : WidgetTheme(
                fill: Color(hex: 0xFFFEFB, opacity: 0.86), ink: Color(hex: 0x161A36),
                heading: Color(hex: 0xB4531A), muted: Color(hex: 0x9A9CAA),
                secondary: Color(hex: 0x62667D), divider: Color(hex: 0xEFECE5),
                overdue: Color(hex: 0xCF4A3F), sunday: Color(hex: 0xCF4A3F),
                todayFill: Color(hex: 0x1B2356), todayInk: .white)
    }

    /// The brand orange: the default list's ring, and the week strip's note dots.
    func accent(_ scheme: ColorScheme) -> Color { Color(hex: scheme == .dark ? 0xFF9A52 : 0xEE7F35) }

    /// A list's colour, which is also its check ring's colour: the app sends a light and a dark
    /// shade (`AgendaListPalette`), and which one applies is the widget's to know.
    func tint(_ list: GlanceList?, _ scheme: ColorScheme) -> Color {
        guard let list, let hex = UInt32((scheme == .dark ? list.colorDark : list.color).dropFirst(), radix: 16) else {
            return accent(scheme)
        }
        return Color(hex: hex)
    }
}

extension Color {
    init(hex: UInt32, opacity: Double = 1) {
        self.init(
            .sRGB,
            red: Double((hex >> 16) & 0xFF) / 255,
            green: Double((hex >> 8) & 0xFF) / 255,
            blue: Double(hex & 0xFF) / 255,
            opacity: opacity)
    }
}

/// Pretendard, the face the design is set in, registered from the extension's own Resources
/// (ATSApplicationFontsPath). Falls back to the system face if it is missing.
enum WidgetFont {
    static func of(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        let name = switch weight {
        case .heavy, .black: "Pretendard-ExtraBold"
        case .bold: "Pretendard-Bold"
        case .semibold: "Pretendard-SemiBold"
        default: "Pretendard-Regular"
        }
        return .custom(name, size: size)
    }
}
