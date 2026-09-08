using Avalonia.Controls;
using Avalonia.Media;
using Daynote.Presentation.Design;

namespace Daynote.Desktop.Platform;

/// <summary>
/// <see cref="DerivedHighContrastPalette"/> as Avalonia brushes, for the platforms that report a
/// contrast preference without a colour set behind it — macOS today.
/// </summary>
/// <remarks>
/// The colours and the reasoning live in the shared presentation layer so they can be tested
/// without a UI framework; this file only turns literals into brushes. Built per variant, because
/// unlike the Windows system palette the derived one follows light and dark.
/// </remarks>
public static class DerivedHighContrastBrushes
{
    public static ResourceDictionary Build(bool dark)
    {
        var dictionary = new ResourceDictionary();

        foreach ((string key, string literal) in DerivedHighContrastPalette.Build(dark))
        {
            dictionary[key] = new SolidColorBrush(Color.Parse(literal));
        }

        return dictionary;
    }
}
