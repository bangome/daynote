using Daynote.Core.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Core.Tests;

/// <summary>
/// A date has an order.
/// </summary>
/// <remarks>
/// It did not, and nothing said so at compile time: <c>OrderBy</c> on a type implementing neither
/// comparison interface throws <see cref="ArgumentException"/> at run time, and only once it has two
/// elements to compare. So sorting one date worked and sorting two threw — which is exactly how the
/// unified search came back blank for any query whose matches spanned more than a single day, with
/// the exception swallowed inside a fire-and-forget query.
/// </remarks>
[TestClass]
public sealed class LocalDateOrderingTests
{
    [TestMethod]
    public void Dates_sort_chronologically()
    {
        LocalDate[] dates =
        [
            Date("2026-07-20"),
            Date("2025-12-31"),
            Date("2026-07-19"),
            Date("2026-08-01"),
        ];

        CollectionAssert.AreEqual(
            new[] { Date("2025-12-31"), Date("2026-07-19"), Date("2026-07-20"), Date("2026-08-01") },
            dates.OrderBy(static date => date).ToArray());

        CollectionAssert.AreEqual(
            new[] { Date("2026-08-01"), Date("2026-07-20"), Date("2026-07-19"), Date("2025-12-31") },
            dates.OrderByDescending(static date => date).ToArray());
    }

    [TestMethod]
    public void The_comparison_agrees_with_the_calendar()
    {
        LocalDate earlier = Date("2026-07-19");
        LocalDate later = Date("2026-07-20");

        Assert.IsLessThan(0, earlier.CompareTo(later));
        Assert.IsGreaterThan(0, later.CompareTo(earlier));
        Assert.AreEqual(0, later.CompareTo(later));

        Assert.IsTrue(earlier < later);
        Assert.IsTrue(earlier <= later);
        Assert.IsTrue(later > earlier);
        Assert.IsTrue(later >= earlier);
        Assert.IsFalse(later < earlier);
    }

    [TestMethod]
    public void Sorting_across_years_and_months_does_not_fall_back_to_the_text()
    {
        // A day-of-month that sorts before another lexicographically but after it by date; an
        // ISO-string comparison happens to agree, so the point is that the comparison is the date's.
        LocalDate[] dates = [Date("2026-01-02"), Date("2025-12-09")];

        Assert.AreEqual(Date("2025-12-09"), dates.Min());
        Assert.AreEqual(Date("2026-01-02"), dates.Max());
    }

    [TestMethod]
    public void Comparing_with_another_type_is_rejected_rather_than_guessed()
    {
        object date = Date("2026-07-20");

        Assert.ThrowsExactly<ArgumentException>(() => ((IComparable)date).CompareTo("2026-07-20"));
        Assert.IsGreaterThan(0, ((IComparable)date).CompareTo(null));
    }

    private static LocalDate Date(string iso) => LocalDate.Parse(iso).Value;
}
