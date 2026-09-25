using Daynote.App.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Desktop.Tests;

/// <summary>
/// Counted copy inflects in English and does not in Korean.
/// </summary>
/// <remarks>
/// Written after the phone app shipped a day header reading "1 notes", which is a third of the days
/// anyone opens. The same single-form string was behind five other sentences, including one whose
/// verb changes too ("1 note was replaced" against "4 notes were replaced").
/// <para>
/// The language is a process-wide singleton, so each case restores it. These run in the desktop test
/// project because that is where the other catalog test lives, but nothing here is desktop-specific:
/// the copy is shared by the WPF app, the Avalonia app and both phones.
/// </para>
/// </remarks>
[TestClass]
public sealed class CountedCopyTests
{
    [TestMethod]
    public void English_uses_the_singular_for_exactly_one()
    {
        InLanguage(AppLanguage.English, () =>
        {
            Assert.AreEqual("1 note", AppStrings.NoteCount(1));
            Assert.AreEqual("1 note", AppStrings.SearchDateNoteCount(1));
            Assert.AreEqual("1 result", AppStrings.SearchResultsCount(1));
            Assert.AreEqual("1 day left in your free trial.", AppStrings.BillingTrial(1));
            Assert.AreEqual("1 day left in your Pro trial", AppStrings.BillingTrialBannerTitle(1));
            StringAssert.StartsWith(AppStrings.AccountConflicts(1), "1 note was replaced");
        });
    }

    [TestMethod]
    public void English_uses_the_plural_for_none_and_for_many()
    {
        InLanguage(AppLanguage.English, () =>
        {
            // Zero takes the plural in English, which is why the rule is "one or not one" rather
            // than "more than one".
            Assert.AreEqual("0 notes", AppStrings.NoteCount(0));
            Assert.AreEqual("2 notes", AppStrings.NoteCount(2));
            Assert.AreEqual("12 results", AppStrings.SearchResultsCount(12));
            Assert.AreEqual("3 days left in your Pro trial", AppStrings.BillingTrialBannerTitle(3));
            StringAssert.StartsWith(AppStrings.AccountConflicts(4), "4 notes were replaced");
        });
    }

    [TestMethod]
    public void Korean_reads_the_same_whatever_the_count()
    {
        InLanguage(AppLanguage.Korean, () =>
        {
            Assert.AreEqual("노트 1개", AppStrings.NoteCount(1));
            Assert.AreEqual("노트 0개", AppStrings.NoteCount(0));
            Assert.AreEqual("노트 5개", AppStrings.NoteCount(5));
            Assert.AreEqual("결과 1개", AppStrings.SearchResultsCount(1));

            // The singular key must exist in Korean too. Without it the lookup falls through to the
            // key name and the header would read "NoteCountFormatOne" on the one-note days.
            Assert.IsFalse(
                AppStrings.NoteCount(1).Contains("Format", StringComparison.Ordinal),
                "The Korean catalog is missing a singular key.");
        });
    }

    private static void InLanguage(AppLanguage language, Action body)
    {
        AppLanguage previous = LocalizationService.Instance.Language;
        LocalizationService.Instance.SetLanguage(language);
        try
        {
            body();
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previous);
        }
    }
}
