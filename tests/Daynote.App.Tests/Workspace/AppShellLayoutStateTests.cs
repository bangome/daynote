using Daynote.App.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class AppShellLayoutStateTests
{
    private static AppShellLayoutState New() =>
        new(new LayoutThresholds(819, 820, 1199, 1200, 8));

    [TestMethod]
    [DataRow(819.0, AppLayoutState.Compact)]
    [DataRow(820.0, AppLayoutState.Regular)]
    [DataRow(1199.0, AppLayoutState.Regular)]
    [DataRow(1200.0, AppLayoutState.Wide)]
    public void InitialWidth_ClassifiesAtExactThresholds(double width, AppLayoutState expected)
    {
        Assert.AreEqual(expected, New().Update(width));
    }

    [TestMethod]
    public void Hysteresis_RetainsRegularUntilThresholdCrossedByEightDips()
    {
        AppShellLayoutState state = New();
        state.Update(1000);

        Assert.AreEqual(AppLayoutState.Regular, state.Update(1200), "Reaching 1200 is within the anti-flap band.");
        Assert.AreEqual(AppLayoutState.Regular, state.Update(1207));
        Assert.AreEqual(AppLayoutState.Wide, state.Update(1208));
    }

    [TestMethod]
    public void Hysteresis_RetainsWideUntilDroppingBelowThresholdByEightDips()
    {
        AppShellLayoutState state = New();
        state.Update(1300);

        Assert.AreEqual(AppLayoutState.Wide, state.Update(1199));
        Assert.AreEqual(AppLayoutState.Wide, state.Update(1192));
        Assert.AreEqual(AppLayoutState.Regular, state.Update(1191));
    }

    [TestMethod]
    public void Hysteresis_GuardsTheCompactRegularBoundary()
    {
        AppShellLayoutState state = New();
        state.Update(1000);

        Assert.AreEqual(AppLayoutState.Regular, state.Update(819), "Dropping to 819 is within the anti-flap band.");
        Assert.AreEqual(AppLayoutState.Compact, state.Update(811));
        Assert.AreEqual(AppLayoutState.Compact, state.Update(827));
        Assert.AreEqual(AppLayoutState.Regular, state.Update(828));
    }
}
