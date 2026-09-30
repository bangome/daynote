using FontFamily = System.Windows.Media.FontFamily;
using FontFamilyMap = System.Windows.Media.FontFamilyMap;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The note body's face, carrying the design's line height.
/// </summary>
/// <remarks>
/// The design sets the body at 15.5px on a 1.9 line height, and the Avalonia shell says so with a
/// <c>LineHeight</c> setter on both layers of the editor. WPF has no such property on
/// <see cref="System.Windows.Controls.TextBox"/> — only <see cref="System.Windows.Controls.TextBlock"/>
/// has one — and the two layers have to agree on every metric that decides a line box, or the caret
/// stops landing on its glyph.
/// <para>
/// So the line height travels on the face instead, which both controls lay out from. A family that
/// merely names an installed font cannot carry one ("a named FontFamily object cannot be modified");
/// a composite family can, and maps the whole of Unicode onto the bundled face. That is the
/// supported way to state a line height WPF will apply to a text box.
/// </para>
/// </remarks>
public static class DeskFonts
{
    /// <summary>The bundled face, addressed the way WPF finds a font shipped as a resource.</summary>
    private const string PretendardUri = "pack://application:,,,/Daynote.App;component/Assets/Fonts/#Pretendard";

    /// <summary>The design's body line height, as a multiple of the font size (15.5 × 1.9 = 29.45).</summary>
    public const double BodyLineSpacing = 1.9;

    /// <summary>
    /// Where the baseline sits inside that line box. The extra room the line height buys is split
    /// above and below the glyphs by this: 0.9 of 1.9 leaves a little more under them than over,
    /// which is where a Latin-plus-Hangul run reads level.
    /// </summary>
    private const double BodyBaseline = 0.9;

    /// <summary>Pretendard, on the body's line height. Both editor layers use this one object.</summary>
    public static FontFamily Body { get; } = BuildBody();

    private static FontFamily BuildBody()
    {
        var family = new FontFamily
        {
            LineSpacing = BodyLineSpacing,
            Baseline = BodyBaseline,
        };
        family.FamilyMaps.Add(new FontFamilyMap
        {
            Unicode = "0000-10ffff",
            Target = PretendardUri,
            Scale = 1.0,
        });
        return family;
    }
}
