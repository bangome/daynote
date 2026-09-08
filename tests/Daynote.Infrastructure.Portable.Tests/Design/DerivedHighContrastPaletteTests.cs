using System.Globalization;
using System.Text.RegularExpressions;
using Daynote.Presentation.Design;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Design;

/// <summary>
/// The derived high-contrast palette — the one macOS gets, having no system colours to offer — is
/// complete, and actually high contrast.
/// </summary>
/// <remarks>
/// The second half is the one worth a test. A palette named "high contrast" that ships a 4.6:1 grey
/// is the Windows story of §4 all over again: the mode exists, is wired, and does not do the thing.
/// So every text-on-surface pairing the role table implies is measured against WCAG's formula, at
/// the AAA threshold, in both variants.
/// </remarks>
[TestClass]
public sealed class DerivedHighContrastPaletteTests
{
    private const double MinimumContrast = 7.0;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void It_defines_every_product_brush_the_role_table_does(bool dark)
    {
        IReadOnlyDictionary<string, string> palette = DerivedHighContrastPalette.Build(dark);

        CollectionAssert.AreEquivalent(
            HighContrastPalette.Keys.Select(key => HighContrastPalette.KeyPrefix + key).ToArray(),
            palette.Keys.ToArray(),
            "The derived palette and the shared role table have drifted apart.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Every_value_is_a_full_opaque_argb_colour(bool dark)
    {
        string[] malformed =
        [
            .. DerivedHighContrastPalette.Build(dark)
                .Where(pair => !Regex.IsMatch(pair.Value, "^#FF[0-9A-Fa-f]{6}$"))
                .Select(pair => $"{pair.Key} = {pair.Value}")
                .Order(),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), malformed, $"Not #FFRRGGBB: {string.Join(", ", malformed)}");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Text_clears_AAA_against_the_surface_it_sits_on(bool dark)
    {
        // Each pair is a foreground role and the background role the table puts it on.
        (HighContrastRole Text, HighContrastRole Surface)[] pairs =
        [
            (HighContrastRole.WindowText, HighContrastRole.Window),
            (HighContrastRole.GrayText, HighContrastRole.Window),
            (HighContrastRole.Hotlight, HighContrastRole.Window),
            (HighContrastRole.ControlText, HighContrastRole.ControlFace),
            (HighContrastRole.GrayText, HighContrastRole.ControlFace),
            (HighContrastRole.HighlightText, HighContrastRole.Highlight),
            // The accent is also drawn as a mark on the page: the heat dot, the check circle.
            (HighContrastRole.Highlight, HighContrastRole.Window),
        ];

        string[] failing =
        [
            .. pairs
                .Select(pair => (pair, Ratio: Contrast(
                    DerivedHighContrastPalette.Resolve(pair.Text, dark),
                    DerivedHighContrastPalette.Resolve(pair.Surface, dark))))
                .Where(item => item.Ratio < MinimumContrast)
                .Select(item => $"{item.pair.Text} on {item.pair.Surface} = {item.Ratio:F1}:1"),
        ];

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            failing,
            $"Below {MinimumContrast}:1 in the {(dark ? "dark" : "light")} variant: {string.Join("; ", failing)}");
    }

    [TestMethod]
    public void Borders_are_the_text_colour_not_a_grey()
    {
        // With every surface collapsed onto the page, the border is the only thing separating
        // regions. A grey border in high contrast is the defect the mode exists to remove.
        foreach (bool dark in new[] { false, true })
        {
            Assert.AreEqual(
                DerivedHighContrastPalette.Resolve(HighContrastRole.WindowText, dark),
                DerivedHighContrastPalette.Resolve(HighContrastRole.WindowFrame, dark),
                $"The {(dark ? "dark" : "light")} frame colour is not the text colour.");
        }
    }

    [TestMethod]
    public void The_variant_is_kept_not_inverted()
    {
        // The user chose light or dark before asking for more contrast. Light gets a white page.
        Assert.AreEqual("#FFFFFFFF", DerivedHighContrastPalette.Resolve(HighContrastRole.Window, dark: false));
        Assert.AreEqual("#FF000000", DerivedHighContrastPalette.Resolve(HighContrastRole.Window, dark: true));
    }

    [TestMethod]
    public void The_brand_literals_are_untouched()
    {
        foreach ((string key, string literal) in HighContrastPalette.Literals)
        {
            Assert.AreEqual(
                literal,
                DerivedHighContrastPalette.Build(dark: true)[HighContrastPalette.KeyPrefix + key],
                $"{key} was recoloured.");
        }
    }

    /// <summary>WCAG 2.x contrast ratio of two <c>#AARRGGBB</c> literals.</summary>
    private static double Contrast(string foreground, string background)
    {
        double a = Luminance(foreground);
        double b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string argb)
    {
        double r = Channel(argb.Substring(3, 2));
        double g = Channel(argb.Substring(5, 2));
        double b = Channel(argb.Substring(7, 2));
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double Channel(string hex)
    {
        double srgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }
}
