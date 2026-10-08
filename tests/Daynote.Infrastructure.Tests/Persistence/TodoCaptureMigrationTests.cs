using Daynote.Core.Agenda;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Infrastructure.Agenda;
using Daynote.Infrastructure.Notes;
using Daynote.Infrastructure.Persistence;
using Daynote.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace Daynote.Infrastructure.Tests.Persistence;

/// <summary>
/// The one-time <c>-[ ]</c> walk (docs/TODOS.md §8), and the runner support that records it.
/// </summary>
[TestClass]
public sealed class TodoCaptureMigrationTests
{
    private static readonly LocalDate Date = LocalDate.Parse("2026-08-20").Value;

    [TestMethod]
    public void It_is_not_in_the_shipping_set_yet()
    {
        // §12: until desktop and phone switch their readers in the same release, a device that
        // migrated would hold entities while its phone still parsed the same lines out of the
        // body — every task twice, and every reminder twice. Step 3 adds it; this is the guard
        // that nobody adds it early by accident.
        Assert.AreEqual(6, MigrationRunner.FromEmbeddedResources().LatestVersion);
        Assert.IsTrue(TodoCaptureMigration.Version > MigrationRunner.FromEmbeddedResources().LatestVersion);
    }

    [TestMethod]
    public async Task Every_checkbox_line_becomes_a_to_do_and_the_body_is_left_alone()
    {
        await using TestDatabase fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => DateTimeOffset.UtcNow);

        NoteId id = await Write(notes, 1, "- [ ] 예산안 초안\n본문 한 줄\n- [x] 영수증 정리 (8/25)");

        await Migrate(fixture);

        var agenda = new SqliteAgendaRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        IReadOnlyList<AgendaItem> created = await agenda.GetForListAsync(AgendaList.DefaultId);
        Assert.AreEqual(2, created.Count);

        AgendaItem draft = created.Single(item => item.Title == "예산안 초안");
        Assert.AreEqual(AgendaKind.Task, draft.Kind);
        Assert.AreEqual(AgendaStatus.NeedsAction, draft.Status);
        Assert.AreEqual(id.Value, draft.SourceNoteId);

        AgendaItem receipts = created.Single(item => item.Title == "영수증 정리");
        Assert.AreEqual(AgendaStatus.Completed, receipts.Status);
        Assert.IsNotNull(receipts.CompletedUtc);
        Assert.AreEqual(new WallClock(new DateTime(2026, 8, 25, 23, 59, 0)), receipts.DueAt);

        // Exactly what these lines did as text: the one with a (M/D) stamp reminded, the one
        // without never did. Giving every migrated row the default alert instead would start
        // pinging people about undated checkboxes that have sat in today's note for months.
        CollectionAssert.AreEqual(AgendaAlert.Default.ToArray(), receipts.AlarmLeadMinutes.ToArray());
        Assert.IsEmpty(draft.AlarmLeadMinutes);

        // §8: the body is the user's own writing and the migration does not rewrite it. Afterwards
        // these lines are text that looks like a checkbox, and nothing parses them.
        NoteSummary note = (await notes.GetAllNotesAsync()).Single();
        Assert.AreEqual("- [ ] 예산안 초안\n본문 한 줄\n- [x] 영수증 정리 (8/25)", note.Body);
    }

    [TestMethod]
    public async Task A_task_with_no_due_date_lands_on_the_day_its_note_was_written()
    {
        await using TestDatabase fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        await Write(notes, 1, "- [ ] 예산안 초안");

        await Migrate(fixture);

        var agenda = new SqliteAgendaRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        // Not today. Giving every historical line a start of today would empty years of notes
        // onto this morning's panel.
        Assert.AreEqual(1, (await agenda.GetForDateAsync(new DateOnly(2026, 8, 20))).Count);
        Assert.AreEqual(0, (await agenda.GetForDateAsync(DateOnly.FromDateTime(DateTime.Today))).Count);
    }

    [TestMethod]
    public async Task Running_it_twice_changes_nothing()
    {
        await using TestDatabase fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        await Write(notes, 1, "- [ ] 예산안 초안\n- [ ] 전화");

        await Migrate(fixture);
        await Migrate(fixture);

        var agenda = new SqliteAgendaRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        Assert.AreEqual(2, (await agenda.GetForListAsync(AgendaList.DefaultId)).Count);
    }

    [TestMethod]
    public async Task A_task_the_user_has_since_edited_is_left_as_it_is()
    {
        await using TestDatabase fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        await Write(notes, 1, "- [ ] 예산안 초안");

        await Migrate(fixture);

        var agenda = new SqliteAgendaRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        AgendaItem created = (await agenda.GetForListAsync(AgendaList.DefaultId)).Single();
        await agenda.SaveAsync(created with
        {
            Title = "예산안 최종본",
            UpdatedUtc = DateTimeOffset.UtcNow,
        });

        // The same row arriving again — a second device that migrated first and synced it over,
        // or a re-run. Overwriting it would undo real work.
        await Migrate(fixture);

        Assert.AreEqual("예산안 최종본", (await agenda.GetAsync(created.Id))!.Title);
    }

    [TestMethod]
    public async Task The_migrated_tasks_are_queued_for_sync()
    {
        await using TestDatabase fixture = TestDatabase.Create();
        fixture.Database.Initialize();
        var notes = new SqliteNoteRepository(fixture.Database, () => DateTimeOffset.UtcNow);
        await Write(notes, 1, "- [ ] 예산안 초안");

        await Migrate(fixture);

        // 006's triggers fire on the migration's own inserts, so nothing has to remember to
        // enrol them afterwards. Both devices derive the same ids, so the two pushes converge on
        // one row rather than duplicating.
        using SqliteConnection connection = fixture.Database.OpenReadConnection();
        Assert.AreEqual(
            1L,
            TestDatabase.ScalarInt64(
                connection,
                "SELECT COUNT(*) FROM sync_outbox WHERE entity = 'agenda_item';"));
    }

    [TestMethod]
    public void A_code_step_is_recorded_once_like_any_other_migration()
    {
        // Isolated from the app's schema on purpose: what is under test is the runner, not the
        // walk. Two steps, the second of them code.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        int runs = 0;
        var runner = new MigrationRunner(
        [
            new SqliteMigration(
                1,
                "base",
                """
                CREATE TABLE schema_versions (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    applied_utc TEXT NOT NULL);
                CREATE TABLE seen (note TEXT NOT NULL);
                """),
            new SqliteMigration(2, "walk", (c, t) =>
            {
                runs += 1;
                using SqliteCommand command = c.CreateCommand();
                command.Transaction = t;
                command.CommandText = "INSERT INTO seen(note) VALUES ('ran');";
                command.ExecuteNonQuery();
            }),
        ]);

        Assert.AreEqual(2, runner.Apply(connection));
        Assert.AreEqual(2, runner.Apply(connection));

        Assert.AreEqual(1, runs);
        Assert.AreEqual(1L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM seen;"));
        Assert.AreEqual(
            1L,
            TestDatabase.ScalarInt64(
                connection,
                "SELECT COUNT(*) FROM schema_versions WHERE version = 2 AND name = 'walk';"));
    }

    [TestMethod]
    public void A_code_step_that_throws_rolls_back_and_stays_unrecorded()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var runner = new MigrationRunner(
        [
            new SqliteMigration(
                1,
                "base",
                """
                CREATE TABLE schema_versions (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL UNIQUE,
                    applied_utc TEXT NOT NULL);
                CREATE TABLE seen (note TEXT NOT NULL);
                """),
            new SqliteMigration(2, "walk", (c, t) =>
            {
                using SqliteCommand command = c.CreateCommand();
                command.Transaction = t;
                command.CommandText = "INSERT INTO seen(note) VALUES ('half done');";
                command.ExecuteNonQuery();
                throw new InvalidOperationException("a line that would not parse");
            }),
        ]);

        Assert.ThrowsExactly<MigrationException>(() => runner.Apply(connection));

        // Half a migration is the one outcome that must not survive: the next launch has to find
        // the database exactly as it was and try the whole thing again.
        Assert.AreEqual(0L, TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM seen;"));
        Assert.AreEqual(
            0L,
            TestDatabase.ScalarInt64(connection, "SELECT COUNT(*) FROM schema_versions WHERE version = 2;"));
    }

    private static async Task<NoteId> Write(SqliteNoteRepository notes, int suffix, string body)
    {
        NoteId id = NoteId.Create(Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}")).Value;
        await notes.CreateNoteAsync(Date, default, id);
        await notes.SaveNoteAsync(
            new NoteSaveRequest(id, Date, "메모", body, 0, IsNew: false, HasCustomTitle: true));
        return id;
    }

    private static async Task Migrate(TestDatabase fixture) =>
        await fixture.Database.WriteAsync<object?>(
            (connection, transaction, _) =>
            {
                TodoCaptureMigration.Migration.Apply!(connection, transaction);
                return null;
            });
}
