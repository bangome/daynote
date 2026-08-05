using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Files;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Files;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Tests.Persistence;

namespace Daynote.Infrastructure.Tests.Notes;

[TestClass]
public sealed class CrossDateAggregationTests
{
    private static readonly DateTimeOffset Utc =
        DateTimeOffset.Parse("2026-03-01T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

    [TestMethod]
    public async Task Month_summary_aggregates_notes_and_files_per_date_within_month_bounds()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => Utc);
        var files = new SqliteDayFileRepository(fixture.Database, () => Utc);

        // Two notes on the 5th, one on the 20th; files on the 20th and 31st.
        await notes.CreateNoteAsync(Date("2026-03-05"), Id(1), Id(2));
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(3), Date("2026-03-20"), "T", "b", 0, IsNew: true, HasCustomTitle: true));
        await files.AddAsync(Guid.NewGuid(), Date("2026-03-20"), "a.txt", Asset("hashA", "aa/hashA.txt"));
        await files.AddAsync(Guid.NewGuid(), Date("2026-03-31"), "b.txt", Asset("hashB", "bb/hashB.txt"));
        // Boundary rows in adjacent months must be excluded.
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(4), Date("2026-02-28"), "T", "b", 0, IsNew: true, HasCustomTitle: true));
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(5), Date("2026-04-01"), "T", "b", 0, IsNew: true, HasCustomTitle: true));

        IReadOnlyList<DateContentSummary> summary = await notes.GetMonthContentSummaryAsync(2026, 3);

        CollectionAssert.AreEqual(
            new[] { Date("2026-03-05"), Date("2026-03-20"), Date("2026-03-31") },
            summary.Select(static s => s.Date).ToArray());
        Assert.AreEqual<DateContentSummary>(new(Date("2026-03-05"), 2, HasClipboard: false, HasFiles: false), summary[0]);
        Assert.AreEqual<DateContentSummary>(new(Date("2026-03-20"), 1, HasClipboard: false, HasFiles: true), summary[1]);
        Assert.AreEqual<DateContentSummary>(new(Date("2026-03-31"), 0, HasClipboard: false, HasFiles: true), summary[2]);
    }

    [TestMethod]
    public async Task Month_summary_of_an_empty_month_is_empty()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => Utc);
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(1), Date("2026-03-10"), "T", "b", 0, IsNew: true, HasCustomTitle: true));

        Assert.IsEmpty(await notes.GetMonthContentSummaryAsync(2026, 1));
    }

    [TestMethod]
    public async Task Month_summary_rejects_an_invalid_month()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => Utc);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            async () => await notes.GetMonthContentSummaryAsync(2026, 13));
    }

    [TestMethod]
    public async Task All_notes_enumerates_every_note_across_dates_ordered_by_date_desc_then_sort_order()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => Utc);
        await notes.CreateNoteAsync(Date("2026-03-05"), Id(1), Id(2));
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(3), Date("2026-03-20"), "커스텀", "todo body", 0, IsNew: true, HasCustomTitle: true));
        await notes.ToggleFavoriteAsync(Date("2026-03-20"), Id(3));

        IReadOnlyList<NoteSummary> all = await notes.GetAllNotesAsync();

        Assert.HasCount(3, all);
        Assert.AreEqual(Id(3).Value, all[0].Id);
        Assert.AreEqual(Date("2026-03-20"), all[0].LocalDate);
        Assert.AreEqual("커스텀", all[0].Title);
        Assert.IsTrue(all[0].IsFavorite);
        Assert.AreEqual("todo body", all[0].Body);
        Assert.AreEqual(Date("2026-03-05"), all[1].LocalDate);
        Assert.AreEqual(0, all[1].SortOrder);
        Assert.AreEqual("노트 1", all[1].Title);
        Assert.AreEqual(1, all[2].SortOrder);
        Assert.AreEqual("노트 2", all[2].Title);
        Assert.IsFalse(all[2].IsFavorite);
    }

    [TestMethod]
    public async Task All_notes_range_overload_bounds_inclusively()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => Utc);
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(1), Date("2026-03-05"), "T", "b", 0, IsNew: true, HasCustomTitle: true));
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(2), Date("2026-03-20"), "T", "b", 0, IsNew: true, HasCustomTitle: true));
        await notes.SaveNoteAsync(new NoteSaveRequest(Id(3), Date("2026-04-02"), "T", "b", 0, IsNew: true, HasCustomTitle: true));

        IReadOnlyList<NoteSummary> ranged = await notes.GetAllNotesAsync(Date("2026-03-20"), Date("2026-04-02"));

        CollectionAssert.AreEqual(
            new[] { Date("2026-04-02"), Date("2026-03-20") },
            ranged.Select(static n => n.LocalDate).ToArray());
    }

    [TestMethod]
    public async Task Cross_date_reads_stay_read_only_and_do_not_block_against_concurrent_writes()
    {
        await using var fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => Utc);

        Task writer = Task.Run(async () =>
        {
            for (int day = 1; day <= 28; day++)
            {
                await notes.SaveNoteAsync(new NoteSaveRequest(
                    Id(1000 + day), Date($"2026-05-{day:D2}"), "T", "b", 0, IsNew: true, HasCustomTitle: true));
            }
        });

        for (int iteration = 0; iteration < 40; iteration++)
        {
            await notes.GetAllNotesAsync();
            await notes.GetMonthContentSummaryAsync(2026, 5);
        }

        await writer;
        Assert.HasCount(28, await notes.GetAllNotesAsync(Date("2026-05-01"), Date("2026-05-28")));
    }

    private static LocalDate Date(string iso) => LocalDate.Parse(iso).Value;

    private static NoteId Id(int suffix) => NoteId.Create(Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}")).Value;

    private static PreparedFileAsset Asset(string hash, string relativePath) =>
        new(hash, relativePath, 8, CreatedNew: true);
}
