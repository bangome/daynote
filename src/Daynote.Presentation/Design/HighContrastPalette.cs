namespace Daynote.Presentation.Design;

/// <summary>
/// A colour the operating system's high-contrast theme defines, named by the job it does rather than
/// by either platform's API.
/// </summary>
/// <remarks>
/// Deliberately small. Every role here exists in both the WPF <c>SystemColors</c> keys and the Win32
/// <c>GetSysColor</c> indices, which is what lets one table drive both shells. Anything richer — a
/// second accent, a tint, a third shade of a border — does not exist in a high-contrast theme, and
/// pretending otherwise is how a palette ends up with two colours that render identically.
/// </remarks>
public enum HighContrastRole
{
    /// <summary>The page: <c>COLOR_WINDOW</c> / <c>SystemColors.WindowColor</c>.</summary>
    Window,

    /// <summary>Text on <see cref="Window"/>.</summary>
    WindowText,

    /// <summary>Borders and rules. High-contrast themes expect these to be visible, not subtle.</summary>
    WindowFrame,

    /// <summary>A raised control surface, distinct from the page.</summary>
    ControlFace,

    /// <summary>Text on <see cref="ControlFace"/>.</summary>
    ControlText,

    /// <summary>Secondary and disabled text.</summary>
    GrayText,

    /// <summary>A selected or filled region.</summary>
    Highlight,

    /// <summary>Text and glyphs on <see cref="Highlight"/>.</summary>
    HighlightText,

    /// <summary>Links, and anything the user is meant to notice.</summary>
    Hotlight,
}

/// <summary>
/// How the v3 product palette is expressed in the operating system's high-contrast colours.
/// </summary>
/// <remarks>
/// The decision behind this file (docs/WINDOWS_ON_AVALONIA.md §4) is that high contrast follows the
/// OS theme rather than shipping a fixed palette of our own: someone who has chosen a high-contrast
/// theme has chosen those colours, and overriding them is the opposite of the feature.
/// <para>
/// It lives in the shared presentation layer, and holds roles rather than colours, because both
/// shells need the same answer and neither can supply it: WPF resolves a role through
/// <c>SystemColors</c>, Avalonia through <c>GetSysColor</c>, and a role table is the only part they
/// can agree on without one referencing the other's framework.
/// </para>
/// <para>
/// <b>The mapping is lossy on purpose.</b> A high-contrast theme has no vocabulary for "green means
/// saved, amber means look at this, red means wrong", and no room for three shades of one hue. So the
/// status colours collapse onto text and <see cref="HighContrastRole.Hotlight"/>, and the calendar
/// heat ramp keeps only as many steps as there are distinguishable roles. Losing a shade is the
/// correct trade: in this mode the words and the shapes carry the meaning, which is what they should
/// have been doing anyway.
/// </para>
/// </remarks>
public static class HighContrastPalette
{
    /// <summary>The key prefix every product brush shares.</summary>
    public const string KeyPrefix = "Daynote.Product.Brush.";

    /// <summary>
    /// Brushes that keep their literal colour in high contrast, and why. The Google marks are a
    /// trademark rendered at a fixed set of colours; recolouring them produces something that is no
    /// longer the Google logo, which is both wrong and against the brand terms. They sit on a
    /// sign-in button whose surrounding text and border do respond to the theme.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Literals { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Google.Blue"] = "#FF4285F4",
            ["Google.Green"] = "#FF34A853",
            ["Google.Yellow"] = "#FFFBBC05",
            ["Google.Red"] = "#FFEA4335",
        };

    /// <summary>
    /// Each product brush and the system colour it becomes. Keys are the short names, without
    /// <see cref="KeyPrefix"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, HighContrastRole> Roles { get; } =
        new Dictionary<string, HighContrastRole>(StringComparer.Ordinal)
        {
            // Surfaces. High contrast has one background, so the layering the v3 design does with
            // near-white on off-white collapses; what separates regions here is the border.
            ["Bg1"] = HighContrastRole.Window,
            ["Bg2"] = HighContrastRole.Window,
            ["Panel"] = HighContrastRole.Window,
            ["Card"] = HighContrastRole.Window,
            ["Input"] = HighContrastRole.Window,
            ["Sticky"] = HighContrastRole.Window,

            // Which is why the borders have to be real: WindowFrame, not a subtle grey.
            ["Stroke"] = HighContrastRole.WindowFrame,
            ["Stroke2"] = HighContrastRole.WindowFrame,
            ["StickyStroke"] = HighContrastRole.WindowFrame,

            // Text.
            ["Text"] = HighContrastRole.WindowText,
            ["Text2"] = HighContrastRole.GrayText,
            ["StickyText"] = HighContrastRole.WindowText,

            // Raised control surfaces: the hover wash and the segmented track are the two places the
            // design relies on a surface being a shade off the page, so they take the control face.
            ["Hover"] = HighContrastRole.ControlFace,
            ["Seg"] = HighContrastRole.ControlFace,

            // Accent. Highlight rather than Hotlight, because the accent is mostly a fill here — the
            // selected day, the primary button — and Highlight is the role that comes with a
            // guaranteed-readable partner in HighlightText.
            ["Accent"] = HighContrastRole.Highlight,
            ["OnAccent"] = HighContrastRole.HighlightText,
            ["SwitchKnob"] = HighContrastRole.HighlightText,
            ["OnClose"] = HighContrastRole.HighlightText,

            // A tint of the accent is not expressible: there is no alpha in a system colour and no
            // second background. AccentSoft falls back to the page, and the accent still reads
            // through the border and the text of whatever it was tinting.
            ["AccentSoft"] = HighContrastRole.Window,
            ["OkSoft"] = HighContrastRole.Window,
            ["WarnSoft"] = HighContrastRole.Window,
            ["DangerSoft"] = HighContrastRole.Window,

            // Status. No green, amber or red to spend, so the three collapse: the states the user has
            // to act on take Hotlight, and a settled state is ordinary text.
            ["Ok"] = HighContrastRole.WindowText,
            ["Warn"] = HighContrastRole.Hotlight,
            ["Danger"] = HighContrastRole.Hotlight,
            ["Overdue"] = HighContrastRole.Hotlight,
            ["Extras"] = HighContrastRole.Hotlight,
            ["CloseHover"] = HighContrastRole.Highlight,

            // The calendar heat ramp keeps two visible steps instead of three. Level 1 is quiet, 2
            // and 3 are the ones worth spotting; a third distinguishable role does not exist here.
            ["Heat1"] = HighContrastRole.GrayText,
            ["Heat2"] = HighContrastRole.Hotlight,
            ["Heat3"] = HighContrastRole.WindowText,

            // Weekend tinting is decoration. The column header and the position already say which
            // day it is, so these become plain text rather than borrowing a status role.
            ["Weekend.Sun"] = HighContrastRole.WindowText,
            ["Weekend.Sat"] = HighContrastRole.WindowText,

            // A scrim is a translucent black in the other themes. Opaque is the honest version here:
            // it is covering the body while a modal is up, and dimming is not available.
            ["Scrim"] = HighContrastRole.Window,
            ["Scrim.Modal"] = HighContrastRole.Window,
        };

    /// <summary>Every product brush this palette defines, short-named, roles and literals together.</summary>
    public static IEnumerable<string> Keys => Roles.Keys.Concat(Literals.Keys);
}
