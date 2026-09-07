using System.Windows.Media;
using Daynote.Presentation.Design;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ResourceDictionary = System.Windows.ResourceDictionary;
using SystemColors = System.Windows.SystemColors;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Builds the product palette out of the operating system's high-contrast colours, from the shared
/// role table in <see cref="HighContrastPalette"/>.
/// </summary>
/// <remarks>
/// The colours are read once, not tracked. Windows requires a sign-out to change high-contrast
/// theme in practice, and the app already decided at startup whether high contrast was on at all
/// (<c>App.OnStartup</c>) — resolving the brushes live would be a liveness the surrounding code does
/// not have.
/// </remarks>
public static class WpfHighContrastPalette
{
    /// <summary>
    /// A dictionary of <c>Daynote.Product.Brush.*</c> brushes to merge last, so it overrides the
    /// light or dark palette by key.
    /// </summary>
    public static ResourceDictionary Build()
    {
        var dictionary = new ResourceDictionary();

        foreach ((string key, HighContrastRole role) in HighContrastPalette.Roles)
        {
            dictionary[HighContrastPalette.KeyPrefix + key] = Frozen(Resolve(role));
        }

        foreach ((string key, string literal) in HighContrastPalette.Literals)
        {
            dictionary[HighContrastPalette.KeyPrefix + key] =
                Frozen((Color)ColorConverter.ConvertFromString(literal)!);
        }

        return dictionary;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Resolve(HighContrastRole role) => role switch
    {
        HighContrastRole.Window => SystemColors.WindowColor,
        HighContrastRole.WindowText => SystemColors.WindowTextColor,
        HighContrastRole.WindowFrame => SystemColors.WindowFrameColor,
        HighContrastRole.ControlFace => SystemColors.ControlColor,
        HighContrastRole.ControlText => SystemColors.ControlTextColor,
        HighContrastRole.GrayText => SystemColors.GrayTextColor,
        HighContrastRole.Highlight => SystemColors.HighlightColor,
        HighContrastRole.HighlightText => SystemColors.HighlightTextColor,
        HighContrastRole.Hotlight => SystemColors.HotTrackColor,
        _ => SystemColors.WindowTextColor,
    };
}
