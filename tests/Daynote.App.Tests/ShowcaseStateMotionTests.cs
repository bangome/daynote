using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class ShowcaseStateMotionTests
{
    [STATestMethod]
    [DataRow("wide.button.default", "default")]
    [DataRow("wide.button.hover", "hover")]
    [DataRow("wide.button.active", "active")]
    [DataRow("wide.button.focus", "focus")]
    [DataRow("wide.button.disabled", "disabled")]
    [DataRow("wide.button.loading", "loading")]
    [DataRow("wide.button.error", "error")]
    public void Compose_ForcesStateOnActualPrimaryButton(string pageId, string state)
    {
        EnsureResources();
        var surface = Compose(pageId, ShowcaseMotion.Reduced, ShowcaseFrame.Settled);
        Layout(surface);

        var target = Descendants(surface).OfType<Button>()
            .Single(element => ShowcaseForcedState.GetIsTarget(element));

        Assert.AreEqual("Primary button", System.Windows.Automation.AutomationProperties.GetName(target));
        Assert.AreEqual(state, ShowcaseForcedState.GetState(target));
        Assert.AreNotEqual(DependencyProperty.UnsetValue, target.ReadLocalValue(ShowcaseForcedState.StateProperty));
        Assert.IsFalse(ShowcaseForcedState.GetIsTarget(surface));
        if (state == "disabled")
            Assert.IsFalse(target.IsEnabled);
        if (state == "loading")
            Assert.AreEqual("Busy", target.Tag);
    }

    [STATestMethod]
    public void WorkspaceFocus_KeepsSelectionAndFocusAsDistinctTargetProperties()
    {
        EnsureResources();
        var surface = Compose("wide.workspace-view-switch.focus", ShowcaseMotion.Reduced, ShowcaseFrame.Settled);
        Layout(surface);
        var items = Descendants(surface).OfType<TabItem>().ToArray();
        var focused = items.Single(ShowcaseForcedState.GetIsTarget);
        var selected = items.Single(item => item.IsSelected);

        Assert.AreNotSame(selected, focused, "Persistent selection must not substitute for forced keyboard focus.");
        Assert.AreEqual("focus", ShowcaseForcedState.GetState(focused));
        Assert.IsTrue(ShowcaseFocus.GetIsPreferred(focused));
    }

    [STATestMethod]
    public void ButtonStates_ChangeTheActualPrimitiveVisualProperties()
    {
        EnsureResources();
        var baseline = ButtonVisual("wide.button.default");
        var hover = ButtonVisual("wide.button.hover");
        var active = ButtonVisual("wide.button.active");
        var focus = ButtonVisual("wide.button.focus");

        Assert.AreNotEqual(baseline.Background, hover.Background);
        Assert.AreNotEqual(hover.Background, active.Background);
        Assert.AreNotEqual(baseline.Border, focus.Border);
        Assert.IsGreaterThan(baseline.BorderThickness, focus.BorderThickness);
    }

    [TestMethod]
    public void MotionProfiles_MapEachAnimatedPrimitiveToItsDesignToken()
    {
        var expected = new Dictionary<string, (string Duration, bool Translate)>
        {
            ["app-shell"] = ("Daynote.Motion.Scope", false),
            ["date-header"] = ("Daynote.Motion.Scope", false),
            ["note-tab"] = ("Daynote.Motion.Micro", false),
            ["editor-toolbar"] = ("Daynote.Motion.Micro", false),
            ["clipboard-item"] = ("Daynote.Motion.Micro", false),
            ["sidebar-note-list"] = ("Daynote.Motion.Micro", false),
            ["clipboard-drawer"] = ("Daynote.Motion.Panel", true),
            ["search"] = ("Daynote.Motion.Panel", true),
            ["status-banner"] = ("Daynote.Motion.Micro", false),
            ["consent-panel"] = ("Daynote.Motion.Panel", false),
            ["settings-row"] = ("Daynote.Motion.Micro", false)
        };

        foreach (var primitive in ShowcaseManifest.Primitives)
        {
            var profile = ShowcaseMotionProfile.For(primitive.Id);
            if (expected.TryGetValue(primitive.Id, out var contract))
            {
                Assert.AreEqual(contract.Duration, profile.DurationResourceKey, primitive.Id);
                Assert.AreEqual(contract.Translate, profile.Translates, primitive.Id);
            }
            else
            {
                Assert.AreEqual("Daynote.Motion.Instant", profile.DurationResourceKey, primitive.Id);
                Assert.IsFalse(profile.Translates, primitive.Id);
            }
        }
    }

    [STATestMethod]
    public void MotionFrames_UseExactRegisteredSamplesAndReducedInstantTransition()
    {
        EnsureResources();
        var rest = MotionValues("wide.search.default", ShowcaseMotion.Normal, ShowcaseFrame.Rest);
        var midpoint = MotionValues("wide.search.default", ShowcaseMotion.Normal, ShowcaseFrame.Midpoint);
        var settled = MotionValues("wide.search.default", ShowcaseMotion.Normal, ShowcaseFrame.Settled);
        var reducedRest = MotionValues("wide.search.default", ShowcaseMotion.Reduced, ShowcaseFrame.Rest);
        var reducedMidpoint = MotionValues("wide.search.default", ShowcaseMotion.Reduced, ShowcaseFrame.Midpoint);
        var reducedSettled = MotionValues("wide.search.default", ShowcaseMotion.Reduced, ShowcaseFrame.Settled);
        var duration = ShowcaseResources.Get<Duration>("Daynote.Motion.Panel").TimeSpan;
        var sampleTime = ShowcaseResources.Get<Duration>("Daynote.Motion.Evidence.Midpoint").TimeSpan;
        var easing = ShowcaseResources.Get<IEasingFunction>("Daynote.Motion.Panel.Easing");
        var offset = ShowcaseResources.Get<double>("Daynote.Motion.Offset.Subtle");
        var expectedMidpoint = easing.Ease(sampleTime.TotalMilliseconds / duration.TotalMilliseconds);

        Assert.AreEqual(0, rest.Opacity);
        Assert.AreEqual(offset, rest.Offset);
        Assert.AreEqual(TimeSpan.Zero, rest.SampleTime);
        Assert.IsTrue(rest.PreAction);
        Assert.AreEqual(expectedMidpoint, midpoint.Opacity, 1e-12);
        Assert.AreEqual(offset * (1 - expectedMidpoint), midpoint.Offset, 1e-12);
        Assert.AreEqual(sampleTime, midpoint.SampleTime);
        Assert.AreEqual((1d, 0d), (settled.Opacity, settled.Offset));
        Assert.AreEqual(duration, settled.SampleTime);
        Assert.AreEqual((rest.Opacity, rest.Offset), (reducedRest.Opacity, reducedRest.Offset));
        Assert.IsTrue(reducedRest.PreAction);
        Assert.AreEqual((1d, 0d), (reducedMidpoint.Opacity, reducedMidpoint.Offset));
        Assert.AreEqual(TimeSpan.Zero, reducedMidpoint.SampleTime);
        Assert.IsFalse(reducedMidpoint.PreAction);
        Assert.AreEqual(reducedMidpoint, reducedSettled);
        Assert.IsFalse(rest.HasAnimation || midpoint.HasAnimation || settled.HasAnimation ||
                       reducedRest.HasAnimation || reducedMidpoint.HasAnimation || reducedSettled.HasAnimation);
    }

    [STATestMethod]
    public void MotionRaster_RendersPreActionThenInkAndSharesSettledPrimitive()
    {
        EnsureResources();
        var rest = MotionTarget(ShowcaseMotion.Normal, ShowcaseFrame.Rest);
        var midpoint = MotionTarget(ShowcaseMotion.Normal, ShowcaseFrame.Midpoint);
        var normalSettled = MotionTarget(ShowcaseMotion.Normal, ShowcaseFrame.Settled);
        var reducedSettled = MotionTarget(ShowcaseMotion.Reduced, ShowcaseFrame.Settled);
        var restPixels = Render(rest);
        var midpointPixels = Render(midpoint);
        var normalSettledPixels = Render(normalSettled);
        var reducedSettledPixels = Render(reducedSettled);

        Assert.IsTrue(restPixels.All(channel => channel == 0), "Rest must be the transparent pre-action primitive.");
        Assert.IsGreaterThan(200, midpointPixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0));
        CollectionAssert.AreEqual(normalSettledPixels, reducedSettledPixels);
    }

    private static byte[] Render(FrameworkElement target)
    {
        var width = (int)Math.Ceiling(target.ActualWidth);
        var height = (int)Math.Ceiling(target.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(target);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static (double Opacity, double Offset, TimeSpan SampleTime, bool PreAction, bool HasAnimation) MotionValues(
        string pageId, ShowcaseMotion motion, ShowcaseFrame frame)
    {
        var target = MotionTarget(motion, frame, pageId);
        var offset = target.RenderTransform is TranslateTransform transform ? transform.Y : 0;
        return (target.Opacity, offset, ShowcaseMotionTarget.GetSampleTime(target),
            ShowcaseMotionTarget.GetIsPreAction(target), target.HasAnimatedProperties);
    }

    private static FrameworkElement MotionTarget(
        ShowcaseMotion motion, ShowcaseFrame frame, string pageId = "wide.search.default")
    {
        var surface = Compose(pageId, motion, frame);
        Layout(surface);
        return Descendants(surface).OfType<FrameworkElement>().Single(ShowcaseMotionTarget.GetIsTarget);
    }

    private static (Color Background, Color Border, double BorderThickness) ButtonVisual(string pageId)
    {
        var surface = Compose(pageId, ShowcaseMotion.Reduced, ShowcaseFrame.Settled);
        Layout(surface);
        var target = Descendants(surface).OfType<Button>()
            .Single(ShowcaseForcedState.GetIsTarget);
        return (((SolidColorBrush)target.Background).Color,
            ((SolidColorBrush)target.BorderBrush).Color,
            target.BorderThickness.Left);
    }

    private static FrameworkElement Compose(string pageId, ShowcaseMotion motion, ShowcaseFrame frame) =>
        new ShowcaseComposer().Compose(new ShowcaseSelection(
            ShowcaseManifest.FindPage(pageId), ShowcasePalette.Standard, motion, ShowcaseStress.Default, frame));

    private static void EnsureResources()
    {
        var application = System.Windows.Application.Current ?? new System.Windows.Application();
        application.Resources.MergedDictionaries.Clear();
        ShowcaseResources.Load(application, highContrast: false);
    }

    private static void Layout(FrameworkElement surface)
    {
        surface.Measure(new Size(1200, 600));
        surface.Arrange(new Rect(0, 0, 1200, 600));
        surface.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var descendant in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return descendant;
        }
    }
}
