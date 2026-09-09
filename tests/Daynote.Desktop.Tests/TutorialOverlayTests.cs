using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Daynote.App.Onboarding;
using Daynote.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// The coaching overlay dims the shell and cuts a hole around each step's target.
/// </summary>
/// <remarks>
/// This shell had no spotlight at all until the port — one flat scrim and a centred card — so the
/// thing to pin is that a hole is actually cut, that it follows the target rather than a fixed
/// rectangle, and that the radius comes from the target's own chrome. Every step's target name has
/// to resolve in this window too: a name that does not exist silently degrades to "no hole", which
/// looks like the old behaviour and would go unnoticed.
/// </remarks>
[TestClass]
public sealed class TutorialOverlayTests
{
    [TestMethod]
    public void Every_step_target_resolves_in_the_shell()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            string[] missing =
            [
                .. TutorialSteps()
                    .Select(static step => step.TargetName)
                    .Where(static name => name is { Length: > 0 })
                    .Distinct(StringComparer.Ordinal)
                    .Where(name => window.FindControl<Control>(name!) is null)!,
            ];

            CollectionAssert.AreEqual(
                Array.Empty<string>(),
                missing,
                "These tutorial targets have no element in this shell, so their steps would show no "
                    + $"spotlight: {string.Join(", ", missing)}");
        });
    }

    [TestMethod]
    public void A_step_with_a_target_cuts_a_hole_that_follows_it()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            TutorialOverlay overlay = Overlay(window);
            var scrim = overlay.FindControl<Avalonia.Controls.Shapes.Path>("Scrim")!;
            var ring = overlay.FindControl<Border>("HighlightRing")!;

            // The calendar step: a panel, so the hole should be panel-rounded and sit over the
            // calendar rather than anywhere else.
            Rect calendar = Bounds(window, "TutCalendar");
            ShowStep(window, overlay, TutorialTargets.Calendar);

            Assert.IsInstanceOfType<CombinedGeometry>(
                scrim.Data,
                "No hole was cut; the scrim is a plain rectangle, which is the pre-port behaviour.");
            Assert.IsTrue(ring.IsVisible, "The target is not ringed.");

            var combined = (CombinedGeometry)scrim.Data!;
            Assert.AreEqual(GeometryCombineMode.Exclude, combined.GeometryCombineMode);
            Rect hole = combined.Geometry2!.Bounds;
            Assert.IsTrue(
                hole.Intersects(calendar) && hole.Width >= calendar.Width && hole.Height >= calendar.Height,
                $"The hole {hole} does not cover the calendar {calendar}.");

            // A different target moves it. The search pill is in the title bar, so a hole that stayed
            // put would be obvious rather than a near miss.
            Rect search = Bounds(window, "TutSearch");
            ShowStep(window, overlay, TutorialTargets.Search);
            Rect moved = ((CombinedGeometry)scrim.Data!).Geometry2!.Bounds;
            Assert.AreNotEqual(hole, moved, "The hole did not follow the step to a different target.");
            Assert.IsTrue(moved.Intersects(search), $"The hole {moved} is not over the search pill {search}.");
        });
    }

    [TestMethod]
    public void A_step_with_no_target_dims_everything_and_rings_nothing()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            TutorialOverlay overlay = Overlay(window);
            var scrim = overlay.FindControl<Avalonia.Controls.Shapes.Path>("Scrim")!;
            var ring = overlay.FindControl<Border>("HighlightRing")!;

            ShowStep(window, overlay, target: null);

            Assert.IsNotInstanceOfType<CombinedGeometry>(scrim.Data, "The welcome card should dim everything.");
            Assert.IsFalse(ring.IsVisible, "Nothing is spotlighted, so nothing should be ringed.");
        });
    }

    [TestMethod]
    public void The_radius_comes_from_the_target_rather_than_a_constant()
    {
        TestServices.WithInitialisedShell((window, shell) =>
        {
            // The panels and the pill draw with different radii, so the spotlight must too — a fixed
            // value is what cut across the curve in the WPF shell before this rule existed.
            double calendar = TutorialOverlay.SpotlightRadius(window.FindControl<Control>("TutCalendar")!);
            double search = TutorialOverlay.SpotlightRadius(window.FindControl<Control>("TutSearch")!);

            Assert.IsGreaterThan(0, calendar, "The calendar panel reported no radius.");
            Assert.IsGreaterThan(0, search, "The search strip reported no radius.");
            Assert.AreNotEqual(calendar, search, "Every target reported the same radius; the rule is not reading the chrome.");
        });
    }

    /// <summary>The step deck, built from the same view model the shell uses.</summary>
    private static IReadOnlyList<TutorialStep> TutorialSteps()
    {
        var settings = new EmptySettings();
        return new TutorialViewModel(
            settings,
            new Daynote.App.Input.ConfigurableShortcuts(settings),
            new UnavailableStartup()).Steps;
    }

    /// <summary>Reads nothing and remembers nothing; the deck does not depend on stored state.</summary>
    private sealed class EmptySettings : Daynote.Core.Settings.ISettingsStore
    {
        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(fallback);

        public ValueTask SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class UnavailableStartup : Daynote.Core.Startup.IStartupTaskService
    {
        public ValueTask<Daynote.Core.Startup.StartupTaskState> GetStateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Daynote.Core.Startup.StartupTaskState.Unavailable);

        public ValueTask<Daynote.Core.Startup.StartupEnableResult> RequestEnableAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Daynote.Core.Startup.StartupEnableResult(Daynote.Core.Startup.StartupTaskState.Unavailable, false));

        public ValueTask<Daynote.Core.Startup.StartupEnableResult> RequestDisableAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Daynote.Core.Startup.StartupEnableResult(Daynote.Core.Startup.StartupTaskState.Unavailable, false));
    }

    private static TutorialOverlay Overlay(Window window) =>
        window.GetVisualDescendants().OfType<TutorialOverlay>().Single();

    private static Rect Bounds(Window window, string name)
    {
        var element = window.FindControl<Control>(name)!;
        return new Rect(element.TranslatePoint(default, window)!.Value, element.Bounds.Size);
    }

    /// <summary>Puts the overlay on the step whose target is <paramref name="target"/> and lays out.</summary>
    private static void ShowStep(Window window, TutorialOverlay overlay, string? target)
    {
        var model = (TutorialViewModel)overlay.DataContext!;
        model.Open();
        model.Index = model.Steps
            .Select(static (step, index) => (step, index))
            .First(pair => pair.step.TargetName == target)
            .index;

        overlay.IsVisible = true;
        window.UpdateLayout();

        // The overlay defers its measurement a dispatcher pass, because a target's layout is not
        // settled when the step changes.
        for (int i = 0; i < 10; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }
}
