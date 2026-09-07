using System.Windows.Media;
using Daynote.App.Shell.Product;
using Daynote.App.Showcase;
using Daynote.Presentation.Design;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace Daynote.App.Tests.Product;

/// <summary>
/// Under the system high-contrast theme the product brushes come from the OS, not the palette.
/// </summary>
/// <remarks>
/// This is the pair of the Avalonia <c>HighContrastThemeTests</c>: one shared role table, so both
/// shells have to be checked or the table drifts against one of them.
/// <para>
/// The merge order is the mechanism and it is easy to get backwards. <see cref="WpfProductThemeApplier"/>
/// removes and re-inserts the light or dark palette on every apply, so a high-contrast dictionary
/// merged once at startup would end up ahead of it the first time the user flips the theme, and the
/// mode would come off with nothing to say so. That is the case the theme toggle below covers.
/// </para>
/// </remarks>
[TestClass]
public sealed class HighContrastPaletteTests
{
    [STATestMethod]
    [DataRow(false, DisplayName = "light")]
    [DataRow(true, DisplayName = "dark")]
    public void The_system_colours_win_over_the_palette(bool dark)
    {
        Application application = Fresh();
        new WpfProductThemeApplier(application, highContrast: true).Apply(dark);

        // Bg1 is the page: the system window colour, which is neither palette's value.
        Assert.AreEqual(SystemColors.WindowColor, Resolve(application, "Bg1"));
        Assert.AreEqual(SystemColors.WindowTextColor, Resolve(application, "Text"));
        Assert.AreEqual(SystemColors.GrayTextColor, Resolve(application, "Text2"));
        Assert.AreEqual(SystemColors.HighlightColor, Resolve(application, "Accent"));
        Assert.AreEqual(SystemColors.HighlightTextColor, Resolve(application, "OnAccent"));
    }

    [STATestMethod]
    public void Flipping_the_theme_does_not_take_high_contrast_off()
    {
        Application application = Fresh();
        var applier = new WpfProductThemeApplier(application, highContrast: true);

        applier.Apply(dark: false);
        applier.Apply(dark: true);
        applier.Apply(dark: false);

        Assert.AreEqual(SystemColors.WindowColor, Resolve(application, "Bg1"));
    }

    [STATestMethod]
    public void Without_high_contrast_the_palette_stands()
    {
        Application application = Fresh();
        new WpfProductThemeApplier(application, highContrast: false).Apply(dark: false);

        Assert.AreEqual(
            (Color)ColorConverter.ConvertFromString("#FFF4F4F5")!,
            Resolve(application, "Bg1"),
            "A build that is not in high contrast should paint the light palette.");
    }

    [STATestMethod]
    public void The_brand_colours_keep_their_literal_values()
    {
        Application application = Fresh();
        new WpfProductThemeApplier(application, highContrast: true).Apply(dark: false);

        foreach ((string key, string literal) in HighContrastPalette.Literals)
        {
            Assert.AreEqual(
                (Color)ColorConverter.ConvertFromString(literal)!,
                Resolve(application, key),
                $"{key} was recoloured.");
        }
    }

    [STATestMethod]
    public void Every_mapped_brush_resolves()
    {
        Application application = Fresh();
        new WpfProductThemeApplier(application, highContrast: true).Apply(dark: false);

        string[] unresolved =
        [
            .. HighContrastPalette.Keys
                .Where(key => application.TryFindResource(HighContrastPalette.KeyPrefix + key) is not SolidColorBrush)
                .Order(),
        ];

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            unresolved,
            $"Mapped but unresolvable: {string.Join(", ", unresolved)}");
    }

    private static Application Fresh()
    {
        Application application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        return application;
    }

    private static Color Resolve(Application application, string shortKey)
    {
        object? resource = application.TryFindResource(HighContrastPalette.KeyPrefix + shortKey);
        var brush = resource as SolidColorBrush;
        Assert.IsNotNull(brush, $"{shortKey} resolved to '{resource}', not a solid colour brush.");
        return brush.Color;
    }
}
