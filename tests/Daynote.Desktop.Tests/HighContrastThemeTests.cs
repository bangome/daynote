using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Daynote.Desktop.Platform;
using Daynote.Presentation.Design;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Under the system high-contrast theme the product brushes come from the OS, not the palette.
/// </summary>
/// <remarks>
/// The merge order is the whole mechanism and it is easy to get backwards: the palette dictionaries
/// are swapped by the light/dark toggle, so a high-contrast dictionary merged once at startup ends up
/// behind them the first time someone flips the theme and the mode silently comes off. These tests
/// apply both variants and check the override survives.
/// </remarks>
[TestClass]
public sealed class HighContrastThemeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void The_system_colours_win_over_the_palette(bool dark)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The palette is read through GetSysColor; macOS needs its own answer.");
        }

        HeadlessAppFixture.OnUiThread(() =>
        {
            Application application = Application.Current!;
            int before = application.Resources.MergedDictionaries.Count;
            try
            {
                new AvaloniaThemeApplier(application, highContrast: true).Apply(dark);

                Assert.AreEqual(
                    dark ? ThemeVariant.Dark : ThemeVariant.Light,
                    application.RequestedThemeVariant,
                    "The variant still tracks the app's own preference in high contrast.");

                // Bg1 is the page. In high contrast it has to be the system window colour, which is
                // black or white in every shipped high-contrast theme and neither of the palette's
                // values (#FFF4F4F5 light, #FF121316 dark).
                object? resource = Lookup(application, "Bg1", dark);
                var brush = resource as ISolidColorBrush;
                Assert.IsNotNull(brush, $"Bg1 resolved to '{resource}', not a solid colour brush.");
                Assert.AreEqual(WindowsSystemWindowColor(), brush.Color, "Bg1 is not the system window colour.");
            }
            finally
            {
                while (application.Resources.MergedDictionaries.Count > before)
                {
                    application.Resources.MergedDictionaries.RemoveAt(
                        application.Resources.MergedDictionaries.Count - 1);
                }
            }
        });
    }

    [TestMethod]
    public void Without_high_contrast_nothing_is_merged()
    {
        HeadlessAppFixture.OnUiThread(() =>
        {
            Application application = Application.Current!;
            int before = application.Resources.MergedDictionaries.Count;

            new AvaloniaThemeApplier(application, highContrast: false).Apply(dark: false);

            Assert.AreEqual(
                before,
                application.Resources.MergedDictionaries.Count,
                "A build that is not in high contrast should merge nothing.");
        });
    }

    [TestMethod]
    public void The_brand_colours_keep_their_literal_values()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The palette is read through GetSysColor; macOS needs its own answer.");
        }

        HeadlessAppFixture.OnUiThread(() =>
        {
            Application application = Application.Current!;
            int before = application.Resources.MergedDictionaries.Count;
            try
            {
                new AvaloniaThemeApplier(application, highContrast: true).Apply(dark: false);

                foreach ((string key, string literal) in HighContrastPalette.Literals)
                {
                    var brush = Lookup(application, key, dark: false) as ISolidColorBrush;
                    Assert.IsNotNull(brush, $"{key} did not resolve to a brush.");
                    Assert.AreEqual(Color.Parse(literal), brush.Color, $"{key} was recoloured.");
                }
            }
            finally
            {
                while (application.Resources.MergedDictionaries.Count > before)
                {
                    application.Resources.MergedDictionaries.RemoveAt(
                        application.Resources.MergedDictionaries.Count - 1);
                }
            }
        });
    }

    private static object? Lookup(Application application, string shortKey, bool dark)
    {
        application.Resources.TryGetResource(
            HighContrastPalette.KeyPrefix + shortKey,
            dark ? ThemeVariant.Dark : ThemeVariant.Light,
            out object? value);
        return value;
    }

    /// <summary>The same colour the palette builder reads, resolved independently of it.</summary>
    private static Color WindowsSystemWindowColor()
    {
        uint packed = GetSysColor(5);
        return Color.FromRgb((byte)(packed & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)((packed >> 16) & 0xFF));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetSysColor(int nIndex);
}
