using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class ShowcaseTypographyTests
{
    [STATestMethod]
    public void ShowcaseHeaderPaneTitle_UsesCompleteDeclaredTypographyRole()
    {
        var application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);

        var selection = new ShowcaseSelection(
            ShowcaseManifest.FindPage("wide.button.default"),
            ShowcasePalette.Standard,
            ShowcaseMotion.Reduced,
            ShowcaseStress.Default,
            ShowcaseFrame.Settled);
        var surface = (Grid)new ShowcaseComposer().Compose(selection);
        var header = (StackPanel)surface.Children[0];
        var title = (TextBlock)header.Children[0];
        var role = ShowcaseResources.Get<Style>("Daynote.Style.Type.PaneTitle");
        var display = ShowcaseResources.Get<System.Windows.Media.FontFamily>("Daynote.FontFamily.Display");

        Assert.AreEqual("Primitive name", AutomationProperties.GetName(title));
        Assert.AreSame(role, title.Style);
        Assert.AreEqual(display.Source, title.FontFamily.Source);
        Assert.AreEqual(ShowcaseResources.Get<double>("Daynote.Type.PaneTitle.FontSize"), title.FontSize);
        Assert.AreEqual(ShowcaseResources.Get<FontWeight>("Daynote.Type.PaneTitle.FontWeight"), title.FontWeight);
        Assert.AreEqual(ShowcaseResources.Get<double>("Daynote.Type.PaneTitle.LineHeight"), title.LineHeight);
        Assert.AreEqual(new Thickness(), title.Margin);
    }
}
