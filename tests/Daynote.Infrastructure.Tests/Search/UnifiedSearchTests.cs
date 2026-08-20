using System.Reflection;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Search;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Search;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Search;

[TestClass]
public sealed class UnifiedSearchTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-07-15").Value;
    private static readonly DateTimeOffset Utc = DateTimeOffset.Parse("2026-07-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

    [TestMethod]
    public void SearchQuery_normalizes_NFC_and_invariant_case_without_erasing_literal_spaces()
    {
        SearchQuery query = SearchQuery.Create(" Cafe\u0301 검색 ");

        Assert.AreEqual(" Café 검색 ", query.NormalizedText);
        Assert.AreEqual(" CAFÉ 검색 ", query.FoldedText);
        Assert.AreEqual(9, query.UnicodeScalarCount);
        Assert.AreEqual(SearchStrategy.Trigram, query.Strategy);
        Assert.IsFalse(query.IsEmpty);
        Assert.IsTrue(SearchQuery.Create(" \t\r\n").IsEmpty);
    }

    [TestMethod]
    public async Task Search_literal_matrix_handles_Korean_Latin_NFC_spaces_and_FTS_syntax_tokens()
    {
        await using SearchFixture fixture = SearchFixture.Create();
        Guid noteId = Id(1);
        await fixture.SaveNoteAsync(noteId, "제목 MixedCase Cafe\u0301", "오 한 검색 검색어 two words a\"b 100% mark_under dash-value AND OR semi;colon slash\\path [brackets] empty() star* caret^ colon:");

        string[] queries = ["오", "검색", "검색어", "mixedcase", "CAFÉ", "two words", "a\"b", "%", "_", "-", "AND", "OR", ";", "\\", "[", "()", "star*", "caret^", "colon:"];
        foreach (string query in queries)
        {
            SearchPage page = await fixture.Search.SearchAsync(query);
            Assert.HasCount(1, page.Results, $"Literal query failed: {query}");
            Assert.AreEqual(noteId, page.Results[0].SourceId);
            Assert.AreEqual(SearchSourceType.Note, page.Results[0].SourceType);
            Assert.AreEqual(Date, page.Results[0].LocalDate);
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.Results[0].Snippet));
        }

        Assert.IsEmpty((await fixture.Search.SearchAsync(string.Empty)).Results);
        Assert.IsEmpty((await fixture.Search.SearchAsync("   ")).Results);
        SearchResult normalized = (await fixture.Search.SearchAsync("CAFÉ")).Results.Single();
        Assert.AreEqual("제목 MixedCase Café", normalized.Title);
        StringAssert.Contains(normalized.Snippet, "오 한 검색");
    }

    [TestMethod]
    public async Task Search_snippet_is_windowed_around_the_match_deep_in_the_body()
    {
        await using SearchFixture fixture = SearchFixture.Create();
        string lead = new('가', 200); // pushes the keyword well past the start of the body
        await fixture.SaveNoteAsync(Id(50), "긴 노트", lead + " 회의 후 연락 주세요 " + new string('나', 200));

        SearchResult result = (await fixture.Search.SearchAsync("연락")).Results.Single();

        StringAssert.Contains(result.Snippet, "연락", "The snippet must include the matched keyword.");
        StringAssert.StartsWith(result.Snippet, "…", "A deep match is windowed, not shown from the body start.");
        Assert.IsTrue(result.Snippet.Length <= 162, "The snippet stays within the window (plus ellipses).");
    }

    [TestMethod]
    public async Task Search_injection_shaped_text_is_quoted_and_matches_only_the_actual_literal()
    {
        await using SearchFixture fixture = SearchFixture.Create();
        const string literal = "\" OR 1=1 -- %_";
        Guid expectedId = Id(2);
        await fixture.SaveNoteAsync(expectedId, "literal", literal);
        await fixture.AppendNoteAsync(Id(3), "decoy", "OR 1=1 and percent underscore are separate");

        SearchPage page = await fixture.Search.SearchAsync(literal);

        Assert.HasCount(1, page.Results);
        Assert.AreEqual(expectedId, page.Results[0].SourceId);
        Assert.AreEqual(SearchSourceType.Note, page.Results[0].SourceType);
        fixture.AssertIndexCounts(2);
    }

    [TestMethod]
    public async Task Search_mutations_are_atomic_and_survive_physical_restart()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-task7-search", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Guid noteId = Id(10);
            await using (SearchFixture first = SearchFixture.Create(root))
            {
                NoteSaveReceipt created = await first.SaveNoteAsync(noteId, "note", "before-token");
                Assert.HasCount(1, (await first.Search.SearchAsync("before-token")).Results);
                first.AssertIndexCounts(1);

                await first.Notes.SaveNoteAsync(new NoteSaveRequest(
                    NoteId.Create(noteId).Value, Date, "note", "after-token", created.Revision,
                    IsNew: false, HasCustomTitle: true));
                Assert.IsEmpty((await first.Search.SearchAsync("before-token")).Results);
                Assert.HasCount(1, (await first.Search.SearchAsync("after-token")).Results);
                first.AssertIndexCounts(1);
            }

            await using SearchFixture restarted = SearchFixture.Create(root);
            SearchPage afterRestart = await restarted.Search.SearchAsync("after-token");
            Assert.HasCount(1, afterRestart.Results);
            Assert.AreEqual(noteId, afterRestart.Results[0].SourceId);
            await restarted.Notes.DeleteNoteAsync(Date, NoteId.Create(noteId).Value);
            Assert.IsEmpty((await restarted.Search.SearchAsync("after-token")).Results);
            restarted.AssertIndexCounts(0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Search_v2_migration_rebuilds_missing_note_and_clipboard_documents()
    {
        string root = Path.Combine(Path.GetTempPath(), "daynote-task7-migration", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "daynote.db");
        Directory.CreateDirectory(root);
        try
        {
            var factory = new SqliteConnectionFactory(new SqliteDatabaseOptions(path));
            using (SqliteConnection connection = factory.OpenConnection())
            {
                string resource = typeof(MigrationRunner).Assembly.GetManifestResourceNames()
                    .Single(static name => name.EndsWith(".Migrations.001_initial.sql", StringComparison.Ordinal));
                using Stream stream = typeof(MigrationRunner).Assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                new MigrationRunner([new SqliteMigration(1, "initial", reader.ReadToEnd())]).Apply(connection);
                using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText =
                    "INSERT INTO notes(id,local_date,title,body,sort_order,revision,created_utc,updated_utc) VALUES($note,$date,'Café','migration-note',0,0,$utc,$utc);" +
                    "INSERT INTO clipboard_items(id,local_date,captured_utc,sequence_number,kind,text_value,asset_hash,payload_hash,byte_length) VALUES($clip,$date,$utc,1,'text','migration-clipboard',NULL,'hash',19);";
                seed.Parameters.AddWithValue("$note", Id(20).ToString("D"));
                seed.Parameters.AddWithValue("$clip", Id(21).ToString("D"));
                seed.Parameters.AddWithValue("$date", Date.ToString());
                seed.Parameters.AddWithValue("$utc", Utc.ToString("O"));
                seed.ExecuteNonQuery();
            }

            await using var database = new SqliteDatabase(new(path));
            DatabaseInitializationResult initialized = database.Initialize();
            var service = new SearchService(new SqliteSearchRepository(database));

            Assert.AreEqual(4, initialized.SchemaVersion);
            Assert.AreEqual(Id(20), (await service.SearchAsync("CAFÉ")).Results.Single().SourceId);
            Assert.AreEqual(Id(21), (await service.SearchAsync("migration-clipboard")).Results.Single().SourceId);
            DatabaseIntegrityResult integrity = database.CheckIntegrity();
            Assert.IsTrue(integrity.IsValid);
            Assert.AreEqual(2, integrity.SourceDocumentCount);
            Assert.AreEqual(2, integrity.FtsDocumentCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Search_pages_fifty_with_deterministic_score_date_type_and_id_ties()
    {
        await using SearchFixture fixture = SearchFixture.Create();
        Guid[] ids = Enumerable.Range(100, 55).Select(Id).ToArray();
        for (int index = 0; index < ids.Length; index++)
        {
            if (index == 0)
            {
                await fixture.SaveNoteAsync(ids[index], "Page", "needle");
            }
            else
            {
                await fixture.AppendNoteAsync(ids[index], "Page", "needle");
            }
        }

        SearchPage first = await fixture.Search.SearchAsync("needle", pageNumber: 0);
        SearchPage second = await fixture.Search.SearchAsync("needle", pageNumber: 1);

        Assert.HasCount(50, first.Results);
        Assert.HasCount(5, second.Results);
        Assert.IsTrue(first.HasMore);
        Assert.IsFalse(second.HasMore);
        CollectionAssert.AreEqual(ids.Take(50).ToArray(), first.Results.Select(static result => result.SourceId).ToArray());
        CollectionAssert.AreEqual(ids.Skip(50).ToArray(), second.Results.Select(static result => result.SourceId).ToArray());
        Assert.AreEqual(55, first.Results.Concat(second.Results).Select(static result => result.SourceId).Distinct().Count());
    }

    [TestMethod]
    public async Task Search_orders_score_then_date_and_stable_id()
    {
        await using SearchFixture fixture = SearchFixture.Create();
        LocalDate laterDate = LocalDate.Parse("2026-07-16").Value;
        Guid earlierNote = Id(30);
        Guid laterNoteA = Id(32);
        Guid laterNoteB = Id(33);
        await fixture.SaveNoteAsync(earlierNote, Date, "Note", "tie");
        await fixture.SaveNoteAsync(laterNoteA, laterDate, "Note", "tie");
        await fixture.AppendNoteAsync(laterNoteB, laterDate, "Note", "tie");

        SearchPage page = await fixture.Search.SearchAsync("tie");

        CollectionAssert.AreEqual(
            new[] { laterNoteA, laterNoteB, earlierNote },
            page.Results.Select(static result => result.SourceId).ToArray());
        CollectionAssert.AreEqual(
            new[] { SearchSourceType.Note, SearchSourceType.Note, SearchSourceType.Note },
            page.Results.Select(static result => result.SourceType).ToArray());
        fixture.AssertIndexCounts(3);
    }

    [TestMethod]
    public async Task Search_cancelled_request_leaves_index_clean_and_a_later_search_resumes()
    {
        await using SearchFixture fixture = SearchFixture.Create();
        Guid noteId = Id(40);
        await fixture.SaveNoteAsync(noteId, "cancel", "resume-token");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await fixture.Search.SearchAsync("resume-token", cancellationToken: cancellation.Token));

        SearchPage resumed = await fixture.Search.SearchAsync("resume-token");
        Assert.HasCount(1, resumed.Results);
        Assert.AreEqual(noteId, resumed.Results[0].SourceId);
        fixture.AssertIndexCounts(1);
    }

    private static Guid Id(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");
}
