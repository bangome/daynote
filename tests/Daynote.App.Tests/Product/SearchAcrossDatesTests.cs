using Daynote.App.Shell.Product;
using Daynote.App.Tests.Workspace;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Product;

/// <summary>
/// A search whose matches span several dates returns rows.
/// </summary>
/// <remarks>
/// It returned nothing. The dropdown derives a 날짜 row per matching date and sorted those dates —
/// and <c>LocalDate</c> implemented no comparison, so <c>OrderByDescending</c> threw as soon as
/// there were two of them. The query runs fire-and-forget, so the exception vanished, the rows were
/// never added, and <c>NoResults</c> — set at the end of the query — never turned on either: an
/// empty panel with no message, indistinguishable from a broken one.
/// <para>
/// One date worked, because a sorter with a single element never compares anything. That is why this
/// test uses three, and why the single-date case is worth keeping beside it.
/// </para>
/// </remarks>
[TestClass]
public sealed class SearchAcrossDatesTests
{
    [TestMethod]
    public async Task Matches_on_several_dates_all_become_rows()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNotesOnDateAsync(WorkspaceTestContext.Date("2026-07-18"), ("첫 회의", "회의록 초안"));
        await context.StoreNotesOnDateAsync(WorkspaceTestContext.Date("2026-07-19"), ("두 번째 회의", "회의 후속"));
        await context.StoreNotesOnDateAsync(WorkspaceTestContext.Date("2026-07-20"), ("세 번째 회의", "회의 결론"));

        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            await harness.Shell.Search.SearchNowAsync("회의");

            Assert.IsFalse(harness.Shell.Search.Failed, "The query failed.");
            Assert.IsFalse(harness.Shell.Search.NoResults, "Three notes match, so this is not an empty result.");

            // Three note rows and three derived date rows.
            Assert.AreEqual(6, harness.Shell.Search.Results.Count);
            Assert.AreEqual(3, harness.Shell.Search.Results.Count(row => row.Kind == AppStringsFor.Date));

            // Newest date first, as the panel promises.
            string[] dateRows = [.. harness.Shell.Search.Results.Where(row => row.Kind == AppStringsFor.Date).Select(row => row.Title)];
            CollectionAssert.AreEqual(dateRows.OrderByDescending(t => t, StringComparer.Ordinal).ToArray(), dateRows,
                "The derived date rows are not newest-first.");
        }
    }

    [TestMethod]
    public async Task A_match_on_one_date_still_works()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNotesOnDateAsync(WorkspaceTestContext.Date("2026-07-20"), ("회의록", "안건"));

        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            await harness.Shell.Search.SearchNowAsync("회의");

            Assert.IsFalse(harness.Shell.Search.Failed);
            Assert.AreEqual(2, harness.Shell.Search.Results.Count, "One note row and one date row.");
        }
    }

    [TestMethod]
    public async Task A_query_that_matches_nothing_says_so()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNotesOnDateAsync(WorkspaceTestContext.Date("2026-07-20"), ("회의록", "안건"));

        WorkspaceTestContext.ProductShellHarness harness = context.BuildProductShell();
        await using (harness)
        {
            await harness.Shell.Search.SearchNowAsync("이런내용은없다");

            Assert.IsTrue(harness.Shell.Search.NoResults, "An empty result has to be stated, not left blank.");
            Assert.IsFalse(harness.Shell.Search.Failed);
            Assert.AreEqual(0, harness.Shell.Search.Results.Count);
        }
    }

    /// <summary>The 날짜 row's kind label, as the view model builds it.</summary>
    private static class AppStringsFor
    {
        internal static string Date => Daynote.App.Localization.AppStrings.SearchKindDate;
    }
}
