using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WpfPath = System.Windows.Shapes.Path;

namespace Daynote.App.Tests;

[TestClass]
public sealed class PrimitiveFixtureSemanticsTests
{
    [STATestMethod]
    [DataRow(ShowcaseStress.Cjk, "입력기")]
    [DataRow(ShowcaseStress.Long, "deliberately long")]
    [DataRow(ShowcaseStress.Unbroken, "ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    public void AppShell_WhenStressSelected_RendersDistinctBoundedPayload(
        ShowcaseStress stress,
        string expected)
    {
        var surface = Compose("wide.app-shell.default", stress, 1586, 992);
        var renderedText = string.Join("\n", Descendants(surface)
            .Select(element => element switch
            {
                TextBlock text => text.Text,
                TextBox text => text.Text,
                _ => string.Empty
            }));

        StringAssert.Contains(renderedText, expected);
        foreach (var textBox in Descendants(surface).OfType<TextBox>())
        {
            Assert.IsLessThanOrEqualTo(1586, textBox.ActualWidth, AutomationProperties.GetName(textBox));
            Assert.AreNotEqual(ScrollBarVisibility.Auto, textBox.HorizontalScrollBarVisibility);
        }
    }

    [STATestMethod]
    public void CompactCjkEditor_StartsAtCommittedFixtureTopWithoutFalseCandidateClaim()
    {
        var surface = Compose("compact.app-shell.default", ShowcaseStress.Cjk, 760, 600);
        var editor = Descendants(surface).OfType<TextBox>()
            .Single(text => AutomationProperties.GetName(text).StartsWith("Markdown editor", StringComparison.Ordinal));

        Assert.AreEqual(0, editor.CaretIndex);
        Assert.AreEqual(VerticalAlignment.Top, editor.VerticalContentAlignment);
        StringAssert.Contains(editor.Text, "입력기 조합 시험");
        StringAssert.Contains(AutomationProperties.GetHelpText(editor), "candidate window require later interaction QA");
        Assert.IsFalse(AutomationProperties.GetHelpText(editor).Contains("candidate interaction verified", StringComparison.OrdinalIgnoreCase));
    }

    [STATestMethod]
    public void FormattingAndSettingsCommands_UseRegisteredPathsInsidePrimaryTargets()
    {
        var toolbar = Compose("wide.editor-toolbar.default", ShowcaseStress.Default, 480, 320);
        var formatting = Descendants(toolbar).OfType<Button>()
            .Where(button => new[] { "Bold", "Italic", "Bulleted list", "Numbered list", "Inline code" }
                .Contains(AutomationProperties.GetName(button), StringComparer.Ordinal))
            .ToArray();
        Assert.HasCount(5, formatting);
        foreach (var button in formatting)
        {
            Assert.IsInstanceOfType<WpfPath>(button.Content);
            Assert.IsNotNull(((WpfPath)button.Content).Data, AutomationProperties.GetName(button));
            Assert.IsGreaterThanOrEqualTo(44, button.ActualWidth, AutomationProperties.GetName(button));
            Assert.IsGreaterThanOrEqualTo(44, button.ActualHeight, AutomationProperties.GetName(button));
            Assert.AreEqual(AutomationProperties.GetName(button), ToolTipService.GetToolTip(button));
            Assert.IsLessThanOrEqualTo(480.5, button.TranslatePoint(new Point(button.ActualWidth, 0), toolbar).X,
                AutomationProperties.GetName(button));
        }

        var buttons = Compose("wide.button.default", ShowcaseStress.Default, 720, 320);
        var settings = Descendants(buttons).OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == "Settings icon button");
        Assert.IsInstanceOfType<WpfPath>(settings.Content);
        Assert.IsNotNull(((WpfPath)settings.Content).Data);
        Assert.AreEqual("Settings icon button", ToolTipService.GetToolTip(settings));
    }

    [STATestMethod]
    public void WorkspaceAndNoteSelectors_ExposeNativeTabAutomationSemantics()
    {
        var switchSurface = Compose("compact.workspace-view-switch.default", ShowcaseStress.Default, 760, 600);
        var tabControl = Descendants(switchSurface).OfType<TabControl>().Single();
        var tabPeer = new TabControlAutomationPeer(tabControl);
        Assert.AreEqual(AutomationControlType.Tab, tabPeer.GetAutomationControlType());
        Assert.HasCount(3, tabControl.Items);
        foreach (var item in tabControl.Items.OfType<TabItem>())
            Assert.AreEqual(
                AutomationControlType.TabItem,
                new TabItemAutomationPeer(item, tabPeer).GetAutomationControlType());

        var noteSurface = Compose("wide.note-tab.default", ShowcaseStress.Long, 720, 320);
        var noteTabs = Descendants(noteSurface).OfType<TabControl>().Single();
        Assert.IsInstanceOfType<TabItem>(noteTabs.Items[0]);
        Assert.IsTrue(((TabItem)noteTabs.Items[0]).IsSelected);
    }

    [STATestMethod]
    public void CalendarWeek_KeepsSevenPrimaryTargetsAndCuesInsideWideRail()
    {
        var surface = Compose("wide.app-shell.default", ShowcaseStress.Default, 1586, 992);
        var days = Descendants(surface).OfType<System.Windows.Controls.Primitives.ToggleButton>()
            .Where(day => AutomationProperties.GetName(day).StartsWith("July ", StringComparison.Ordinal))
            .ToArray();

        Assert.HasCount(7, days);
        foreach (var day in days)
        {
            Assert.IsGreaterThanOrEqualTo(44, day.ActualWidth, AutomationProperties.GetName(day));
            Assert.IsGreaterThanOrEqualTo(44, day.ActualHeight, AutomationProperties.GetName(day));
            var parent = (FrameworkElement)VisualTreeHelper.GetParent(day);
            Assert.IsLessThanOrEqualTo(parent.ActualWidth + 0.5, day.TranslatePoint(new Point(day.ActualWidth, 0), parent).X,
                AutomationProperties.GetName(day));
        }
    }

    [STATestMethod]
    public void AppShellPaneTitles_UseDeclaredDisplayTypographyRole()
    {
        var surface = Compose("wide.app-shell.default", ShowcaseStress.Cjk, 1586, 992);
        var paneTitles = Descendants(surface).OfType<TextBlock>()
            .Where(text => AutomationProperties.GetName(text) is "Calendar month")
            .ToArray();

        Assert.HasCount(1, paneTitles);
        foreach (var title in paneTitles)
        {
            StringAssert.Contains(title.FontFamily.Source, "Segoe UI Variable Display");
            Assert.AreEqual(22, title.FontSize);
            Assert.AreEqual(new Thickness(), title.Margin);
        }
    }

    private static FrameworkElement Compose(string pageId, ShowcaseStress stress, double width, double height)
    {
        var application = Application.Current ?? new Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        var selection = new ShowcaseSelection(
            ShowcaseManifest.FindPage(pageId),
            ShowcasePalette.Standard,
            ShowcaseMotion.Reduced,
            stress,
            ShowcaseFrame.Settled);
        var surface = new ShowcaseComposer().Compose(selection);
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        ApplyTemplates(surface);
        surface.UpdateLayout();
        return surface;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }

    private static void ApplyTemplates(DependencyObject root)
    {
        if (root is Control control)
            control.ApplyTemplate();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            ApplyTemplates(VisualTreeHelper.GetChild(root, index));
    }
}
