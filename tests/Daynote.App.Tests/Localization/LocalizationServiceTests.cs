using System.ComponentModel;
using System.Globalization;
using Daynote.App.Localization;

namespace Daynote.App.Tests.Localization;

/// <summary>
/// Behaviour of the live language switch: notifications, culture, weak observer registration, and
/// the startup resolution that decides which language a profile opens in.
/// </summary>
// Switching languages mutates process-wide state (the active catalog, the thread cultures, and
// the untitled-note format), so these classes must not run alongside tests that read it.
[DoNotParallelize]
[TestClass]
public sealed class LocalizationServiceTests
{
    /// <summary>Korean is the service's initial state; every test restores it so ordering cannot matter.</summary>
    [TestCleanup]
    public void RestoreKorean() => LocalizationService.Instance.SetLanguage(AppLanguage.Korean);

    private sealed class Recorder : ILanguageAware
    {
        public int Count { get; private set; }

        public void OnLanguageChanged() => Count++;
    }

    [TestMethod]
    public void Switching_language_changes_what_the_indexer_returns()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("설정", AppStrings.SettingsTitle);

        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        Assert.AreEqual("Settings", AppStrings.SettingsTitle);
    }

    [TestMethod]
    public void Switching_language_invalidates_every_indexer_binding()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        var raised = new List<string?>();
        void Handler(object? sender, PropertyChangedEventArgs args) => raised.Add(args.PropertyName);
        LocalizationService.Instance.PropertyChanged += Handler;
        try
        {
            LocalizationService.Instance.SetLanguage(AppLanguage.English);
        }
        finally
        {
            LocalizationService.Instance.PropertyChanged -= Handler;
        }

        // "Item[]" is the name WPF listens for to re-read every {loc:Tr} binding in loaded XAML.
        CollectionAssert.Contains(raised, "Item[]");
        CollectionAssert.Contains(raised, nameof(LocalizationService.Language));
    }

    [TestMethod]
    public void Switching_language_notifies_registered_observers()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        var recorder = new Recorder();
        LocalizationService.Instance.Observe(recorder);

        LocalizationService.Instance.SetLanguage(AppLanguage.English);

        Assert.AreEqual(1, recorder.Count);
        GC.KeepAlive(recorder);
    }

    [TestMethod]
    public void Observing_the_same_object_twice_notifies_it_once()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        var recorder = new Recorder();
        LocalizationService.Instance.Observe(recorder);
        LocalizationService.Instance.Observe(recorder);

        LocalizationService.Instance.SetLanguage(AppLanguage.English);

        Assert.AreEqual(1, recorder.Count);
        GC.KeepAlive(recorder);
    }

    [TestMethod]
    public void Redundant_switch_raises_nothing()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        var recorder = new Recorder();
        LocalizationService.Instance.Observe(recorder);

        LocalizationService.Instance.SetLanguage(AppLanguage.English);

        Assert.AreEqual(0, recorder.Count);
        GC.KeepAlive(recorder);
    }

    [TestMethod]
    public void Switching_language_switches_the_formatting_culture()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        Assert.AreEqual("en-US", LocalizationService.Instance.Culture.Name);
        Assert.AreEqual("en-US", CultureInfo.CurrentCulture.Name);

        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("ko-KR", LocalizationService.Instance.Culture.Name);
        Assert.AreEqual("ko-KR", CultureInfo.CurrentCulture.Name);
    }

    [TestMethod]
    public void An_unknown_key_falls_back_to_the_key_rather_than_throwing()
    {
        Assert.AreEqual("NoSuchKey", LocalizationService.Instance["NoSuchKey"]);
    }

    [TestMethod]
    public void Persisted_tags_round_trip()
    {
        Assert.AreEqual(AppLanguage.Korean, AppLanguages.FromTag(AppLanguages.ToTag(AppLanguage.Korean)));
        Assert.AreEqual(AppLanguage.English, AppLanguages.FromTag(AppLanguages.ToTag(AppLanguage.English)));
    }

    [TestMethod]
    public void An_absent_or_unrecognized_tag_means_no_stored_choice()
    {
        Assert.IsNull(AppLanguages.FromTag(null));
        Assert.IsNull(AppLanguages.FromTag(string.Empty));
        Assert.IsNull(AppLanguages.FromTag("fr"));
    }

    [TestMethod]
    public void Only_a_korean_system_culture_defaults_to_korean()
    {
        Assert.AreEqual(AppLanguage.Korean, AppLanguages.FromCulture(CultureInfo.GetCultureInfo("ko-KR")));
        Assert.AreEqual(AppLanguage.English, AppLanguages.FromCulture(CultureInfo.GetCultureInfo("en-GB")));
        Assert.AreEqual(AppLanguage.English, AppLanguages.FromCulture(CultureInfo.GetCultureInfo("ja-JP")));
        Assert.AreEqual(AppLanguage.English, AppLanguages.FromCulture(CultureInfo.InvariantCulture));
    }
}
