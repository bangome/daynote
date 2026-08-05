using System.Windows;
using System.Windows.Controls;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class ShowcaseRuntimeCompositionTests
{
    [STATestMethod]
    public void EveryManifestPage_ComposesAndCompletesWpfLayoutWithRealResources()
    {
        Assert.AreEqual(381, ShowcaseManifest.Pages.Count, "The complete Section 5 layout/state matrix changed.");
        var application = System.Windows.Application.Current ?? new System.Windows.Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
        var normalMotion = DaynoteMotionPolicy.ForShowcase(reducedMotion: false);
        var reducedMotion = DaynoteMotionPolicy.ForShowcase(reducedMotion: true);
        Assert.IsFalse(normalMotion.ReducedMotion);
        Assert.IsTrue(reducedMotion.ReducedMotion);
        Assert.IsGreaterThan(TimeSpan.Zero, normalMotion.Duration("Daynote.Motion.Panel").TimeSpan);
        Assert.AreEqual(TimeSpan.Zero, reducedMotion.Duration("Daynote.Motion.Panel").TimeSpan);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), normalMotion.EvidenceMidpoint);
        var failures = new List<string>();

        foreach (var page in ShowcaseManifest.Pages)
        {
            try
            {
                var selection = new ShowcaseSelection(
                    page,
                    ShowcasePalette.Standard,
                    ShowcaseMotion.Reduced,
                    ShowcaseStress.Default,
                    ShowcaseFrame.Settled);
                var surface = new ShowcaseComposer().Compose(selection);
                surface.Measure(new Size(1586, 992));
                surface.Arrange(new Rect(0, 0, 1586, 992));
                ApplyTemplates(surface);
                surface.UpdateLayout();

                Assert.IsGreaterThan(0, surface.DesiredSize.Width, page.Id);
                Assert.IsGreaterThan(0, surface.DesiredSize.Height, page.Id);
            }
            catch (Exception exception)
            {
                failures.Add($"{page.Id}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        application.Resources.MergedDictionaries.Clear();
        Assert.AreEqual(0, failures.Count, $"WPF composition failures:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static void ApplyTemplates(DependencyObject root)
    {
        if (root is Control control)
            control.ApplyTemplate();

        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            ApplyTemplates(System.Windows.Media.VisualTreeHelper.GetChild(root, index));
    }
}
