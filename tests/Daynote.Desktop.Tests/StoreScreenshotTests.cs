using Daynote.App.Localization;
using Daynote.App.Shell.Product;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Renders the Store listing's screenshots from the shipping shell.
/// </summary>
/// <remarks>
/// A test rather than a script, and that is the point. The four images under
/// <c>docs/brand/microsoft-store/</c> were captured by hand on 2026-07-27 and quietly rotted: they
/// predate the v3 palette and the account window, and one of them showed the clipboard drawer — a
/// feature deleted in August. A listing that advertises a removed feature is a certification
/// problem and a refund problem, and nothing in the repository noticed for six weeks.
/// <para>
/// Rendering them from the real service graph means they cannot drift again without this test
/// rewriting them. Every piece of content is produced by the app rather than drawn over it: the
/// to-do rows come from parsing note bodies, the chips from the tag command, the file cards from
/// the real content-addressed store. A screenshot assembled any other way could show a layout the
/// app cannot actually produce. The seeding and the capture live in <see cref="ListingShots"/>,
/// shared with <see cref="SiteScreenshotTests"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class StoreScreenshotTests
{
    /// <summary>The Store's floor is 1366x768; this clears it and matches a common laptop.</summary>
    private const int Width = 1440;
    private const int Height = 900;

    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "brand", "microsoft-store"));

    [TestMethod]
    public void Korean_listing_images() => Capture(AppLanguage.Korean, Root, "daynote-store-ko");

    [TestMethod]
    public void English_listing_images() =>
        Capture(AppLanguage.English, Path.Combine(Root, "en"), "daynote-store-en");

    private static void Capture(AppLanguage language, string directory, string prefix)
    {
        AppLanguage original = LocalizationService.Instance.Language;
        try
        {
            TestServices.WithInitialisedShell(Width, Height, (window, shell) =>
            {
                LocalizationService.Instance.SetLanguage(language);
                ListingShots.Pump(window);

                ListingShots.Seed(shell, language);
                Directory.CreateDirectory(directory);

                shell.ActiveTab = RightTab.Todo;
                Shoot(window, directory, $"{prefix}-01-overview");

                // The tags and files lists fill the middle of the window, opened from the sidebar.
                shell.ActiveTab = RightTab.Tags;
                shell.IsListMode = true;
                if (shell.TagPanel.Tags.FirstOrDefault() is { } tag)
                {
                    tag.IsExpanded = true;
                }

                Shoot(window, directory, $"{prefix}-02-tags");

                shell.ActiveTab = RightTab.Files;
                Shoot(window, directory, $"{prefix}-03-files");

                shell.IsListMode = false;
                shell.ActiveTab = RightTab.Todo;
                shell.IsDark = true;
                Shoot(window, directory, $"{prefix}-04-dark");
                shell.IsDark = false;

                // What the app sells. Two paid tiers is a thing a listing has to say out loud, and
                // it is the one screen a reviewer looks for when the app takes money.
                ListingShots.ShowPlans(window, shell);
                Shoot(window, directory, $"{prefix}-05-plans");
                shell.CloseSettingsCommand.Execute(null);
                ListingShots.Pump();
            });
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    private static void Shoot(Avalonia.Controls.Window window, string directory, string name) =>
        ListingShots.Shoot(window, directory, name, Width, Height);
}
