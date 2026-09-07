using System.Text.RegularExpressions;
using Daynote.Presentation.Design;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Infrastructure.Portable.Tests.Design;

/// <summary>
/// The high-contrast palette covers the product palette exactly.
/// </summary>
/// <remarks>
/// This is the test that was missing when high contrast quietly did nothing. The old
/// <c>Daynote.Colors.HighContrast.xaml</c> redefined 28 brushes from the pre-v3 foundation layer and
/// the v3 UI reads none of them, so the mode was wired up, merged at startup, and invisible — with
/// nothing failing to say so.
/// <para>
/// Coverage in both directions is the whole point. A brush the table forgets renders in its normal
/// theme colour inside a high-contrast session, which is exactly the kind of fault a person who needs
/// this mode cannot work around, and which nobody testing in the normal theme will ever see.
/// </para>
/// <para>
/// It sits beside <see cref="PaletteParityTests"/> for the same reason: no UI framework needed, and
/// this is the one test project both CI legs run.
/// </para>
/// </remarks>
[TestClass]
public sealed class HighContrastPaletteTests
{
    [TestMethod]
    public void It_covers_every_product_brush_and_invents_none()
    {
        string[] product = [.. ProductBrushKeys().Order()];
        string[] covered = [.. HighContrastPalette.Keys.Order()];

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            product.Except(covered).Order().ToArray(),
            "These brushes have no high-contrast mapping, so they would keep their normal-theme "
                + "colour in a high-contrast session. Add them to HighContrastPalette.Roles.");
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            covered.Except(product).Order().ToArray(),
            "HighContrastPalette maps brushes the product palette does not define.");
    }

    [TestMethod]
    public void A_brush_is_either_a_role_or_a_literal_never_both()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            HighContrastPalette.Roles.Keys.Intersect(HighContrastPalette.Literals.Keys).Order().ToArray(),
            "A brush cannot both follow the system theme and keep a fixed colour.");
    }

    [TestMethod]
    public void Every_literal_is_a_full_opaque_argb_colour()
    {
        // The literals are pasted brand colours, and a short or alpha-less form parses differently
        // in the two frameworks.
        string[] malformed =
        [
            .. HighContrastPalette.Literals
                .Where(pair => !Regex.IsMatch(pair.Value, "^#FF[0-9A-Fa-f]{6}$"))
                .Select(pair => $"{pair.Key} = {pair.Value}")
                .Order(),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), malformed, $"Not #FFRRGGBB: {string.Join(", ", malformed)}");
    }

    [TestMethod]
    public void The_literals_match_the_colours_the_light_palette_uses()
    {
        // A literal is only defensible while it is the same colour the rest of the app draws. If the
        // brand colours are ever corrected, this catches the copy that was left behind.
        IReadOnlyDictionary<string, string> light = ProductBrushes();

        string[] drifted =
        [
            .. HighContrastPalette.Literals
                .Where(pair => !string.Equals(light[pair.Key], pair.Value, StringComparison.OrdinalIgnoreCase))
                .Select(pair => $"{pair.Key} (palette {light[pair.Key]}, high contrast {pair.Value})")
                .Order(),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), drifted, string.Join("; ", drifted));
    }

    private static IEnumerable<string> ProductBrushKeys() => ProductBrushes().Keys;

    /// <summary>The light palette, short-named, as the definition of which brushes the product has.</summary>
    private static IReadOnlyDictionary<string, string> ProductBrushes()
    {
        string markup = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Daynote.App", "Themes", "Daynote.Product.Light.xaml"));

        return Regex
            .Matches(
                markup,
                "<SolidColorBrush x:Key=\"Daynote\\.Product\\.Brush\\.(?<key>[^\"]+)\" Color=\"(?<color>[^\"]+)\"")
            .ToDictionary(
                match => match.Groups["key"].Value,
                match => match.Groups["color"].Value,
                StringComparer.Ordinal);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DESIGN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the Daynote repository above '{AppContext.BaseDirectory}'.");
    }
}
