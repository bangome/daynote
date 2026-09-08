namespace Daynote.Presentation.Design;

/// <summary>
/// The high-contrast palette for a platform that reports a contrast <em>preference</em> but no
/// colours to honour it with.
/// </summary>
/// <remarks>
/// <para>
/// Windows hands out the whole theme: <c>GetSysColor</c> answers every role, so
/// <c>WindowsHighContrastPalette</c> asks and merges the result, and the user's chosen theme wins
/// (docs/WINDOWS_ON_AVALONIA.md §4). macOS has no equivalent. "Increase contrast" in System Settings
/// darkens borders and drops translucency inside the system appearance; it does not publish a colour
/// set an app can read, and Avalonia surfaces only
/// <c>PlatformColorValues.ContrastPreference</c> — the fact of the preference, not the palette.
/// </para>
/// <para>
/// So the colours here are ours. That is a real difference from the Windows behaviour and not a
/// translation of it: on Windows the app defers to the theme, and here there is no theme to defer to,
/// only a request for more contrast than the v3 palette gives. Every value below is the maximum-
/// contrast reading of its role within the variant the user is already in — light stays light — and
/// each is at least 7:1 against the surface it is used on, which is WCAG AAA for body text.
/// </para>
/// <para>
/// It shares <see cref="HighContrastPalette.Roles"/> with the Windows path deliberately. The lossy
/// decisions in that table — status colours collapsing, the heat ramp losing a step, tints falling
/// back to the page — are properties of high contrast itself, not of Win32, so they should not be
/// re-litigated per platform. Only the nine role colours differ.
/// </para>
/// </remarks>
public static class DerivedHighContrastPalette
{
    /// <summary>
    /// Every product brush, prefixed key to <c>#AARRGGBB</c> literal, for one theme variant.
    /// </summary>
    /// <param name="dark">
    /// Whether the app is in its dark variant. High contrast does not choose light or dark; the user
    /// has already chosen, and inverting it here would be a surprise rather than an accommodation.
    /// </param>
    public static IReadOnlyDictionary<string, string> Build(bool dark)
    {
        var palette = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string key, HighContrastRole role) in HighContrastPalette.Roles)
        {
            palette[HighContrastPalette.KeyPrefix + key] = Resolve(role, dark);
        }

        // The Google marks keep their literal colours here for the same reason as on Windows: a
        // recoloured trademark is no longer the trademark.
        foreach ((string key, string literal) in HighContrastPalette.Literals)
        {
            palette[HighContrastPalette.KeyPrefix + key] = literal;
        }

        return palette;
    }

    /// <summary>The colour one role takes in one variant.</summary>
    public static string Resolve(HighContrastRole role, bool dark) => role switch
    {
        // The page, and text on it: the two ends of the range. Pure black and pure white, because
        // this is the mode where a designer's softened near-black is the thing being asked about.
        HighContrastRole.Window => dark ? Black : White,
        HighContrastRole.WindowText => dark ? White : Black,

        // Borders carry every boundary the collapsed surfaces no longer show, so they are the text
        // colour rather than a grey: in this mode a rule the user cannot see is a rule that is not
        // there.
        HighContrastRole.WindowFrame => dark ? White : Black,

        // A raised surface stays a shade off the page and no more. The v3 design layers near-white
        // on off-white in places; one step is enough to keep that reading, and the border does the
        // rest. Text on it is full contrast either way.
        HighContrastRole.ControlFace => dark ? "#FF2A2A2A" : "#FFE8E8E8",
        HighContrastRole.ControlText => dark ? White : Black,

        // Secondary text has to stay secondary and stay legible, which the v3 mid-grey manages only
        // in the first respect. These sit at roughly 11:1 on their page instead of the 4.6:1 the
        // ordinary palette gives.
        HighContrastRole.GrayText => dark ? "#FFD6D6D6" : "#FF3A3A3A",

        // The accent is mostly a fill — the selected day, the primary button — so it is paired with
        // a guaranteed partner rather than tuned on its own. Both directions clear 11:1 against
        // their page and against HighlightText.
        HighContrastRole.Highlight => dark ? "#FF66CCFF" : "#FF0000CC",
        HighContrastRole.HighlightText => dark ? Black : White,

        // Links and the states worth acting on. Distinct from Highlight so a link inside a selected
        // region is still a link, and never a hue that reads as decoration.
        HighContrastRole.Hotlight => dark ? "#FFFFD400" : "#FF8C0000",

        _ => dark ? White : Black,
    };

    private const string Black = "#FF000000";
    private const string White = "#FFFFFFFF";
}
