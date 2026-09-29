using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Daynote.App.Input;
using Daynote.Desktop.Platform;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>Regressions found in review of the design-B port.</summary>
[TestClass]
public sealed class DesignBReviewFixTests
{
    [TestMethod]
    public void Closing_settings_mid_capture_stops_the_capture()
    {
        // Left running, the window's key filter swallows every key for the capture and the next chord
        // silently rebinds the row. Escape always cancelled it; the scrim and the page list did not.
        TestServices.WithInitialisedShell((_, shell) =>
        {
            DesktopSettingsViewModel settings = shell.SettingsViewModel!;
            shell.OpenSettingsCommand.Execute(null);
            settings.InAppShortcuts[0].StartCaptureCommand.Execute(null);
            Assert.IsTrue(settings.IsCapturing, "The scenario needs a capture running.");

            shell.CloseSettingsCommand.Execute(null);
            Assert.IsFalse(settings.IsCapturing, "The capture outlived the dialog.");

            shell.OpenSettingsCommand.Execute(null);
            settings.StartHotkeyCaptureCommand.Execute(null);
            settings.Section = SettingsSection.Data;
            Assert.IsFalse(settings.IsCapturing, "The capture outlived its page.");
        });
    }

    [TestMethod]
    public void Every_design_brush_has_a_high_contrast_stand_in()
    {
        string markup = File.ReadAllText(Path.Combine(DesktopRoot, "Themes", "Daynote.Desk.axaml"));
        string[] keys =
        [
            .. Regex.Matches(markup, @"x:Key=""Daynote\.Desk\.Brush\.([A-Za-z0-9.]+)""")
                .Select(static m => m.Groups[1].Value)
                .Distinct()
                .Order(),
        ];

        Assert.IsGreaterThan(10, keys.Length, "The scan found suspiciously few design brushes.");
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            keys.Except(DeskHighContrastAliases.Map.Keys).ToArray(),
            "These design brushes would keep their colours in a high-contrast session.");

        ResourceDictionary derived = DeskHighContrastAliases.AddTo(DerivedHighContrastBrushes.Build(dark: false));
        foreach (string key in keys)
        {
            Assert.IsTrue(derived.ContainsKey("Daynote.Desk.Brush." + key), $"{key} was not added to the high-contrast dictionary.");
        }
    }

    [TestMethod]
    public void A_hidden_card_takes_no_slot_in_the_grid()
    {
        HeadlessAppFixture.OnUiThread(() =>
        {
            var grid = new AutoFillGrid { MinItemWidth = 100, Gap = 10 };
            var hidden = new Border { Height = 50, IsVisible = false };
            var shown = new Border { Height = 50 };
            grid.Children.Add(hidden);
            grid.Children.Add(shown);

            grid.Measure(new Size(210, double.PositiveInfinity));
            grid.Arrange(new Rect(0, 0, 210, grid.DesiredSize.Height));

            Assert.AreEqual(0, shown.Bounds.X, "The visible card was pushed along by a hidden one.");
            Assert.AreEqual(50, grid.DesiredSize.Height);
        });
    }

    private static string DesktopRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "Daynote.Desktop")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("No repository root."), "src", "Daynote.Desktop");
        }
    }
}
