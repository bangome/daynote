using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Renders the screenshots the landing page at daynote.arachat.cc serves.
/// </summary>
/// <remarks>
/// The same rot that <see cref="StoreScreenshotTests"/> was written to stop had happened to the
/// website unnoticed: on 2026-10-01 <c>cloud/site/public/img/hero-ko.webp</c> still showed the
/// clipboard drawer — deleted in August — the pre-v3 palette, and a note body with a broken
/// <c>[[file:…]]</c> link sitting in it as literal text. The marketing copy had been corrected; the
/// pictures had not, and nothing in the repository compared them.
/// <para>
/// These write PNGs into <c>cloud/site/shots/</c>, which is not what the site serves.
/// <c>node cloud/site/shots.mjs</c> turns them into the <c>.webp</c> and <c>.jpg</c> files under
/// <c>public/img/</c> at the exact sizes <c>index.html</c> declares. Keeping the encode out of the
/// test is deliberate: Avalonia writes PNG, and sharp is already a dependency of the site build.
/// </para>
/// </remarks>
[TestClass]
public sealed class SiteScreenshotTests
{
    /// <summary>What <c>index.html</c> declares for the hero image.</summary>
    private const int HeroWidth = 1046;
    private const int HeroHeight = 714;

    /// <summary>And for the three figures below it.</summary>
    private const int ShotWidth = 1200;
    private const int ShotHeight = 675;

    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "cloud", "site", "shots"));

    [TestMethod]
    public void Korean_site_images() => Capture(AppLanguage.Korean, "ko");

    [TestMethod]
    public void English_site_images() => Capture(AppLanguage.English, "en");

    private static void Capture(AppLanguage language, string lang)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            Directory.CreateDirectory(Root);

            // The hero is its own render rather than a crop of the wide one: the shell lays out
            // differently at 1046 wide, and a crop would show a layout no one is served.
            Shell(language, HeroWidth, HeroHeight, (window, shell) =>
            {
                shell.ActiveTab = RightTab.Todo;
                ListingShots.Shoot(window, Root, $"hero-{lang}", HeroWidth, HeroHeight);
            });

            Shell(language, ShotWidth, ShotHeight, (window, shell) =>
            {
                shell.ActiveTab = RightTab.Todo;
                ListingShots.Shoot(window, Root, $"shot-{lang}-01-overview", ShotWidth, ShotHeight);

                shell.ActiveTab = RightTab.Files;
                shell.IsListMode = true;
                ListingShots.Shoot(window, Root, $"shot-{lang}-03-files", ShotWidth, ShotHeight);
                shell.IsListMode = false;

                // The caption promises "global and in-app shortcuts", which is the settings page
                // rather than anything on the main window.
                shell.SettingsViewModel!.Section = SettingsSection.Shortcuts;
                shell.OpenSettingsCommand.Execute(null);
                ListingShots.Pump(window);
                ListingShots.Shoot(window, Root, $"shot-{lang}-04-shortcuts", ShotWidth, ShotHeight);
                shell.CloseSettingsCommand.Execute(null);
                ListingShots.Pump();
            });
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    /// <summary>A seeded shell at one size; the two site sizes each need their own layout pass.</summary>
    private static void Shell(
        AppLanguage language,
        int width,
        int height,
        Action<Avalonia.Controls.Window, DesktopShellViewModel> body) =>
        TestServices.WithInitialisedShell(width, height, (window, shell) =>
        {
            LocalizationService.Instance.SetLanguage(language);
            ListingShots.Pump(window);
            ListingShots.Seed(shell, language);
            body(window, shell);
        });
}
