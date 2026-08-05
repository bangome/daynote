using Daynote.App.Localization;
using Daynote.App.Tests.Lifecycle;
using Daynote.Core.Settings;

namespace Daynote.App.Tests.Localization;

/// <summary>
/// Startup language resolution: a persisted choice wins, and a profile that has never chosen one
/// follows the Windows display language.
/// </summary>
// Switching languages mutates process-wide state (the active catalog, the thread cultures, and
// the untitled-note format), so these classes must not run alongside tests that read it.
[DoNotParallelize]
[TestClass]
public sealed class LanguageStartupTests
{
    [TestCleanup]
    public void RestoreKorean() => LocalizationService.Instance.SetLanguage(AppLanguage.Korean);

    [TestMethod]
    public async Task A_persisted_choice_wins_over_the_system_language()
    {
        var store = new InMemorySettingsStore();
        await store.SetAsync(UiSettings.LanguageKey, AppLanguages.ToTag(AppLanguage.English));

        Assert.AreEqual(AppLanguage.English, await LanguageStartup.ResolveAsync(store));

        await store.SetAsync(UiSettings.LanguageKey, AppLanguages.ToTag(AppLanguage.Korean));

        Assert.AreEqual(AppLanguage.Korean, await LanguageStartup.ResolveAsync(store));
    }

    [TestMethod]
    public async Task A_profile_with_no_stored_choice_follows_the_system_language()
    {
        var store = new InMemorySettingsStore();

        Assert.AreEqual(AppLanguages.FromSystem(), await LanguageStartup.ResolveAsync(store));
    }

    [TestMethod]
    public async Task An_unrecognized_stored_tag_falls_back_to_the_system_language()
    {
        var store = new InMemorySettingsStore();
        await store.SetAsync(UiSettings.LanguageKey, "de");

        Assert.AreEqual(AppLanguages.FromSystem(), await LanguageStartup.ResolveAsync(store));
    }

    [TestMethod]
    public async Task Applying_the_startup_language_activates_it()
    {
        var store = new InMemorySettingsStore();
        await store.SetAsync(UiSettings.LanguageKey, AppLanguages.ToTag(AppLanguage.English));

        await LanguageStartup.ApplyAsync(store);

        Assert.AreEqual(AppLanguage.English, LocalizationService.Instance.Language);
        Assert.AreEqual("Settings", AppStrings.SettingsTitle);
    }
}
