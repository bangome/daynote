using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.App.Tests.Localization;

/// <summary>
/// The default title for a note the user never named lives in Core, which cannot reference the
/// app's string catalog — the localization service pushes the translated format down instead.
/// These tests cover that hand-off in both directions.
/// </summary>
// Switching languages mutates process-wide state (the active catalog, the thread cultures, and
// the untitled-note format), so these classes must not run alongside tests that read it.
[DoNotParallelize]
[TestClass]
public sealed class UntitledNoteLocalizationTests
{
    [TestCleanup]
    public void RestoreKorean() => LocalizationService.Instance.SetLanguage(AppLanguage.Korean);

    /// <summary>An empty day's single projection note — the canonical never-titled note.</summary>
    private static Note Untitled() =>
        NoteSet.Empty(LocalDate.Parse("2026-07-27").Value).Notes[0];

    [TestMethod]
    public void Switching_language_retitles_untitled_notes()
    {
        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("노트 1", Untitled().Title);

        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        Assert.AreEqual("Note 1", Untitled().Title);

        LocalizationService.Instance.SetLanguage(AppLanguage.Korean);
        Assert.AreEqual("노트 1", Untitled().Title);
    }

    [TestMethod]
    public void A_format_without_the_number_placeholder_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UntitledNote.Format = "Note");
        Assert.ThrowsExactly<ArgumentException>(() => UntitledNote.Format = "  ");

        // The rejected assignments must not have disturbed the active format.
        Assert.AreEqual("노트 1", UntitledNote.TitleFor(1));
    }
}
