using Daynote.Core.Domain;

namespace Daynote.Core.Tests;

[TestClass]
public sealed class LocalDateTests
{
    [TestMethod]
    public void StrictIsoDateRoundTripsWithoutDependingOnMachineTimeZone()
    {
        DomainResult<LocalDate> parsed = LocalDate.Parse("2024-02-29");

        Assert.IsTrue(parsed.IsSuccess);
        LocalDate date = parsed.Value;
        TimeZoneInfo.ClearCachedData();

        Assert.AreEqual("2024-02-29", date.ToString());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("2026-02-29")]
    [DataRow("2026-07-15T00:00:00")]
    [DataRow("2026-7-15")]
    [DataRow(" 2026-07-15")]
    [DataRow("2026/07/15")]
    public void NonCanonicalOrImpossibleDateIsRejected(string value)
    {
        DomainResult<LocalDate> parsed = LocalDate.Parse(value);

        Assert.IsFalse(parsed.IsSuccess);
        Assert.AreEqual(DomainErrorCode.InvalidLocalDate, parsed.Error.Code);
    }
}
