using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Daynote.App.Localization;
using Daynote.Mobile.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>
/// The settings page's choices: the theme and the language, each a pair of pills.
/// </summary>
/// <remarks>
/// Written because tapping the theme switch on a Simulator appeared to do nothing, and a headless
/// test says in a second whether the control or the input is at fault.
/// </remarks>
[TestClass]
public sealed class SettingsInteractionTests
{
    [TestMethod]
    public void The_dark_pill_turns_the_dark_theme_on()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            shell.GoToPageCommand.Execute(ViewModels.MobilePage.Settings);
            view.UpdateLayout();

            Button dark = Pill(view, shell.Strings["MobileThemeDark"]);

            Assert.IsFalse(shell.IsDark, "The shell started in the dark theme.");
            Assert.IsFalse(dark.Classes.Contains("on"), "The dark pill reads as chosen in the light theme.");
            Assert.IsGreaterThan(0, dark.Bounds.Width, "The dark pill has no width to tap.");

            // The bound command, which is what a tap ends up calling. This checks the binding, not
            // the hit test.
            dark.Command!.Execute(dark.CommandParameter);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(shell.IsDark, "The dark pill did not reach the shell.");
            Assert.IsTrue(dark.Classes.Contains("on"), "The dark pill does not show that it is chosen.");

            Button light = Pill(view, shell.Strings["MobileThemeLight"]);
            light.Command!.Execute(light.CommandParameter);
            Assert.IsFalse(shell.IsDark, "The light pill did not turn the dark theme off.");
        });
    }

    [TestMethod]
    public void The_language_pills_switch_the_language_at_once()
    {
        TestServices.WithInitialisedShell((view, shell) =>
        {
            AppLanguage original = LocalizationService.Instance.Language;
            try
            {
                LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
                shell.GoToPageCommand.Execute(ViewModels.MobilePage.Settings);
                view.UpdateLayout();

                Assert.IsTrue(shell.IsKorean);
                Pump(() => shell.SelectLanguageCommand.ExecuteAsync(AppLanguage.English));

                Assert.AreEqual(AppLanguage.English, LocalizationService.Instance.Language);
                Assert.IsTrue(shell.IsEnglish, "The English pill does not read as chosen.");
                Assert.AreEqual("Theme", shell.Strings["MobileSettingsTheme"], "The page did not switch to English.");
            }
            finally
            {
                LocalizationService.Instance.SetLanguage(original);
            }
        });
    }

    [TestMethod]
    public void A_build_without_sync_says_the_notes_stay_on_the_device()
    {
        TestServices.WithInitialisedShell((_, shell) =>
        {
            Assert.IsFalse(shell.HasAccount);
            Assert.AreEqual(shell.Strings["AccountBarLocalMobile"], shell.AccountCardTitle);
            Assert.AreEqual(shell.Strings["MobileStorageDevice"], shell.StorageText);

            // With no account there is no page behind the card to open.
            shell.OpenAccountCommand.Execute(null);
            Assert.IsFalse(shell.IsAccountOpen);
        });
    }

    private static Button Pill(Control view, string label) => view.GetLogicalDescendants()
        .OfType<SettingsPage>()
        .Single()
        .GetLogicalDescendants()
        .OfType<Button>()
        .Single(b => b.Classes.Contains("pill") && b.Content is TextBlock { Text: { } text } && text == label);

    private static void Pump(Func<Task> work)
    {
        Task task = work();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted)
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, "The command did not complete within 20 seconds.");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
}
