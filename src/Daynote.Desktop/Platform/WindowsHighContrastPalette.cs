using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Media;
using Daynote.Presentation.Design;

namespace Daynote.Desktop.Platform;

/// <summary>
/// Builds the product palette out of the operating system's high-contrast colours, from the same
/// shared role table the WPF shell uses.
/// </summary>
/// <remarks>
/// Avalonia surfaces a contrast <em>preference</em> (<c>PlatformColorValues.ContrastPreference</c>)
/// but not the colours behind it, so the values come from Win32 <c>GetSysColor</c> directly. That is
/// the price of following the OS theme rather than shipping a fixed palette
/// (docs/WINDOWS_ON_AVALONIA.md §4), and it is why this file is Windows-only: macOS "Increase
/// contrast" adjusts the system appearance instead of exposing a colour set, so it needs its own
/// answer rather than a bad translation of this one.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsHighContrastPalette
{
    /// <summary>
    /// The brushes to merge over the theme dictionaries. Read once, for the same reason as the WPF
    /// side: changing high-contrast theme is a sign-out in practice.
    /// </summary>
    public static ResourceDictionary Build()
    {
        var dictionary = new ResourceDictionary();

        foreach ((string key, HighContrastRole role) in HighContrastPalette.Roles)
        {
            dictionary[HighContrastPalette.KeyPrefix + key] = new SolidColorBrush(Resolve(role));
        }

        foreach ((string key, string literal) in HighContrastPalette.Literals)
        {
            dictionary[HighContrastPalette.KeyPrefix + key] = new SolidColorBrush(Color.Parse(literal));
        }

        return dictionary;
    }

    // The GetSysColor indices behind each role. Named here rather than inline so the mapping from
    // the shared table to Win32 is readable in one place.
    private const int ColorWindow = 5;
    private const int ColorWindowFrame = 6;
    private const int ColorWindowText = 8;
    private const int ColorHighlight = 13;
    private const int ColorHighlightText = 14;
    private const int ColorButtonFace = 15;
    private const int ColorButtonText = 18;
    private const int ColorGrayText = 17;
    private const int ColorHotlight = 26;

    private static Color Resolve(HighContrastRole role) => FromSystem(role switch
    {
        HighContrastRole.Window => ColorWindow,
        HighContrastRole.WindowText => ColorWindowText,
        HighContrastRole.WindowFrame => ColorWindowFrame,
        HighContrastRole.ControlFace => ColorButtonFace,
        HighContrastRole.ControlText => ColorButtonText,
        HighContrastRole.GrayText => ColorGrayText,
        HighContrastRole.Highlight => ColorHighlight,
        HighContrastRole.HighlightText => ColorHighlightText,
        HighContrastRole.Hotlight => ColorHotlight,
        _ => ColorWindowText,
    });

    /// <summary>GetSysColor returns COLORREF: 0x00BBGGRR, so the byte order is the reverse of ARGB.</summary>
    private static Color FromSystem(int index)
    {
        uint packed = GetSysColor(index);
        return Color.FromRgb((byte)(packed & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)((packed >> 16) & 0xFF));
    }

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int nIndex);
}
