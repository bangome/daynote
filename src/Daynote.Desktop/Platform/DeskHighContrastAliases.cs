using Avalonia.Controls;
using Daynote.Presentation.Design;

namespace Daynote.Desktop.Platform;

/// <summary>
/// The design-B brushes in a high-contrast session.
/// </summary>
/// <remarks>
/// Daynote.Desk.axaml adds brushes the shared palette does not have — the primary fill, the sun
/// marks, the navy sidebar. A high-contrast dictionary built from the shared roles leaves them out,
/// so they would keep their design colours over the system's. Each one takes the high-contrast brush
/// of the shared role it plays: the primary is the accent, faint text is secondary text, the sidebar
/// is the page. Pointing at the same brush instances keeps the pairs (fill and ink) the system chose.
/// </remarks>
public static class DeskHighContrastAliases
{
    /// <summary>Desk key → the Product key whose high-contrast brush it borrows.</summary>
    public static IReadOnlyDictionary<string, string> Map { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Text3"] = "Text2",
        ["Pri"] = "Accent",
        ["OnPri"] = "OnAccent",
        ["Sun"] = "Accent",
        ["SunInk"] = "Accent",
        ["SunSoft"] = "AccentSoft",
        ["Sunday"] = "Weekend.Sun",
        ["Side"] = "Bg1",
        ["SideText"] = "Text",
        ["Side2"] = "Text2",
        ["SideHover"] = "Hover",
        ["SideActive"] = "AccentSoft",
        ["SideStroke"] = "Stroke",
        ["AvatarInk"] = "OnAccent",
        ["SideSunday"] = "Weekend.Sun",
        ["Saved"] = "Ok",
        ["PostItShadow"] = "Stroke",
    };

    private const string DeskPrefix = "Daynote.Desk.Brush.";

    /// <summary>Adds the Desk keys to a high-contrast dictionary that already holds the Product keys.</summary>
    public static ResourceDictionary AddTo(ResourceDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        foreach ((string desk, string product) in Map)
        {
            if (dictionary.TryGetValue(HighContrastPalette.KeyPrefix + product, out object? brush))
            {
                dictionary[DeskPrefix + desk] = brush;
            }
        }

        return dictionary;
    }
}
