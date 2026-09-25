using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The settings page's one interactive control.
/// </summary>
/// <remarks>
/// Written because tapping the theme switch on a Simulator appeared to do nothing, and a headless
/// test says in a second whether the control or the input is at fault.
/// </remarks>
[TestClass]
public sealed class SettingsInteractionTests
{
    [TestMethod]
    public void The_theme_switch_turns_the_dark_theme_on()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            shell.GoToPageCommand.Execute(ViewModels.MobilePage.Settings);
            view.UpdateLayout();

            ToggleSwitch toggle = view.GetLogicalDescendants()
                .OfType<SettingsPage>()
                .Single()
                .GetLogicalDescendants()
                .OfType<ToggleSwitch>()
                .Single();

            Assert.IsFalse(shell.IsDark, "The shell started in the dark theme.");
            Assert.IsTrue(toggle.IsEffectivelyEnabled, "The theme switch is disabled.");
            Assert.IsGreaterThan(0, toggle.Bounds.Width, "The theme switch has no width to tap.");

            // The control's own toggle, which is what a tap ends up calling. This checks the
            // binding, not the hit test.
            toggle.IsChecked = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(shell.IsDark, "Turning the switch on did not reach the shell.");
        });
    }
}
