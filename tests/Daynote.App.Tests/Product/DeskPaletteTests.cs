using System.Windows.Media;
using Daynote.App.Shell.Product;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace Daynote.App.Tests.Product;

/// <summary>
/// The design-B layer paints over the product palette, and the stack above it still wins.
/// </summary>
/// <remarks>
/// Three dictionaries now claim the same keys, and the order between them is the whole behaviour:
/// the product palette declares them, Daynote.Desk.Light/Dark redefine them for this design, and
/// high contrast — when the system is in it — takes them from the OS. Getting that order wrong is
/// invisible at build time and silently wrong on screen, which is why it is asserted rather than
/// reasoned about.
/// <para>
/// The Avalonia shell reaches the same result by merging Daynote.Desk.axaml after
/// Daynote.Product.axaml. The colours below are that file's, so the two shells drifting apart
/// fails here.
/// </para>
/// </remarks>
[TestClass]
public sealed class DeskPaletteTests
{
    [STATestMethod]
    [DataRow(false, "#FFF6F5F1", "#FF161A36", "#FF1B2356", DisplayName = "light")]
    [DataRow(true, "#FF0E1020", "#FFECEAF2", "#FFFFAD72", DisplayName = "dark")]
    public void The_desk_layer_redefines_the_product_brushes(bool dark, string bg1, string text, string accent)
    {
        Application application = Fresh();

        new WpfProductThemeApplier(application, highContrast: false).Apply(dark);

        Assert.AreEqual(Parse(bg1), Resolve(application, "Daynote.Product.Brush.Bg1"));
        Assert.AreEqual(Parse(text), Resolve(application, "Daynote.Product.Brush.Text"));
        Assert.AreEqual(Parse(accent), Resolve(application, "Daynote.Product.Brush.Accent"));
    }

    [STATestMethod]
    [DataRow(false, DisplayName = "light")]
    [DataRow(true, DisplayName = "dark")]
    public void The_keys_only_this_design_has_resolve(bool dark)
    {
        Application application = Fresh();
        new WpfProductThemeApplier(application, highContrast: false).Apply(dark);

        // The navy sidebar and the orange sun are design-B's own; nothing in the product palette
        // declares them, so an unmerged desk layer shows up here as a missing brush rather than as
        // a wrong colour somewhere on screen.
        string[] deskOnly =
        [
            "Text3", "Pri", "OnPri", "Sun", "SunInk", "SunSoft", "Sunday",
            "Side", "SideText", "Side2", "SideHover", "SideActive", "SideStroke",
            "AvatarInk", "SideSunday", "Saved", "PostItShadow",
        ];

        string[] missing =
        [
            .. deskOnly
                .Where(key => application.TryFindResource("Daynote.Desk.Brush." + key) is not SolidColorBrush)
                .Order(),
        ];

        CollectionAssert.AreEqual(Array.Empty<string>(), missing, $"Unresolved desk brushes: {string.Join(", ", missing)}");
    }

    [STATestMethod]
    public void The_face_and_the_marks_resolve()
    {
        Application application = Fresh();
        new WpfProductThemeApplier(application, highContrast: false).Apply(dark: false);

        // Pretendard is addressed by a pack URI into this assembly. A wrong one does not throw —
        // WPF falls back to the default face — so the family is checked by name.
        var sans = application.TryFindResource("Daynote.Desk.Font.Sans") as FontFamily;
        Assert.IsNotNull(sans, "The design's face is not merged.");
        StringAssert.Contains(sans.Source, "Pretendard", StringComparison.Ordinal);

        // The key every existing Setter actually reads. Defining only the Desk key above left the
        // shell in Segoe and the design looking unapplied — the face has to arrive under the name
        // the styles ask for, which means this dictionary must win over Daynote.Product.Styles.xaml.
        var ui = application.TryFindResource("Daynote.Product.Font.UI") as FontFamily;
        Assert.IsNotNull(ui, "The UI face did not resolve.");
        StringAssert.Contains(
            ui.Source,
            "Pretendard",
            $"The shell still asks for '{ui.Source}', so nothing on screen changes face.");

        foreach (string mark in new[] { "Search", "Sun", "Moon", "Star", "PostIt", "Trash", "ChevronLeft", "Plus" })
        {
            Assert.IsInstanceOfType<Geometry>(
                application.TryFindResource("Daynote.Desk.Geo." + mark),
                $"Daynote.Desk.Geo.{mark} is missing or is not a geometry.");
        }
    }

    [STATestMethod]
    public void High_contrast_still_wins_over_the_desk_layer()
    {
        // The desk layer is merged on every apply, after the palette. If it were merged after the
        // high-contrast dictionary instead, turning the mode on would change nothing — the failure
        // the high-contrast tests exist to catch, reintroduced one layer higher.
        Application application = Fresh();
        new WpfProductThemeApplier(application, highContrast: true).Apply(dark: false);

        Assert.AreEqual(SystemColors.WindowColor, Resolve(application, "Daynote.Product.Brush.Bg1"));
        Assert.AreEqual(SystemColors.HighlightColor, Resolve(application, "Daynote.Product.Brush.Accent"));
    }

    [STATestMethod]
    public void Flipping_the_theme_keeps_the_desk_layer_on_top()
    {
        // The applier removes and re-inserts both palettes on every apply. If the desk half were
        // added once at startup, the second apply would leave it behind the product palette and the
        // design would come off with nothing to say so.
        Application application = Fresh();
        var applier = new WpfProductThemeApplier(application, highContrast: false);

        applier.Apply(dark: false);
        applier.Apply(dark: true);
        applier.Apply(dark: false);

        Assert.AreEqual(Parse("#FFF6F5F1"), Resolve(application, "Daynote.Product.Brush.Bg1"));
    }

    private static Application Fresh()
    {
        Application application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        return application;
    }

    private static Color Resolve(Application application, string key) =>
        application.TryFindResource(key) is SolidColorBrush brush
            ? brush.Color
            : throw new AssertFailedException($"{key} did not resolve to a brush.");

    private static Color Parse(string literal) => (Color)ColorConverter.ConvertFromString(literal)!;
}
