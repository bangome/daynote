using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Daynote.App.Onboarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Onboarding;

/// <summary>
/// The tutorial's spotlight is cut with the corner radius of the element it spotlights.
/// </summary>
/// <remarks>
/// It used to be a fixed 8, which matched the buttons and cut across the curve of the 10px panels
/// and the 14px editor card. Each tutorial target has a different chrome — a Border of its own, a
/// templated Button, a UserControl, a bare TextBox inside a pill — so the radius has to be read
/// from what is actually drawn, and each of those shapes is a case here.
/// </remarks>
[TestClass]
public sealed class TutorialSpotlightTests
{
    [STATestMethod]
    public void A_border_reports_its_own_radius()
    {
        var panel = new Border { CornerRadius = new CornerRadius(14), Width = 100, Height = 40 };

        Assert.AreEqual(14, TutorialView.SpotlightRadius(panel));
    }

    [STATestMethod]
    public void A_templated_control_reports_the_radius_of_its_chrome()
    {
        // A Button drawn by a template whose root is a rounded Border, the way the product styles do.
        const string template = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="Button">
              <Border CornerRadius="9" Background="Gray">
                <ContentPresenter />
              </Border>
            </ControlTemplate>
            """;
        var button = new Button { Template = (ControlTemplate)XamlReader.Parse(template), Width = 80, Height = 30 };
        button.ApplyTemplate();

        Assert.AreEqual(9, TutorialView.SpotlightRadius(button));
    }

    [STATestMethod]
    public void A_bare_control_inside_a_pill_reports_the_pill()
    {
        // The search box: a plain TextBox whose rounded shape is the Border around it.
        var box = new TextBox { Width = 200, Height = 28 };
        _ = new Border { CornerRadius = new CornerRadius(13), Child = box };

        Assert.AreEqual(13, TutorialView.SpotlightRadius(box));
    }

    [STATestMethod]
    public void Anything_else_falls_back_to_the_control_radius()
    {
        // A square element with no rounded chrome anywhere around it: the default rather than 0, so
        // the hole is never sharp-cornered against a UI that has no sharp corners.
        var grid = new Grid { Width = 50, Height = 50 };

        Assert.AreEqual(8, TutorialView.SpotlightRadius(grid));
    }
}
