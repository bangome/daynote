using Daynote.App.Account;
using Daynote.App.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Account;

/// <summary>
/// The plan table says what the two plans are, and says only what is true.
/// </summary>
/// <remarks>
/// A comparison table is the one screen where a wrong row is a promise the product does not keep, so
/// the rows are pinned: what the free plan includes, what it does not, and that the difference is
/// the one the sync blurb already states.
/// </remarks>
[TestClass]
public sealed class PlanComparisonTests
{
    [TestCleanup]
    public void RestoreKorean() => LocalizationService.Instance.SetLanguage(AppLanguage.Korean);

    [TestMethod]
    public void The_paid_plan_adds_exactly_one_thing()
    {
        PlanComparisonRow[] onlyPro = [.. PlanComparison.Rows.Where(row => row.NotInFree)];

        // Signing in syncs notes for nothing; the subscription is for images and files. A second
        // row here means either the product changed or the table is claiming something.
        Assert.AreEqual(1, onlyPro.Length, $"Pro claims {onlyPro.Length} things over free.");
        StringAssert.Contains(onlyPro.Single().Label, "파일", "The row Pro adds is not the file one.");
    }

    [TestMethod]
    public void Every_row_reads_in_both_languages()
    {
        foreach (AppLanguage language in new[] { AppLanguage.Korean, AppLanguage.English })
        {
            LocalizationService.Instance.SetLanguage(language);
            foreach (PlanComparisonRow row in PlanComparison.Rows)
            {
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(row.Label),
                    $"A plan row has no label in {language}.");
                Assert.IsFalse(
                    row.Label.StartsWith("PlanRow", StringComparison.Ordinal),
                    $"'{row.Label}' is a key, not copy: the {language} catalog is missing it.");
            }
        }
    }

    [TestMethod]
    public void A_row_is_either_in_the_free_plan_or_it_is_not()
    {
        foreach (PlanComparisonRow row in PlanComparison.Rows)
        {
            Assert.AreNotEqual(row.InFree, row.NotInFree, $"'{row.Label}' draws both a tick and a dash.");
        }
    }

    [TestMethod]
    public void The_table_has_enough_rows_to_be_a_comparison()
    {
        Assert.IsGreaterThanOrEqualTo(4, PlanComparison.Rows.Count, "A two-row table is a price tag, not a comparison.");
    }
}
