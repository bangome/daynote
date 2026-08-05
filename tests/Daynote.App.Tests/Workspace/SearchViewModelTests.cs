using Daynote.App.Search;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Search;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Workspace;

[TestClass]
public sealed class SearchViewModelTests
{
    private static readonly LocalDate Day = WorkspaceTestContext.Date("2026-07-20");

    [TestMethod]
    public async Task Debounce_CoalescesRapidKeystrokesIntoASingleSearch()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Day, "Note", "abcdef body");
        var counting = new CountingSearchRepository(new Daynote.Infrastructure.Search.SqliteSearchRepository(context.Database));
        var service = new SearchService(counting);
        var scheduler = new ManualSearchScheduler();
        using var vm = new SearchViewModel(service, new RecordingSearchActivation(), scheduler, TimeSpan.FromMilliseconds(200));

        vm.Query = "a";
        vm.Query = "ab";
        vm.Query = "abc";
        scheduler.ReleaseAll();
        await vm.Pending;

        Assert.AreEqual(3, scheduler.DelayCount, "Each keystroke schedules a debounce delay.");
        Assert.AreEqual(1, counting.SearchCount, "Only the final query should reach the repository.");
        Assert.IsGreaterThanOrEqualTo(1, vm.Results.Count);
    }

    [TestMethod]
    public async Task StaleSearch_OlderInFlightQueryDoesNotOverwriteNewerResults()
    {
        var gated = new GatedSearchRepository();
        var service = new SearchService(gated);
        using var vm = new SearchViewModel(
            service, new RecordingSearchActivation(), new ImmediateSearchScheduler(), TimeSpan.Zero);

        vm.Query = "old";
        vm.Query = "new";
        gated.Complete("new", Result("New note", SearchSourceType.Note));
        await vm.Pending;

        // The superseded "old" query resolving late must not replace the newer results.
        gated.Complete("old", Result("Old note", SearchSourceType.Note));

        Assert.AreEqual(1, vm.Results.Count);
        Assert.AreEqual("New note", vm.Results[0].Title);
    }

    [TestMethod]
    public async Task Paging_ReturnsDeterministicOrderedPagesOfFifty()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        var baseDate = new DateOnly(2026, 1, 1);
        for (int index = 0; index < 60; index++)
        {
            LocalDate date = WorkspaceTestContext.Date(
                baseDate.AddDays(index).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            await context.StoreNoteAsync(date, $"Title {index:D2}", "zebra body");
        }

        using var vm = NewViewModel(context);
        vm.Query = "zebra";
        await vm.Pending;

        Assert.AreEqual(50, vm.Results.Count, "Page size is 50.");
        Assert.IsTrue(vm.HasMore);
        Guid[] firstPage = [.. vm.Results.Select(r => r.SourceId)];

        await vm.LoadMoreAsync();

        Assert.AreEqual(60, vm.Results.Count);
        Assert.IsFalse(vm.HasMore);

        using var second = NewViewModel(context);
        second.Query = "zebra";
        await second.Pending;
        CollectionAssert.AreEqual(firstPage, second.Results.Select(r => r.SourceId).Take(50).ToArray(),
            "Ordering must be deterministic across identical queries.");
    }

    [TestMethod]
    public async Task Results_CarryNoteSourceBadge()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Day, "Meeting", "zebra in a note");

        using var vm = NewViewModel(context);
        vm.Query = "zebra";
        await vm.Pending;

        SearchResultViewModel note = vm.Results.Single(r => r.IsNote);
        Assert.AreEqual("노트", note.KindDisplay);
        Assert.AreEqual("Daynote.Style.SearchSource.Note", note.SourceStyleKey);
    }

    [TestMethod]
    public async Task LiteralQueries_KoreanAndPunctuationReturnLiteralMatchesWithNoError()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNotesOnDateAsync(Day, ("오늘 노트", "오늘 회의 메모"), ("검색", "검색 결과입니다"));
        NoteId percent = await context.StoreNoteAsync(WorkspaceTestContext.Date("2026-07-21"), "percent", "50% 완료");
        NoteId underscore = await context.StoreNoteAsync(WorkspaceTestContext.Date("2026-07-22"), "underscore", "snake_case value");
        await context.StoreNoteAsync(WorkspaceTestContext.Date("2026-07-23"), "andor", "A AND B OR C");
        await context.StoreNoteAsync(WorkspaceTestContext.Date("2026-07-24"), "injection", "\" OR 1=1 --");

        await AssertLiteral(context, "오", atLeast: 1);
        await AssertLiteral(context, "검색", atLeast: 1);
        await AssertLiteral(context, "AND", atLeast: 1);
        await AssertLiteral(context, "OR", atLeast: 1);
        await AssertLiteral(context, "\" OR 1=1 --", atLeast: 1);

        // Punctuation must be escaped, not treated as a LIKE wildcard.
        using var pctVm = NewViewModel(context);
        pctVm.Query = "%";
        await pctVm.Pending;
        Assert.AreNotEqual(SearchLoadState.Error, pctVm.LoadState);
        Assert.AreEqual(1, pctVm.Results.Count, "'%' must match only the literal percent item.");
        Assert.AreEqual(percent.Value, pctVm.Results[0].SourceId);

        using var underscoreVm = NewViewModel(context);
        underscoreVm.Query = "_";
        await underscoreVm.Pending;
        Assert.AreEqual(1, underscoreVm.Results.Count, "'_' must match only the literal underscore item.");
        Assert.AreEqual(underscore.Value, underscoreVm.Results[0].SourceId);
    }

    [TestMethod]
    public async Task EmptyQuery_ClearsResultsAndReturnsToIdle()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        await context.StoreNoteAsync(Day, "Note", "zebra body");
        using var vm = NewViewModel(context);
        vm.Query = "zebra";
        await vm.Pending;
        Assert.IsGreaterThanOrEqualTo(1, vm.Results.Count);

        vm.Query = "   ";
        await vm.Pending;

        Assert.AreEqual(0, vm.Results.Count);
        Assert.AreEqual(SearchLoadState.Idle, vm.LoadState);
        Assert.IsFalse(vm.HasMore);
    }

    private static SearchViewModel NewViewModel(WorkspaceTestContext context) =>
        new(context.SearchService, new RecordingSearchActivation(), new ImmediateSearchScheduler(), TimeSpan.Zero);

    private static async Task AssertLiteral(WorkspaceTestContext context, string query, int atLeast)
    {
        using var vm = NewViewModel(context);
        vm.Query = query;
        await vm.Pending;
        Assert.AreNotEqual(SearchLoadState.Error, vm.LoadState, $"Query '{query}' errored.");
        Assert.IsGreaterThanOrEqualTo(atLeast, vm.Results.Count, $"Query '{query}' returned no literal match.");
    }

    private static SearchResult Result(string title, SearchSourceType type) =>
        new(type, Guid.NewGuid(), Day, title, title, 0.0);
}
